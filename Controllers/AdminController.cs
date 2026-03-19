using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace SpartanVentasApi.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "ADMIN")]
    public class AdminController : ControllerBase
    {
        private readonly IConfiguration _config;

        public AdminController(IConfiguration config)
        {
            _config = config;
        }

        private string GetConnection()
        {
            return _config.GetConnectionString("SAP")
                   ?? throw new InvalidOperationException("No existe ConnectionStrings:SAP");
        }

        private static string HashSHA256Hex(string texto)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(texto));
            return Convert.ToHexString(bytes);
        }

        // ================================================================
        // ROLES
        // GET /api/admin/roles
        // ================================================================
        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            try
            {
                using var cn = new SqlConnection(GetConnection());

                var sql = @"
SELECT 
    Id,
    Nombre
FROM dbo.ApiRoles
ORDER BY Nombre;";

                var data = await cn.QueryAsync(sql);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al obtener roles.",
                    detalle = ex.Message
                });
            }
        }

        // ================================================================
        // USUARIOS
        // GET /api/admin/usuarios
        // ================================================================
        [HttpGet("usuarios")]
        public async Task<IActionResult> GetUsuarios()
        {
            try
            {
                using var cn = new SqlConnection(GetConnection());

                var sql = @"
DECLARE @BaseUrl NVARCHAR(200) = 'https://app.spartan.cl/ingreso.html?token=';

SELECT
    u.Id,
    u.Usuario AS Login,
    u.Nombre,
    CAST(ISNULL(u.Activo, 0) AS bit) AS Activo,
    u.SlpCode,
    ISNULL(rx.RolNombre, 'VENDEDOR') AS Rol,
    CASE
        WHEN l.Token IS NOT NULL AND ISNULL(l.Activo, 0) = 1
            THEN @BaseUrl + CONVERT(VARCHAR(36), l.Token)
        ELSE NULL
    END AS LinkAcceso
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
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('GERENCIA','GERENTE') THEN 1
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('SUPERVISOR','JEFE') THEN 2
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('ADMIN_VENDEDORES','MANTENCION','MANTENCIÓN') THEN 3
            WHEN UPPER(LTRIM(RTRIM(r.Nombre))) IN ('ADMIN','ADMINISTRADOR','ADMINISTRADOR DEL SISTEMA') THEN 4
            ELSE 99
        END,
        ur.Id DESC
) rx
OUTER APPLY
(
    SELECT TOP 1
        l.Token,
        l.Activo,
        l.FechaCreacion,
        l.FechaExpiracion
    FROM dbo.ApiVendedorLinks l
    WHERE l.SlpCode = u.SlpCode
    ORDER BY l.FechaCreacion DESC
) l
ORDER BY u.Nombre, u.Usuario;";

                var data = await cn.QueryAsync(sql);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al obtener usuarios.",
                    detalle = ex.Message
                });
            }
        }


        // ================================================================
        // CREAR USUARIO
        // POST /api/admin/usuarios
        // ================================================================
        public class CrearUsuarioRequest
        {
            public string Usuario { get; set; } = "";
            public string Nombre { get; set; } = "";
            public string Clave { get; set; } = "";
            public string Rol { get; set; } = "";
            public int? SlpCode { get; set; }
        }

        [HttpPost("usuarios")]
        public async Task<IActionResult> CrearUsuario([FromBody] CrearUsuarioRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Usuario) ||
                string.IsNullOrWhiteSpace(req.Nombre) ||
                string.IsNullOrWhiteSpace(req.Clave) ||
                string.IsNullOrWhiteSpace(req.Rol))
            {
                return BadRequest(new { mensaje = "Usuario, nombre, clave y rol son obligatorios." });
            }

            using var cn = new SqlConnection(GetConnection());
            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            try
            {
                var existe = await cn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(1) FROM dbo.ApiUsuarios WHERE Usuario = @Usuario",
                    new { req.Usuario }, tx);

                if (existe > 0)
                {
                    tx.Rollback();
                    return BadRequest(new { mensaje = "El usuario ya existe." });
                }

                var hash = HashSHA256Hex(req.Clave);

                // Alineado a tu base: columna Clave, no ClaveHash
                // SlpCode va directo en ApiUsuarios porque así lo usas en AuthController
                var usuarioId = await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ApiUsuarios (Usuario, Nombre, Clave, SlpCode, Activo)
VALUES (@Usuario, @Nombre, @Clave, @SlpCode, 1);

SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new
                    {
                        req.Usuario,
                        req.Nombre,
                        Clave = hash,
                        req.SlpCode
                    }, tx);

                // Alineado a tu base: ApiRoles.Nombre
                var rolId = await cn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 Id
FROM dbo.ApiRoles
WHERE LTRIM(RTRIM(Nombre)) = LTRIM(RTRIM(@Rol));",
                    new { req.Rol }, tx);

                if (rolId == null)
                {
                    tx.Rollback();
                    return BadRequest(new { mensaje = "El rol indicado no existe." });
                }

                await cn.ExecuteAsync(@"
INSERT INTO dbo.ApiUsuarioRoles (UsuarioId, RolId)
VALUES (@UsuarioId, @RolId);",
                    new
                    {
                        UsuarioId = usuarioId,
                        RolId = rolId.Value
                    }, tx);

                tx.Commit();

                return Ok(new
                {
                    mensaje = "Usuario creado correctamente.",
                    usuarioId
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return StatusCode(500, new
                {
                    mensaje = "Error al crear usuario.",
                    detalle = ex.Message
                });
            }
        }

        // ================================================================
        // CAMBIAR ESTADO
        // PATCH /api/admin/usuarios/{id}/estado
        // ================================================================
        public class EstadoRequest
        {
            public bool Activo { get; set; }
        }

        [HttpPatch("usuarios/{id}/estado")]
        public async Task<IActionResult> CambiarEstado(int id, [FromBody] EstadoRequest req)
        {
            try
            {
                using var cn = new SqlConnection(GetConnection());

                var filas = await cn.ExecuteAsync(@"
UPDATE dbo.ApiUsuarios
SET Activo = @Activo
WHERE Id = @Id;",
                    new
                    {
                        Id = id,
                        Activo = req.Activo ? 1 : 0
                    });

                if (filas == 0)
                    return NotFound(new { mensaje = "Usuario no encontrado." });

                return Ok(new { mensaje = "Estado actualizado correctamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al actualizar estado.",
                    detalle = ex.Message
                });
            }
        }

        // ================================================================
        // RESET CLAVE
        // POST /api/admin/usuarios/{id}/reset-clave
        // ================================================================
        public class ResetClaveRequest
        {
            public string NuevaClave { get; set; } = "";
        }

        [HttpPost("usuarios/{id}/reset-clave")]
        public async Task<IActionResult> ResetClave(int id, [FromBody] ResetClaveRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.NuevaClave))
                return BadRequest(new { mensaje = "Debe indicar la nueva clave." });

            try
            {
                using var cn = new SqlConnection(GetConnection());
                var hash = HashSHA256Hex(req.NuevaClave);

                var filas = await cn.ExecuteAsync(@"
UPDATE dbo.ApiUsuarios
SET Clave = @Clave
WHERE Id = @Id;",
                    new
                    {
                        Id = id,
                        Clave = hash
                    });

                if (filas == 0)
                    return NotFound(new { mensaje = "Usuario no encontrado." });

                return Ok(new { mensaje = "Clave actualizada correctamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al resetear clave.",
                    detalle = ex.Message
                });
            }
        }

        // ================================================================
        // GENERAR LINK
        // POST /api/admin/usuarios/{id}/generar-link
        // ================================================================
        // Se deja con try/catch. Si la estructura real de ApiVendedorLinks
        // difiere, este endpoint se ajusta luego con un SELECT TOP 10 *.
        [HttpPost("usuarios/{id}/generar-link")]
        public async Task<IActionResult> GenerarLink(int id)
        {
            try
            {
                using var cn = new SqlConnection(GetConnection());

                // 1. Obtener SlpCode del usuario
                var slpCode = await cn.ExecuteScalarAsync<int?>(@"
SELECT SlpCode
FROM dbo.ApiUsuarios
WHERE Id = @Id;",
                    new { Id = id });

                if (slpCode == null)
                    return BadRequest(new { mensaje = "El usuario no tiene SlpCode asociado." });

                // 2. Verificar si ya existe link
                var existe = await cn.ExecuteScalarAsync<int>(@"
                                                                SELECT COUNT(1)
                                                                FROM dbo.ApiVendedorLinks
                                                                WHERE SlpCode = @SlpCode;",
                    new { SlpCode = slpCode });

                if (existe == 0)
                {
                    // INSERT (usa defaults de SQL: newid() y getdate())
                    await cn.ExecuteAsync(@"
                                                            INSERT INTO dbo.ApiVendedorLinks (SlpCode)
                                                            VALUES (@SlpCode);",
                        new { SlpCode = slpCode });
                }
                else
                {
                    // UPDATE (regenera token)
                    await cn.ExecuteAsync(@"
                                                        UPDATE dbo.ApiVendedorLinks
                                                        SET Token = NEWID(),
                                                            Activo = 1,
                                                            FechaCreacion = GETDATE()
                                                        WHERE SlpCode = @SlpCode;",
                        new { SlpCode = slpCode });
                }

                // 3. Obtener el nuevo token
                var token = await cn.ExecuteScalarAsync<Guid>(@"
                                                        SELECT Token
                                                        FROM dbo.ApiVendedorLinks
                                                        WHERE SlpCode = @SlpCode;",
                    new { SlpCode = slpCode });

                return Ok(new
                {
                    mensaje = "Link generado correctamente.",
                    link = $"https://app.spartan.cl/ingreso.html?token={token}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al generar link.",
                    detalle = ex.Message
                });
            }
        }

        // ================================================================
        // VER LINK
        // GET /api/admin/usuarios/{id}/link
        // ================================================================
        [HttpGet("usuarios/{id}/link")]
        public async Task<IActionResult> VerLink(int id)
        {
            try
            {
                using var cn = new SqlConnection(GetConnection());

                var sql = @"
                                DECLARE @BaseUrl NVARCHAR(200) = 'https://app.spartan.cl/ingreso.html?token=';

                                SELECT TOP 1
                                    Link = @BaseUrl + CONVERT(VARCHAR(36), l.Token)
                                FROM dbo.ApiUsuarios u
                                INNER JOIN dbo.ApiVendedorLinks l
                                    ON l.SlpCode = u.SlpCode
                                WHERE u.Id = @Id
                                  AND u.Activo = 1
                                  AND u.SlpCode IS NOT NULL
                                  AND ISNULL(l.Activo, 1) = 1;";

                var link = await cn.ExecuteScalarAsync<string?>(sql, new { Id = id });

                if (string.IsNullOrWhiteSpace(link))
                    return NotFound(new { mensaje = "El usuario no tiene link activo." });

                return Ok(new { link });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al obtener link.",
                    detalle = ex.Message
                });
            }
        }


    }
}