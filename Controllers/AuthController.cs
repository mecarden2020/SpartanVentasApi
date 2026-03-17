using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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

        public AuthController(IConfiguration config)
        {
            _config = config;
        }

        private string ConnStr =>
            _config.GetConnectionString("SAP")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:SAP");

        // ============================================================
        // Normalización de Roles (Backend)
        // ============================================================
        private static string NormalizaRol(string? rolDb)
        {
            var r = (rolDb ?? "").Trim().ToUpperInvariant();

            // ADMIN de vendedores / mantención (NO GERENCIA)
            if (r is "ADMIN_VENDEDORES" or "MANTENCION" or "MANTENCIÓN")
                return "ADMIN_VENDEDORES";

            // Admin total (si lo usas)
            if (r is "ADMIN" or "ADMINISTRADOR" or "ADMINISTRADOR DEL SISTEMA")
                return "ADMIN";

            // Gerencia
            if (r is "GERENCIA" or "GERENTE")
                return "GERENCIA";

            // Supervisor
            if (r is "SUPERVISOR" or "JEFE")
                return "SUPERVISOR";

            // Default
            return "VENDEDOR";
        }

        // ----------------------------------------------------------------------
        // LOGIN
        // POST /api/auth/login
        // ----------------------------------------------------------------------
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            try
            {
                var username = (request?.Username ?? string.Empty).Trim();
                var passPlano = (request?.Password ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(username))
                    return Unauthorized("Usuario inválido");

                if (string.IsNullOrWhiteSpace(passPlano))
                    return Unauthorized("Contraseña requerida");

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
OUTER APPLY (
    SELECT TOP 1 r.Nombre AS RolNombre
    FROM dbo.ApiUsuarioRoles ur
    JOIN dbo.ApiRoles r ON r.Id = ur.RolId
    WHERE ur.UsuarioId = u.Id
    ORDER BY
        CASE 
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('GERENCIA','GERENTE') THEN 1
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('SUPERVISOR','JEFE') THEN 2
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('ADMIN_VENDEDORES','MANTENCION','MANTENCIÓN') THEN 3
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('ADMIN','ADMINISTRADOR','ADMINISTRADOR DEL SISTEMA') THEN 4
            ELSE 99
        END,
        ur.Id DESC
) rr
WHERE u.Usuario = @Usuario AND u.Activo = 1;
", conn);


                cmd.Parameters.AddWithValue("@Usuario", username);

                int userId;
                string usuarioBD;
                string claveBD;
                string nombreBD;
                int? slpCodeBD;
                string rolBD;

                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return Unauthorized("Usuario no registrado o inactivo");

                    userId = Convert.ToInt32(reader["Id"]);
                    usuarioBD = (reader["Usuario"]?.ToString() ?? "").Trim();
                    claveBD = reader["Clave"]?.ToString() ?? "";
                    nombreBD = (reader["Nombre"]?.ToString() ?? "").Trim();
                    rolBD = NormalizaRol(reader["Rol"]?.ToString() ?? "VENDEDOR");

                    slpCodeBD = reader["SlpCode"] == DBNull.Value ? null : Convert.ToInt32(reader["SlpCode"]);
                }

                // ==============================
                // VALIDACIÓN PASSWORD (SHA256 HEX)
                // ==============================
                var hashIngresado = PasswordHelper.Sha256Hex(passPlano);

                if (!string.Equals(hashIngresado, claveBD, StringComparison.OrdinalIgnoreCase))
                    return Unauthorized("Contraseña incorrecta");

                var user = new UserInfo
                {
                    Username = usuarioBD,
                    Login = nombreBD,
                    SlpCode = slpCodeBD,
                    Role = string.IsNullOrWhiteSpace(rolBD) ? "VENDEDOR" : rolBD,
                    Permisos = ObtenerPermisosDeUsuarioSafe(conn, userId)
                };

                var token = GenerateJwtToken(user);

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
                return StatusCode(500, "Error interno en login: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------
        // MAGIC LINK
        // GET /api/auth/magic-link?token=GUID
        // Robusto: Token puede ser uniqueidentifier o varchar/nvarchar
        // ----------------------------------------------------------------------
        [HttpGet("magic-link")]
        public IActionResult MagicLink([FromQuery] string token)
        {
            try
            {
                var tokenStr = (token ?? "").Trim();
                if (tokenStr.Length < 20)
                    return Unauthorized("Link inválido o expirado");

                using var conn = new SqlConnection(ConnStr);
                conn.Open();

                // 1) Buscar link activo y no expirado
                using var cmd = new SqlCommand(@"
SELECT TOP 1 SlpCode
FROM dbo.ApiVendedorLinks
WHERE CONVERT(varchar(36), Token) = @Token
  AND Activo = 1
  AND (FechaExpiracion IS NULL OR FechaExpiracion >= GETDATE());
", conn);

                cmd.Parameters.AddWithValue("@Token", tokenStr);

                var slpObj = cmd.ExecuteScalar();
                if (slpObj == null)
                    return Unauthorized("Link inválido o expirado");

                var slpCode = Convert.ToInt32(slpObj);

                // 2) Buscar usuario activo por SlpCode
                using var cmd2 = new SqlCommand(@"
SELECT TOP 1
    u.Id, u.Usuario, u.Nombre, u.SlpCode,
    ISNULL(rr.RolNombre,'VENDEDOR') AS Rol
FROM dbo.ApiUsuarios u
OUTER APPLY (
    SELECT TOP 1 r.Nombre AS RolNombre
    FROM dbo.ApiUsuarioRoles ur
    JOIN dbo.ApiRoles r ON r.Id = ur.RolId
    WHERE ur.UsuarioId = u.Id
    ORDER BY
        CASE 
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('GERENCIA','GERENTE') THEN 1
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('SUPERVISOR','JEFE') THEN 2
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('ADMIN_VENDEDORES','MANTENCION','MANTENCIÓN') THEN 3
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('ADMIN','ADMINISTRADOR','ADMINISTRADOR DEL SISTEMA') THEN 4
            ELSE 99
        END,
        ur.Id DESC
) rr
WHERE u.SlpCode = @SlpCode AND u.Activo = 1;
", conn);


                cmd2.Parameters.AddWithValue("@SlpCode", slpCode);

                int userId;
                string usuario;
                string nombre;
                string rol;
                int? slp;

                using (var rd = cmd2.ExecuteReader())
                {
                    if (!rd.Read())
                        return Unauthorized("Vendedor no habilitado");

                    userId = Convert.ToInt32(rd["Id"]);
                    usuario = (rd["Usuario"]?.ToString() ?? "").Trim();
                    nombre = (rd["Nombre"]?.ToString() ?? "").Trim();
                    rol = NormalizaRol(rd["Rol"]?.ToString() ?? "VENDEDOR");

                    slp = rd["SlpCode"] == DBNull.Value ? null : Convert.ToInt32(rd["SlpCode"]);
                }

                var user = new UserInfo
                {
                    Username = usuario,
                    Login = nombre,
                    SlpCode = slp,
                    Role = rol,
                    Permisos = ObtenerPermisosDeUsuarioSafe(conn, userId)
                };

                var jwt = GenerateJwtToken(user);

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
                return StatusCode(500, "Error interno en magic-link: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------
        // PERMISOS (SAFE)
        // ----------------------------------------------------------------------
        private List<string> ObtenerPermisosDeUsuarioSafe(SqlConnection conn, int userId)
        {
            var permisos = new List<string>();

            try
            {
                using var cmd = new SqlCommand(@"
SELECT p.Codigo
FROM dbo.ApiRolPermisos rp
JOIN dbo.ApiPermisos p ON p.Id = rp.PermisoId
JOIN dbo.ApiUsuarioRoles ur ON ur.RolId = rp.RolId
WHERE ur.UsuarioId = @UserId;
", conn);

                cmd.Parameters.AddWithValue("@UserId", userId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var code = reader["Codigo"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(code))
                        permisos.Add(code.Trim());
                }
            }
            catch (SqlException)
            {
                // si faltan tablas/relaciones en algún ambiente, no rompas login
                return new List<string>();
            }

            return permisos;
        }

        // ----------------------------------------------------------------------
        // JWT
        // ----------------------------------------------------------------------
        private string GenerateJwtToken(UserInfo user)
        {
            var jwt = _config.GetSection("Jwt");

            var issuer = jwt["Issuer"];
            var audience = jwt["Audience"];
            var keyStr = jwt["Key"];

            if (string.IsNullOrWhiteSpace(keyStr))
                throw new InvalidOperationException("Falta Jwt:Key en appsettings.json");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                // Identidad base
                new Claim(JwtRegisteredClaimNames.Sub, user.Username ?? ""),

                // ✅ Consistencia para tus controllers (Wall/Feedback)
                new Claim("login", user.Username ?? ""),        // login técnico (usuario)
                new Claim("username", user.Username ?? ""),     // alias alterno
                new Claim("nombre", user.Login ?? ""),          // nombre visible
                new Claim("slpCode", user.SlpCode?.ToString() ?? ""), // ✅ lo que Feedback busca
                new Claim("SlpCode", user.SlpCode?.ToString() ?? "")  // compatibilidad
            };

            // Role
            if (!string.IsNullOrWhiteSpace(user.Role))
                claims.Add(new Claim(ClaimTypes.Role, user.Role));

            // Permisos
            foreach (var p in user.Permisos ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(p))
                    claims.Add(new Claim("permiso", p.Trim()));
            }

            // Expires
            var expiresMinutes = double.TryParse(jwt["ExpiresInMinutes"], out var m) ? m : 60;

            var token = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
