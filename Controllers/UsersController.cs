using Microsoft.Data.SqlClient;
using SpartanVentasApi.Models.Requests; // <-- donde está UploadPhotoRequest
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace SpartanVentasApi.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".webp" };
        private const long MaxBytes = 2 * 1024 * 1024; // 2MB

        private readonly IConfiguration _cfg;
        private readonly IWebHostEnvironment _env;

        public UsersController(IConfiguration cfg, IWebHostEnvironment env)
        {
            _cfg = cfg;
            _env = env;
        }

        private string ConnStr => _cfg.GetConnectionString("SAP")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection");


        // POST /api/users/me/photo  (Endpoint)  Metodo

        [Authorize]
        [HttpPost("me/photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadMyPhoto()
        {
            var file = Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return BadRequest("Archivo vacío.");

            if (file.Length > MaxBytes)
                return BadRequest("Archivo supera el máximo permitido (2MB).");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExt.Contains(ext))
                return BadRequest("Formato no permitido (jpg/jpeg/png/webp).");

            

            var userLogin =
                User.Identity?.Name
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? User.FindFirst("unique_name")?.Value
                ?? User.FindFirst("name")?.Value
                ?? User.FindFirst("login")?.Value
                ?? User.FindFirst("username")?.Value
                ?? User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrWhiteSpace(userLogin))
                return Unauthorized("No se pudo obtener el usuario.");





            // 🔍 DEBUG TEMPORAL – VER CLAIMS DEL JWT
            // var claims = User.Claims
            //     .Select(c => new { c.Type, c.Value })
            //     .ToList();

            // return Ok(claims);   // ← solo para debug, luego se quita

            // var userLogin = User.Identity?.Name;
            //var userLogin = User.FindFirst("sub")?.Value;

            if (string.IsNullOrWhiteSpace(userLogin))
                return Unauthorized("No se pudo obtener el usuario (Identity.Name).");

            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "users");
            Directory.CreateDirectory(uploadsDir);

            // Limpia caracteres raros para nombre de archivo
            var safeLogin = string.Concat(userLogin.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

            var fileName = $"{safeLogin}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            var fullPath = Path.Combine(uploadsDir, fileName);

            await using (var fs = System.IO.File.Create(fullPath))
                await file.CopyToAsync(fs);

            var photoUrl = $"/uploads/users/{fileName}";

            // Guardar SOLO la URL en SQL
            await using var cn = new SqlConnection(ConnStr);
            await cn.ExecuteAsync(@"
                                    UPDATE dbo.ApiUserProfile
                                    SET PhotoUrl = @photoUrl,
                                        UpdatedAt = SYSDATETIME()
                                    WHERE Login = @login",
            new { photoUrl, login = userLogin });

            return Ok(new { photoUrl });
        }

        [Authorize]
        [HttpGet("me/profile")]
        public async Task<IActionResult> GetMyProfile()
        {
            // obtener login desde claims (mismo criterio que UploadMyPhoto)
            var userLogin =
                            User.Identity?.Name
                            ?? User.FindFirst("usuario")?.Value
                            ?? User.FindFirst(ClaimTypes.Name)?.Value
                            ?? User.FindFirst("unique_name")?.Value
                            ?? User.FindFirst("login")?.Value
                            ?? User.FindFirst("sub")?.Value;


            if (string.IsNullOrWhiteSpace(userLogin))
                return Unauthorized("No se pudo obtener el usuario.");

            await using var cn = new SqlConnection(ConnStr);

            var sql = @"
                        SELECT TOP 1
                            Login,
                            PhotoUrl
                        FROM dbo.ApiUserProfile
                        WHERE Login = @login;
                        ";

            var row = await cn.QueryFirstOrDefaultAsync(sql, new { login = userLogin });

            // Si no existe fila, devolvemos photoUrl null (front pone default)
            if (row == null) return Ok(new { login = userLogin, photoUrl = (string?)null });

            return Ok(new { login = (string)row.Login, photoUrl = (string?)row.PhotoUrl });
        }
    }
}



    
