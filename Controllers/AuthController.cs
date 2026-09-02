using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SpartanVentasApi.Helpers;
using SpartanVentasApi.Models;

namespace SpartanVentasApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;

        // Debe coincidir exactamente con el esquema
        // configurado en Program.cs y ConsultaSN.
        private const string SharedCookieScheme =
            "SpartanCloud.SharedCookie";

        public AuthController(IConfiguration config)
        {
            _config = config;
        }

        private string ConnStr =>
            _config.GetConnectionString("SAP")
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:SAP");

        // ============================================================
        // NORMALIZACIÓN DE ROLES
        // ============================================================

        private static string NormalizaRol(string? rolDb)
        {
            var r = (rolDb ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            // ADMIN de vendedores / mantención.
            if (r is "ADMIN_VENDEDORES"
                or "MANTENCION"
                or "MANTENCIÓN")
            {
                return "ADMIN_VENDEDORES";
            }

            // Administrador total.
            if (r is "ADMIN"
                or "ADMINISTRADOR"
                or "ADMINISTRADOR DEL SISTEMA")
            {
                return "ADMIN";
            }

            // Gerencia.
            if (r is "GERENCIA" or "GERENTE")
            {
                return "GERENCIA";
            }

            // Supervisor.
            if (r is "SUPERVISOR" or "JEFE")
            {
                return "SUPERVISOR";
            }

            // Recepción.
            if (r is "RECEPCION" or "RECEPCIONISTA")
            {
                return "RECEPCIONISTA";
            }

            // Rol por defecto.
            return "VENDEDOR";
        }

        // ============================================================
        // LOGIN
        // POST /api/auth/login
        // ============================================================

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest request)
        {
            try
            {
                var username =
                    (request?.Username ?? string.Empty).Trim();

                var passPlano =
                    (request?.Password ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(username))
                {
                    return Unauthorized("Usuario inválido");
                }

                if (string.IsNullOrWhiteSpace(passPlano))
                {
                    return Unauthorized("Contraseña requerida");
                }

                using var conn = new SqlConnection(ConnStr);
                conn.Open();

                using var cmd = new SqlCommand(@"
SELECT TOP 1
    u.Id,
    u.Usuario,
    u.Clave,
    u.Nombre,
    u.SlpCode,
    ISNULL(rr.RolNombre,'VENDEDOR') AS Rol
FROM dbo.ApiUsuarios u
OUTER APPLY
(
    SELECT TOP 1
        r.Nombre AS RolNombre
    FROM dbo.ApiUsuarioRoles ur
    INNER JOIN dbo.ApiRoles r
        ON r.Id = ur.RolId
    WHERE ur.UsuarioId = u.Id
    ORDER BY
        CASE
            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN ('GERENCIA','GERENTE')
                THEN 1

            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN ('SUPERVISOR','JEFE')
                THEN 2

            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN
                 (
                    'ADMIN_VENDEDORES',
                    'MANTENCION',
                    'MANTENCIÓN'
                 )
                THEN 3

            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN
                 (
                    'ADMIN',
                    'ADMINISTRADOR',
                    'ADMINISTRADOR DEL SISTEMA'
                 )
                THEN 4

            ELSE 99
        END,
        ur.Id DESC
) rr
WHERE u.Usuario = @Usuario
  AND u.Activo = 1;
", conn);

                cmd.Parameters.AddWithValue(
                    "@Usuario",
                    username);

                int userId;
                string usuarioBD;
                string claveBD;
                string nombreBD;
                int? slpCodeBD;
                string rolBD;

                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return Unauthorized(
                            "Usuario no registrado o inactivo");
                    }

                    userId =
                        Convert.ToInt32(reader["Id"]);

                    usuarioBD =
                        (reader["Usuario"]?.ToString()
                         ?? string.Empty).Trim();

                    claveBD =
                        reader["Clave"]?.ToString()
                        ?? string.Empty;

                    nombreBD =
                        (reader["Nombre"]?.ToString()
                         ?? string.Empty).Trim();

                    rolBD = NormalizaRol(
                        reader["Rol"]?.ToString()
                        ?? "VENDEDOR");

                    slpCodeBD =
                        reader["SlpCode"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                reader["SlpCode"]);
                }

                // ====================================================
                // VALIDACIÓN DE CONTRASEÑA SHA256 HEX
                // ====================================================

                var hashIngresado =
                    PasswordHelper.Sha256Hex(passPlano);

                if (!string.Equals(
                        hashIngresado,
                        claveBD,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Unauthorized(
                        "Contraseña incorrecta");
                }

                var user = new UserInfo
                {
                    Username = usuarioBD,
                    Login = nombreBD,
                    SlpCode = slpCodeBD,

                    Role = string.IsNullOrWhiteSpace(rolBD)
                        ? "VENDEDOR"
                        : rolBD,

                    Permisos =
                        ObtenerPermisosDeUsuarioSafe(
                            conn,
                            userId)
                };

                // JWT existente.
                var token = GenerateJwtToken(user);

                // Nueva cookie compartida para SpartanCloud.
                await CrearCookieSsoAsync(user);

                // Se mantiene exactamente la estructura JSON
                // utilizada por login.html.
                return Ok(new
                {
                    token,
                    slpCode = user.SlpCode,
                    username = user.Username,
                    login = user.Login,
                    role = user.Role,
                    permisos = user.Permisos
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Error interno en login: " + ex.Message);
            }
        }

        // ============================================================
        // MAGIC LINK
        // GET /api/auth/magic-link?token=GUID
        // ============================================================

        [HttpGet("magic-link")]
        public async Task<IActionResult> MagicLink(
            [FromQuery] string token)
        {
            try
            {
                var tokenStr =
                    (token ?? string.Empty).Trim();

                if (tokenStr.Length < 20)
                {
                    return Unauthorized(
                        "Link inválido o expirado");
                }

                using var conn = new SqlConnection(ConnStr);
                conn.Open();

                // Buscar enlace activo y no expirado.
                using var cmd = new SqlCommand(@"
SELECT TOP 1
    SlpCode
FROM dbo.ApiVendedorLinks
WHERE CONVERT(varchar(36), Token) = @Token
  AND Activo = 1
  AND
  (
      FechaExpiracion IS NULL
      OR FechaExpiracion >= GETDATE()
  );
", conn);

                cmd.Parameters.AddWithValue(
                    "@Token",
                    tokenStr);

                var slpObj = cmd.ExecuteScalar();

                if (slpObj == null)
                {
                    return Unauthorized(
                        "Link inválido o expirado");
                }

                var slpCode =
                    Convert.ToInt32(slpObj);

                // Buscar usuario activo asociado al SlpCode.
                using var cmd2 = new SqlCommand(@"
SELECT TOP 1
    u.Id,
    u.Usuario,
    u.Nombre,
    u.SlpCode,
    ISNULL(rr.RolNombre,'VENDEDOR') AS Rol
FROM dbo.ApiUsuarios u
OUTER APPLY
(
    SELECT TOP 1
        r.Nombre AS RolNombre
    FROM dbo.ApiUsuarioRoles ur
    INNER JOIN dbo.ApiRoles r
        ON r.Id = ur.RolId
    WHERE ur.UsuarioId = u.Id
    ORDER BY
        CASE
            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN ('GERENCIA','GERENTE')
                THEN 1

            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN ('SUPERVISOR','JEFE')
                THEN 2

            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN
                 (
                    'ADMIN_VENDEDORES',
                    'MANTENCION',
                    'MANTENCIÓN'
                 )
                THEN 3

            WHEN UPPER(LTRIM(RTRIM(r.Nombre)))
                 IN
                 (
                    'ADMIN',
                    'ADMINISTRADOR',
                    'ADMINISTRADOR DEL SISTEMA'
                 )
                THEN 4

            ELSE 99
        END,
        ur.Id DESC
) rr
WHERE u.SlpCode = @SlpCode
  AND u.Activo = 1;
", conn);

                cmd2.Parameters.AddWithValue(
                    "@SlpCode",
                    slpCode);

                int userId;
                string usuario;
                string nombre;
                string rol;
                int? slp;

                using (var rd = cmd2.ExecuteReader())
                {
                    if (!rd.Read())
                    {
                        return Unauthorized(
                            "Vendedor no habilitado");
                    }

                    userId =
                        Convert.ToInt32(rd["Id"]);

                    usuario =
                        (rd["Usuario"]?.ToString()
                         ?? string.Empty).Trim();

                    nombre =
                        (rd["Nombre"]?.ToString()
                         ?? string.Empty).Trim();

                    rol = NormalizaRol(
                        rd["Rol"]?.ToString()
                        ?? "VENDEDOR");

                    slp =
                        rd["SlpCode"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["SlpCode"]);
                }

                var user = new UserInfo
                {
                    Username = usuario,
                    Login = nombre,
                    SlpCode = slp,
                    Role = rol,

                    Permisos =
                        ObtenerPermisosDeUsuarioSafe(
                            conn,
                            userId)
                };

                // JWT existente.
                var jwt = GenerateJwtToken(user);

                // Nueva cookie compartida para SpartanCloud.
                await CrearCookieSsoAsync(user);

                return Ok(new
                {
                    token = jwt,
                    slpCode = user.SlpCode,
                    username = user.Username,
                    login = user.Login,
                    role = user.Role,
                    permisos = user.Permisos
                });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Error interno en magic-link: "
                    + ex.Message);
            }
        }

        // ============================================================
        // CREACIÓN DE COOKIE COMPARTIDA SSO
        // ============================================================

        private async Task CrearCookieSsoAsync(UserInfo user)
        {
            var username =
                (user.Username ?? string.Empty).Trim();

            var nombre =
                (user.Login ?? string.Empty).Trim();

            var rol = string.IsNullOrWhiteSpace(user.Role)
                ? "VENDEDOR"
                : user.Role.Trim().ToUpperInvariant();

            var slpCode =
                user.SlpCode?.ToString()
                ?? string.Empty;

            var claims = new List<Claim>
            {
                // Identificador principal.
                new Claim(
                    ClaimTypes.NameIdentifier,
                    username),

                // User.Identity.Name.
                new Claim(
                    ClaimTypes.Name,
                    username),

                // Claims usados por SpartanCloud y módulos.
                new Claim(
                    "usuario",
                    username),

                new Claim(
                    "username",
                    username),

                new Claim(
                    "login",
                    username),

                new Claim(
                    "nombre",
                    nombre),

                new Claim(
                    "slpCode",
                    slpCode),

                new Claim(
                    "SlpCode",
                    slpCode),

                // Rol reconocido por
                // User.IsInRole(...) y [Authorize(Roles = "...")].
                new Claim(
                    ClaimTypes.Role,
                    rol)
            };

            // Copiar permisos dentro de la cookie.
            foreach (var permiso in
                     user.Permisos ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(permiso))
                {
                    claims.Add(
                        new Claim(
                            "permiso",
                            permiso.Trim()));
                }
            }

            var identity = new ClaimsIdentity(
                claims,
                SharedCookieScheme,
                ClaimTypes.Name,
                ClaimTypes.Role);

            var principal =
                new ClaimsPrincipal(identity);

            var propiedades =
                new AuthenticationProperties
                {
                    // La cookie se conserva aunque se cierre
                    // y vuelva a abrir el navegador.
                    IsPersistent = true,

                    AllowRefresh = true,

                    // Debe ser coherente con las 8 horas
                    // configuradas en Program.cs.
                    ExpiresUtc =
                        DateTimeOffset.UtcNow.AddHours(8)
                };

            await HttpContext.SignInAsync(
                SharedCookieScheme,
                principal,
                propiedades);
        }

        // ============================================================
        // PERMISOS
        // ============================================================

        private List<string> ObtenerPermisosDeUsuarioSafe(
            SqlConnection conn,
            int userId)
        {
            var permisos = new List<string>();

            try
            {
                using var cmd = new SqlCommand(@"
SELECT
    p.Codigo
FROM dbo.ApiRolPermisos rp
INNER JOIN dbo.ApiPermisos p
    ON p.Id = rp.PermisoId
INNER JOIN dbo.ApiUsuarioRoles ur
    ON ur.RolId = rp.RolId
WHERE ur.UsuarioId = @UserId;
", conn);

                cmd.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var code =
                        reader["Codigo"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        permisos.Add(code.Trim());
                    }
                }
            }
            catch (SqlException)
            {
                // Si faltan tablas o relaciones en algún ambiente,
                // no se interrumpe el inicio de sesión.
                return new List<string>();
            }

            return permisos;
        }

        // ============================================================
        // GENERACIÓN JWT ACTUAL
        // ============================================================

        private string GenerateJwtToken(UserInfo user)
        {
            var jwt =
                _config.GetSection("Jwt");

            var issuer =
                jwt["Issuer"];

            var audience =
                jwt["Audience"];

            var keyStr =
                jwt["Key"];

            if (string.IsNullOrWhiteSpace(keyStr))
            {
                throw new InvalidOperationException(
                    "Falta Jwt:Key en appsettings.json");
            }

            var key =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(keyStr));

            var creds =
                new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                // Identidad base.
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    user.Username ?? string.Empty),

                // Claims utilizados por los controladores actuales.
                new Claim(
                    "login",
                    user.Username ?? string.Empty),

                new Claim(
                    "username",
                    user.Username ?? string.Empty),

                new Claim(
                    "nombre",
                    user.Login ?? string.Empty),

                new Claim(
                    "slpCode",
                    user.SlpCode?.ToString()
                    ?? string.Empty),

                new Claim(
                    "SlpCode",
                    user.SlpCode?.ToString()
                    ?? string.Empty)
            };

            // Rol.
            if (!string.IsNullOrWhiteSpace(user.Role))
            {
                claims.Add(
                    new Claim(
                        ClaimTypes.Role,
                        user.Role));
            }

            // Permisos.
            foreach (var permiso in
                     user.Permisos ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(permiso))
                {
                    claims.Add(
                        new Claim(
                            "permiso",
                            permiso.Trim()));
                }
            }

            double expiresMinutes;

            if (string.Equals(
                    user.Role,
                    "GERENCIA",
                    StringComparison.OrdinalIgnoreCase))
            {
                expiresMinutes = 43200;
                // 30 días.
            }
            else
            {
                expiresMinutes = 10080;
                // 7 días.
            }

            var token =
                new JwtSecurityToken(
                    issuer,
                    audience,
                    claims,
                    expires:
                        DateTime.UtcNow.AddMinutes(
                            expiresMinutes),
                    signingCredentials: creds);

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }
    }
}