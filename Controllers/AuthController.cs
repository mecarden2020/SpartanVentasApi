using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SpartanVentasApi.Helpers;
using SpartanVentasApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

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
        //
        // Permite autenticación mediante:
        //   1) Usuario de dbo.ApiUsuarios
        //   2) Correo SAP OSLP.U_CORREO
        //
        // El correo se resuelve mediante SlpCode.
        // Si un correo corresponde a más de un usuario activo,
        // NO se selecciona uno arbitrariamente.
        // ============================================================

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest request)
        {
            try
            {
                var identificador =
                    (request?.Username ?? string.Empty).Trim();

                var passPlano =
                    (request?.Password ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(identificador))
                {
                    return Unauthorized(
                        "Debe ingresar usuario o correo");
                }

                if (string.IsNullOrWhiteSpace(passPlano))
                {
                    return Unauthorized(
                        "Contraseña requerida");
                }

                using var conn = new SqlConnection(ConnStr);
                conn.Open();


                // ============================================================
                // PASO 1
                // Intentar localizar directamente por Usuario
                // ============================================================

                int? usuarioIdEncontrado = null;

                using (var cmdUsuario = new SqlCommand(@"
SELECT TOP 1
    u.Id
FROM dbo.ApiUsuarios u
WHERE LTRIM(RTRIM(u.Usuario)) = @Identificador
  AND u.Activo = 1;
", conn))
                {
                    cmdUsuario.Parameters.Add(
                        "@Identificador",
                        SqlDbType.NVarChar,
                        255).Value = identificador;

                    var resultado =
                        cmdUsuario.ExecuteScalar();

                    if (resultado != null &&
                        resultado != DBNull.Value)
                    {
                        usuarioIdEncontrado =
                            Convert.ToInt32(resultado);
                    }
                }


                // ============================================================
                // PASO 2
                // Si no fue encontrado como Usuario,
                // intentar resolver mediante OSLP.U_CORREO
                // ============================================================

                if (!usuarioIdEncontrado.HasValue)
                {
                    var usuariosPorCorreo =
                        new List<int>();

                    using (var cmdCorreo = new SqlCommand(@"
SELECT DISTINCT
    u.Id
FROM dbo.ApiUsuarios u
INNER JOIN OSLP s
    ON s.SlpCode = u.SlpCode
WHERE u.Activo = 1
  AND s.U_CORREO IS NOT NULL
  AND LTRIM(RTRIM(s.U_CORREO)) <> ''
  AND LOWER(LTRIM(RTRIM(s.U_CORREO)))
      = LOWER(LTRIM(RTRIM(@Identificador)));
", conn))
                    {
                        cmdCorreo.Parameters.Add(
                            "@Identificador",
                            SqlDbType.NVarChar,
                            255).Value = identificador;

                        using var readerCorreo =
                            cmdCorreo.ExecuteReader();

                        while (readerCorreo.Read())
                        {
                            usuariosPorCorreo.Add(
                                Convert.ToInt32(
                                    readerCorreo["Id"]));
                        }
                    }


                    // --------------------------------------------------------
                    // No existe usuario ni correo relacionado
                    // --------------------------------------------------------

                    if (usuariosPorCorreo.Count == 0)
                    {
                        return Unauthorized(
                            "Usuario o correo no registrado, o usuario inactivo");
                    }


                    // --------------------------------------------------------
                    // Correo ambiguo:
                    // más de un usuario activo asociado al mismo correo
                    // --------------------------------------------------------

                    if (usuariosPorCorreo.Count > 1)
                    {
                        return Unauthorized(
                            "El correo está asociado a más de un usuario. " +
                            "Ingrese utilizando su usuario habitual.");
                    }


                    // --------------------------------------------------------
                    // Correo válido y único
                    // --------------------------------------------------------

                    usuarioIdEncontrado =
                        usuariosPorCorreo[0];
                }


                // ============================================================
                // PASO 3
                // CARGAR DATOS BASE DEL USUARIO
                // ============================================================

                int userId;
                string usuarioBD;
                string claveBD;
                string nombreBD;
                int? slpCodeBD;

                using (var cmd = new SqlCommand(@"
SELECT TOP 1
    u.Id,
    u.Usuario,
    u.Clave,
    u.Nombre,
    u.SlpCode
FROM dbo.ApiUsuarios u
WHERE u.Id = @UsuarioId
  AND u.Activo = 1;
", conn))
                {
                    cmd.Parameters.Add(
                        "@UsuarioId",
                        SqlDbType.Int).Value =
                            usuarioIdEncontrado.Value;

                    using var reader = cmd.ExecuteReader();

                    if (!reader.Read())
                    {
                        return Unauthorized(
                            "Usuario no registrado o inactivo");
                    }

                    userId = Convert.ToInt32(reader["Id"]);

                    usuarioBD =
                        (reader["Usuario"]?.ToString()
                         ?? string.Empty).Trim();

                    claveBD =
                        reader["Clave"]?.ToString()
                        ?? string.Empty;

                    nombreBD =
                        (reader["Nombre"]?.ToString()
                         ?? string.Empty).Trim();

                    slpCodeBD =
                        reader["SlpCode"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(reader["SlpCode"]);
                }


                // ============================================================
                // PASO 4
                // VALIDACIÓN DE CONTRASEÑA
                // ============================================================

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


                // ============================================================
                // PASO 5
                // OBTENER TODOS LOS ROLES AUTORIZADOS
                // ============================================================

                var roles =
                    ObtenerRolesDeUsuario(conn, userId);


                // ============================================================
                // PASO 6
                // SI TIENE MÁS DE UN PERFIL, NO CREAR SESIÓN TODAVÍA
                // ============================================================

                if (roles.Count > 1)
                {
                    var tokenSeleccion =
                        GenerarTokenSeleccionPerfil(
                            userId,
                            usuarioBD);

                    return Ok(new
                    {
                        requiereSeleccionPerfil = true,

                        tokenSeleccion,

                        username = usuarioBD,
                        login = nombreBD,
                        slpCode = slpCodeBD,

                        perfiles = roles
                    });
                }


                // ============================================================
                // PASO 7
                // UN SOLO ROL -> LOGIN NORMAL
                // ============================================================

                var rolBD =
                    roles.Count == 1
                        ? roles[0]
                        : "VENDEDOR";

                var user = new UserInfo
                {
                    Username = usuarioBD,
                    Login = nombreBD,
                    SlpCode = slpCodeBD,
                    Role = rolBD,

                    Permisos =
                        ObtenerPermisosPorRol(
                            conn,
                            userId,
                            rolBD)
                };


                // ============================================================
                // PASO 8
                // JWT DEFINITIVO
                // ============================================================

                var token = GenerateJwtToken(user);


                // ============================================================
                // PASO 9
                // COOKIE SSO
                // ============================================================

                await CrearCookieSsoAsync(user);


                // ============================================================
                // PASO 10
                // RESPUESTA LOGIN NORMAL
                // ============================================================

                return Ok(new
                {
                    requiereSeleccionPerfil = false,

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
        // DTO - SELECCIÓN DE PERFIL
        // ============================================================

        public class SeleccionarPerfilRequest
        {
            public string TokenSeleccion { get; set; } =
                string.Empty;

            public string Perfil { get; set; } =
                string.Empty;
        }




        // ============================================================
        // SELECCIONAR PERFIL
        // POST /api/auth/seleccionar-perfil
        // ============================================================

        [HttpPost("seleccionar-perfil")]
        public async Task<IActionResult> SeleccionarPerfil(
            [FromBody] SeleccionarPerfilRequest request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.TokenSeleccion) ||
                    string.IsNullOrWhiteSpace(request.Perfil))
                {
                    return BadRequest(
                        "Token y perfil son requeridos");
                }

                var tokenSeleccion =
                    request.TokenSeleccion.Trim();

                var perfilSolicitado =
                    NormalizaRol(request.Perfil);

                // ====================================================
                // VALIDAR TOKEN TEMPORAL
                // ====================================================

                var jwtConfig =
                    _config.GetSection("Jwt");

                var keyStr =
                    jwtConfig["Key"];

                if (string.IsNullOrWhiteSpace(keyStr))
                {
                    throw new InvalidOperationException(
                        "Falta Jwt:Key en appsettings.json");
                }

                var tokenHandler =
                    new JwtSecurityTokenHandler();

                var validationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtConfig["Issuer"],

                        ValidateAudience = true,
                        ValidAudience = jwtConfig["Audience"],

                        ValidateIssuerSigningKey = true,

                        IssuerSigningKey =
                            new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(keyStr)),

                        ValidateLifetime = true,

                        ClockSkew = TimeSpan.Zero
                    };

                ClaimsPrincipal principal;

                try
                {
                    principal =
                        tokenHandler.ValidateToken(
                            tokenSeleccion,
                            validationParameters,
                            out _);
                }
                catch
                {
                    return Unauthorized(
                        "La selección de perfil expiró. Inicie sesión nuevamente.");
                }


                // ====================================================
                // CONFIRMAR QUE ES TOKEN DE SELECCIÓN
                // ====================================================

                var tokenType =
                    principal.FindFirst("tokenType")?.Value;

                if (!string.Equals(
                        tokenType,
                        "PROFILE_SELECTION",
                        StringComparison.Ordinal))
                {
                    return Unauthorized(
                        "Token de selección inválido");
                }


                // ====================================================
                // OBTENER USER ID DEL TOKEN
                // ====================================================

                var userIdTexto =
                    principal.FindFirst("userId")?.Value;

                if (!int.TryParse(
                        userIdTexto,
                        out var userId))
                {
                    return Unauthorized(
                        "Token de selección inválido");
                }


                // ====================================================
                // CONSULTAR USUARIO
                // ====================================================

                using var conn =
                    new SqlConnection(ConnStr);

                conn.Open();

                string usuarioBD;
                string nombreBD;
                int? slpCodeBD;

                using (var cmd = new SqlCommand(@"
SELECT
    Id,
    Usuario,
    Nombre,
    SlpCode
FROM dbo.ApiUsuarios
WHERE Id = @UserId
  AND Activo = 1;
", conn))
                {
                    cmd.Parameters.Add(
                        "@UserId",
                        SqlDbType.Int).Value = userId;

                    using var reader =
                        cmd.ExecuteReader();

                    if (!reader.Read())
                    {
                        return Unauthorized(
                            "Usuario no habilitado");
                    }

                    usuarioBD =
                        (reader["Usuario"]?.ToString()
                         ?? string.Empty).Trim();

                    nombreBD =
                        (reader["Nombre"]?.ToString()
                         ?? string.Empty).Trim();

                    slpCodeBD =
                        reader["SlpCode"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                reader["SlpCode"]);
                }


                // ====================================================
                // VERIFICAR QUE EL USUARIO REALMENTE POSEA ESE PERFIL
                // ====================================================

                var rolesAutorizados =
                    ObtenerRolesDeUsuario(
                        conn,
                        userId);

                var perfilValido =
                    rolesAutorizados.Any(
                        r => string.Equals(
                            r,
                            perfilSolicitado,
                            StringComparison.OrdinalIgnoreCase));

                if (!perfilValido)
                {
                    return Forbid();
                }


                // ====================================================
                // CREAR IDENTIDAD CON EL PERFIL SELECCIONADO
                // ====================================================

                var user =
                    new UserInfo
                    {
                        Username = usuarioBD,
                        Login = nombreBD,
                        SlpCode = slpCodeBD,
                        Role = perfilSolicitado,

                        Permisos =
                            ObtenerPermisosPorRol(
                                conn,
                                userId,
                                perfilSolicitado)
                    };


                // ====================================================
                // JWT DEFINITIVO
                // ====================================================

                var token =
                    GenerateJwtToken(user);


                // ====================================================
                // COOKIE SSO DEFINITIVA
                // ====================================================

                await CrearCookieSsoAsync(user);


                // ====================================================
                // RESPUESTA
                // ====================================================

                return Ok(new
                {
                    requiereSeleccionPerfil = false,

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
                    "Error al seleccionar perfil: "
                    + ex.Message);
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




        // ============================================================
        // OBTENER ROLES DISPONIBLES DEL USUARIO
        // ============================================================

        private List<string> ObtenerRolesDeUsuario(
            SqlConnection conn,
            int userId)
        {
            var roles = new List<string>();

            using var cmd = new SqlCommand(@"
                                    SELECT DISTINCT
                                        r.Nombre
                                    FROM dbo.ApiUsuarioRoles ur
                                    INNER JOIN dbo.ApiRoles r
                                        ON r.Id = ur.RolId
                                    WHERE ur.UsuarioId = @UserId
                                    ORDER BY r.Nombre;
                                    ", conn);

            cmd.Parameters.Add(
                "@UserId",
                SqlDbType.Int).Value = userId;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var rol = NormalizaRol(
                    reader["Nombre"]?.ToString());

                if (!string.IsNullOrWhiteSpace(rol)
                    && !roles.Contains(
                        rol,
                        StringComparer.OrdinalIgnoreCase))
                {
                    roles.Add(rol);
                }
            }

            // Compatibilidad con usuarios antiguos sin rol explícito.
            if (roles.Count == 0)
            {
                roles.Add("VENDEDOR");
            }

            return roles;
        }




        // ============================================================
        // OBTENER PERMISOS DEL PERFIL ACTIVO
        // ============================================================

        private List<string> ObtenerPermisosPorRol(
            SqlConnection conn,
            int userId,
            string rolActivo)
        {
            var permisos = new List<string>();

            try
            {
                using var cmd = new SqlCommand(@"
                            SELECT DISTINCT
                                p.Codigo
                            FROM dbo.ApiUsuarioRoles ur
                            INNER JOIN dbo.ApiRoles r
                                ON r.Id = ur.RolId
                            INNER JOIN dbo.ApiRolPermisos rp
                                ON rp.RolId = r.Id
                            INNER JOIN dbo.ApiPermisos p
                                ON p.Id = rp.PermisoId
                            WHERE ur.UsuarioId = @UserId
                              AND UPPER(LTRIM(RTRIM(r.Nombre))) = @Rol;
                            ", conn);

                cmd.Parameters.Add(
                    "@UserId",
                    SqlDbType.Int).Value = userId;

                cmd.Parameters.Add(
                    "@Rol",
                    SqlDbType.NVarChar,
                    50).Value = rolActivo
                        .Trim()
                        .ToUpperInvariant();

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var codigo =
                        reader["Codigo"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(codigo))
                    {
                        permisos.Add(codigo.Trim());
                    }
                }
            }
            catch (SqlException)
            {
                return new List<string>();
            }

            return permisos
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }







        // ============================================================
        // TOKEN TEMPORAL PARA SELECCIÓN DE PERFIL
        // Duración: 5 minutos
        // ============================================================

        private string GenerarTokenSeleccionPerfil(
            int userId,
            string username)
        {
            var jwt =
                _config.GetSection("Jwt");

            var issuer = jwt["Issuer"];
            var audience = jwt["Audience"];
            var keyStr = jwt["Key"];

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
        new Claim(
            JwtRegisteredClaimNames.Sub,
            username),

        new Claim(
            "userId",
            userId.ToString()),

        new Claim(
            "username",
            username),

        // Impide confundirlo con un JWT definitivo.
        new Claim(
            "tokenType",
            "PROFILE_SELECTION")
    };

            var token =
                new JwtSecurityToken(
                    issuer,
                    audience,
                    claims,
                    expires: DateTime.UtcNow.AddMinutes(5),
                    signingCredentials: creds);

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }




















    }




}