using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace SpartanVentasApi.Controllers
{
    [ApiController]
    [Route("api/feedback")]
    public class FeedbackController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        public FeedbackController(IConfiguration cfg) => _cfg = cfg;

        private string ConnStr => _cfg.GetConnectionString("SAP")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:SAP");

        private int? GetSlpCode()
        {
            var v =
                User.FindFirst("slpCode")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            return int.TryParse(v, out var n) ? n : null;
        }

        private string GetLogin()
        {
            return
                User.FindFirst("login")?.Value
                ?? User.FindFirst("username")?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? User.Identity?.Name
                ?? User.FindFirst("unique_name")?.Value
                ?? User.FindFirst("name")?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "";
        }

        public record CreatePostRequest(string Tipo, string Modulo, string Titulo, string Mensaje);


        // =================================================================================================

        [Authorize]
        [HttpGet("posts")]
        public async Task<IActionResult> GetPosts([FromQuery] int skip = 0, [FromQuery] int take = 30)
        {
            if (take is < 1 or > 100) take = 30;
            if (skip < 0) skip = 0;

            await using var cn = new SqlConnection(ConnStr);
            await cn.OpenAsync();

            var sql = @"
SELECT
    P.Id,
    P.CreatedAt,
    P.CreatedBySlpCode,
    U.Login,
    U.PhotoUrl,
    P.Tipo,
    P.Modulo,
    P.Titulo,
    P.Mensaje
FROM dbo.ApiFeedbackPosts P
LEFT JOIN dbo.ApiUserProfile U ON U.SlpCode = P.CreatedBySlpCode
WHERE P.IsHidden = 0
ORDER BY P.CreatedAt DESC
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@skip", skip);
            cmd.Parameters.AddWithValue("@take", take);

            var rows = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                rows.Add(new
                {
                    id = rd.GetInt32(0),
                    createdAt = rd.GetDateTime(1),
                    slpCode = rd.GetInt32(2),
                    login = rd.IsDBNull(3) ? $"SlpCode {rd.GetInt32(2)}" : rd.GetString(3),
                    photoUrl = rd.IsDBNull(4) ? null : rd.GetString(4),
                    tipo = rd.GetString(5),
                    modulo = rd.GetString(6),
                    titulo = rd.GetString(7),
                    mensaje = rd.GetString(8)
                });
            }

            return Ok(rows);
        }



        // =========================================================================
        [Authorize]
        [HttpPost("posts")]
        public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest req)
        {
            var slpCode = GetSlpCode();
            if (slpCode is null) return Unauthorized("No se encontró slpCode en el token.");

            var tipo = (req.Tipo ?? "").Trim().ToUpperInvariant();
            var modulo = (req.Modulo ?? "").Trim();
            var titulo = (req.Titulo ?? "").Trim();
            var mensaje = (req.Mensaje ?? "").Trim();

            if (string.IsNullOrWhiteSpace(titulo) || titulo.Length < 3) return BadRequest("Título inválido.");
            if (string.IsNullOrWhiteSpace(mensaje) || mensaje.Length < 3) return BadRequest("Mensaje inválido.");
            if (tipo is not ("MEJORA" or "BUG" or "DUDA")) tipo = "MEJORA";
            if (string.IsNullOrWhiteSpace(modulo)) modulo = "General";

            await using var cn = new SqlConnection(ConnStr);
            await cn.OpenAsync();

            // ✅ Insert oculto (NO se publica)
            var sql = @"
INSERT INTO dbo.ApiFeedbackPosts
(
    CreatedAt,
    CreatedBySlpCode,
    Tipo,
    Modulo,
    Titulo,
    Mensaje,
    IsHidden
)
OUTPUT INSERTED.Id, INSERTED.CreatedAt
VALUES
(
    SYSUTCDATETIME(),
    @slp,
    @tipo,
    @mod,
    @tit,
    @msg,
    1
);
";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@slp", slpCode.Value);
            cmd.Parameters.AddWithValue("@tipo", tipo);
            cmd.Parameters.AddWithValue("@mod", modulo);
            cmd.Parameters.AddWithValue("@tit", titulo);
            cmd.Parameters.AddWithValue("@msg", mensaje);

            int newId;
            DateTime createdAt;

            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                await rd.ReadAsync();
                newId = rd.GetInt32(0);
                createdAt = rd.GetDateTime(1);
            }

            // Obtener perfil (para correo y respuesta)
            string login = $"SlpCode {slpCode.Value}";
            string? photoUrl = null;

            await using (var profileCmd = new SqlCommand(@"
SELECT TOP 1 Login, PhotoUrl
FROM dbo.ApiUserProfile
WHERE SlpCode = @slp;
", cn))
            {
                profileCmd.Parameters.AddWithValue("@slp", slpCode.Value);

                await using var rd2 = await profileCmd.ExecuteReaderAsync();
                if (await rd2.ReadAsync())
                {
                    login = rd2.IsDBNull(0) ? login : rd2.GetString(0);
                    photoUrl = rd2.IsDBNull(1) ? null : rd2.GetString(1);
                }
            }

            // ✅ Enviar correo (no bloquear al usuario; si falla no rompe)
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendFeedbackEmailAsync(
                        id: newId,
                        createdAtUtc: createdAt,
                        slpCode: slpCode.Value,
                        login: login,
                        tipo: tipo,
                        modulo: modulo,
                        titulo: titulo,
                        mensaje: mensaje
                    );
                }
                catch
                {
                    // opcional: log
                }
            });

            return Ok(new
            {
                id = newId,
                createdAt,
                slpCode = slpCode.Value,
                login,
                photoUrl,
                tipo,
                modulo,
                titulo,
                mensaje,
                sentTo = "informatica@spartan.cl",
                isHidden = true
            });
        }







        // =========================
        // Email helper
        // =========================
        private async Task SendFeedbackEmailAsync(
    int id,
    DateTime createdAtUtc,
    int slpCode,
    string login,
    string tipo,
    string modulo,
    string titulo,
    string mensaje)
        {
            // appsettings.json => Email:...
            var host = _cfg["Email:SmtpHost"];
            var portStr = _cfg["Email:SmtpPort"];
            var user = _cfg["Email:SmtpUser"];
            var pass = _cfg["Email:SmtpPass"];
            var enableSslStr = _cfg["Email:EnableSsl"];
            var from = _cfg["Email:From"] ?? user;
            var to = _cfg["Email:To"] ?? "informatica@spartan.cl";

            // Si no está configurado, no hagas nada
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return;

            int port = int.TryParse(portStr, out var p) ? p : 587;
            bool enableSsl = !bool.TryParse(enableSslStr, out var ssl) || ssl; // default true

            var body = new StringBuilder();
            body.AppendLine("Nueva sugerencia / feedback recibido en SpartanVentas.");
            body.AppendLine();
            body.AppendLine($"ID: {id}");
            body.AppendLine($"Fecha (UTC): {createdAtUtc:yyyy-MM-dd HH:mm:ss}");
            body.AppendLine($"Usuario: {login} (SlpCode {slpCode})");
            body.AppendLine($"Tipo: {tipo}");
            body.AppendLine($"Módulo: {modulo}");
            body.AppendLine($"Título: {titulo}");
            body.AppendLine();
            body.AppendLine("Mensaje:");
            body.AppendLine(mensaje);

            using var mail = new MailMessage
            {
                From = new MailAddress(from),
                Subject = $"[SpartanVentas] Feedback {tipo} • {modulo} • #{id} • {titulo}",
                Body = body.ToString()
            };
            mail.To.Add(to);

            using var smtp = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl
            };

            // Si hay usuario configurado, setea credenciales (Office365/Gmail)
            if (!string.IsNullOrWhiteSpace(user))
                smtp.Credentials = new NetworkCredential(user, pass);

            await smtp.SendMailAsync(mail);
        }

    }
}
