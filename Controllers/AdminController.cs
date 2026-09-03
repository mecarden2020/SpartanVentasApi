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
                   ?? throw new InvalidOperationException(
                       "No existe ConnectionStrings:SAP");
        }

        private static string HashSHA256Hex(string texto)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(
                Encoding.UTF8.GetBytes(texto));

            return Convert.ToHexString(bytes);
        }


        // ================================================================
        // DTO INTERNOS
        // ================================================================

        private sealed class UsuarioListadoDto
        {
            public int Id { get; set; }
            public string Login { get; set; } = "";
            public string Nombre { get; set; } = "";
            public bool Activo { get; set; }
            public int? SlpCode { get; set; }

            // Se conserva Rol para compatibilidad con el JS actual.
            public string Rol { get; set; } = "VENDEDOR";

            // Nueva propiedad multirol.
            public List<RolDto> Roles { get; set; } = new();

            public string? LinkAcceso { get; set; }
        }

        private sealed class RolDto
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = "";
        }

        private sealed class UsuarioBaseDb
        {
            public int Id { get; set; }
            public string Login { get; set; } = "";
            public string Nombre { get; set; } = "";
            public bool Activo { get; set; }
            public int? SlpCode { get; set; }
            public string? LinkAcceso { get; set; }
        }

        private sealed class UsuarioRolDb
        {
            public int UsuarioId { get; set; }
            public int RolId { get; set; }
            public string RolNombre { get; set; } = "";
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
                using var cn =
                    new SqlConnection(GetConnection());

                var sql = @"
SELECT 
    Id,
    Nombre
FROM dbo.ApiRoles
ORDER BY Nombre;";

                var data =
                    await cn.QueryAsync(sql);

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
        //
        // Ahora devuelve:
        //   - Rol   : rol principal, para compatibilidad con frontend actual
        //   - Roles : todos los roles asignados al usuario
        // ================================================================

        [HttpGet("usuarios")]
        public async Task<IActionResult> GetUsuarios()
        {
            try
            {
                using var cn =
                    new SqlConnection(GetConnection());

                await cn.OpenAsync();

                var sqlUsuarios = @"
DECLARE @BaseUrl NVARCHAR(200) =
    'https://app.spartan.cl/ingreso.html?token=';

SELECT
    u.Id,
    u.Usuario AS Login,
    u.Nombre,
    CAST(ISNULL(u.Activo, 0) AS bit) AS Activo,
    u.SlpCode,

    CASE
        WHEN l.Token IS NOT NULL
             AND ISNULL(l.Activo, 0) = 1
            THEN @BaseUrl + CONVERT(VARCHAR(36), l.Token)
        ELSE NULL
    END AS LinkAcceso

FROM dbo.ApiUsuarios u

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

                var usuariosDb =
                    (await cn.QueryAsync<UsuarioBaseDb>(
                        sqlUsuarios))
                    .ToList();


                var sqlRoles = @"
SELECT
    ur.UsuarioId,
    r.Id AS RolId,
    r.Nombre AS RolNombre
FROM dbo.ApiUsuarioRoles ur
INNER JOIN dbo.ApiRoles r
    ON r.Id = ur.RolId
ORDER BY
    ur.UsuarioId,
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

        WHEN UPPER(LTRIM(RTRIM(r.Nombre))) = 'VENDEDOR'
            THEN 5

        ELSE 99
    END,
    ur.Id DESC;";

                var rolesDb =
                    (await cn.QueryAsync<UsuarioRolDb>(
                        sqlRoles))
                    .ToList();


                var rolesPorUsuario =
                    rolesDb
                    .GroupBy(x => x.UsuarioId)
                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .Select(x => new RolDto
                            {
                                Id = x.RolId,
                                Nombre = x.RolNombre
                            })
                            .ToList()
                    );


                var resultado =
                    usuariosDb
                    .Select(u =>
                    {
                        var roles =
                            rolesPorUsuario.TryGetValue(
                                u.Id,
                                out var lista)
                                ? lista
                                : new List<RolDto>();

                        var rolPrincipal =
                            roles.FirstOrDefault()?.Nombre
                            ?? "VENDEDOR";

                        return new UsuarioListadoDto
                        {
                            Id = u.Id,
                            Login = u.Login,
                            Nombre = u.Nombre,
                            Activo = u.Activo,
                            SlpCode = u.SlpCode,
                            Rol = rolPrincipal,
                            Roles = roles,
                            LinkAcceso = u.LinkAcceso
                        };
                    })
                    .ToList();

                return Ok(resultado);
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
        // ROLES DE UN USUARIO
        // GET /api/admin/usuarios/{id}/roles
        // ================================================================

        [HttpGet("usuarios/{id}/roles")]
        public async Task<IActionResult> GetRolesUsuario(int id)
        {
            try
            {
                using var cn =
                    new SqlConnection(GetConnection());

                var existeUsuario =
                    await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.ApiUsuarios
WHERE Id = @Id;",
                        new { Id = id });

                if (existeUsuario == 0)
                {
                    return NotFound(new
                    {
                        mensaje = "Usuario no encontrado."
                    });
                }

                var roles =
                    await cn.QueryAsync<RolDto>(@"
SELECT
    r.Id,
    r.Nombre
FROM dbo.ApiUsuarioRoles ur
INNER JOIN dbo.ApiRoles r
    ON r.Id = ur.RolId
WHERE ur.UsuarioId = @UsuarioId
ORDER BY r.Nombre;",
                        new
                        {
                            UsuarioId = id
                        });

                return Ok(roles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje =
                        "Error al obtener roles del usuario.",
                    detalle = ex.Message
                });
            }
        }


        // ================================================================
        // AGREGAR ROL A USUARIO EXISTENTE
        // POST /api/admin/usuarios/{id}/roles
        // ================================================================

        public class AgregarRolRequest
        {
            public int? RolId { get; set; }
            public string Rol { get; set; } = "";
        }

        [HttpPost("usuarios/{id}/roles")]
        public async Task<IActionResult> AgregarRolUsuario(
            int id,
            [FromBody] AgregarRolRequest req)
        {
            using var cn =
                new SqlConnection(GetConnection());

            await cn.OpenAsync();

            using var tx =
                cn.BeginTransaction();

            try
            {
                var existeUsuario =
                    await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.ApiUsuarios
WHERE Id = @UsuarioId;",
                        new
                        {
                            UsuarioId = id
                        },
                        tx);

                if (existeUsuario == 0)
                {
                    tx.Rollback();

                    return NotFound(new
                    {
                        mensaje = "Usuario no encontrado."
                    });
                }


                int? rolId = req.RolId;

                if (!rolId.HasValue &&
                    !string.IsNullOrWhiteSpace(req.Rol))
                {
                    rolId =
                        await cn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1
    Id
FROM dbo.ApiRoles
WHERE UPPER(LTRIM(RTRIM(Nombre))) =
      UPPER(LTRIM(RTRIM(@Rol)));",
                            new
                            {
                                Rol = req.Rol
                            },
                            tx);
                }


                if (!rolId.HasValue)
                {
                    tx.Rollback();

                    return BadRequest(new
                    {
                        mensaje =
                            "Debe indicar un rol válido."
                    });
                }


                var existeRol =
                    await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.ApiRoles
WHERE Id = @RolId;",
                        new
                        {
                            RolId = rolId.Value
                        },
                        tx);

                if (existeRol == 0)
                {
                    tx.Rollback();

                    return BadRequest(new
                    {
                        mensaje =
                            "El rol indicado no existe."
                    });
                }


                var yaAsignado =
                    await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.ApiUsuarioRoles
WHERE UsuarioId = @UsuarioId
  AND RolId = @RolId;",
                        new
                        {
                            UsuarioId = id,
                            RolId = rolId.Value
                        },
                        tx);

                if (yaAsignado > 0)
                {
                    tx.Rollback();

                    return BadRequest(new
                    {
                        mensaje =
                            "El usuario ya tiene asignado ese rol."
                    });
                }


                await cn.ExecuteAsync(@"
INSERT INTO dbo.ApiUsuarioRoles
    (UsuarioId, RolId)
VALUES
    (@UsuarioId, @RolId);",
                    new
                    {
                        UsuarioId = id,
                        RolId = rolId.Value
                    },
                    tx);

                tx.Commit();

                return Ok(new
                {
                    mensaje =
                        "Rol agregado correctamente."
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();

                return StatusCode(500, new
                {
                    mensaje =
                        "Error al agregar rol al usuario.",
                    detalle = ex.Message
                });
            }
        }


        // ================================================================
        // QUITAR ROL A USUARIO
        // DELETE /api/admin/usuarios/{id}/roles/{rolId}
        //
        // Protección:
        // no permite dejar al usuario sin roles.
        // ================================================================

        [HttpDelete("usuarios/{id}/roles/{rolId}")]
        public async Task<IActionResult> QuitarRolUsuario(
            int id,
            int rolId)
        {
            using var cn =
                new SqlConnection(GetConnection());

            await cn.OpenAsync();

            using var tx =
                cn.BeginTransaction();

            try
            {
                var existeRelacion =
                    await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.ApiUsuarioRoles
WHERE UsuarioId = @UsuarioId
  AND RolId = @RolId;",
                        new
                        {
                            UsuarioId = id,
                            RolId = rolId
                        },
                        tx);

                if (existeRelacion == 0)
                {
                    tx.Rollback();

                    return NotFound(new
                    {
                        mensaje =
                            "El usuario no tiene asignado ese rol."
                    });
                }


                var cantidadRoles =
                    await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.ApiUsuarioRoles
WHERE UsuarioId = @UsuarioId;",
                        new
                        {
                            UsuarioId = id
                        },
                        tx);

                if (cantidadRoles <= 1)
                {
                    tx.Rollback();

                    return BadRequest(new
                    {
                        mensaje =
                            "No se puede quitar el último rol del usuario."
                    });
                }


                await cn.ExecuteAsync(@"
DELETE FROM dbo.ApiUsuarioRoles
WHERE UsuarioId = @UsuarioId
  AND RolId = @RolId;",
                    new
                    {
                        UsuarioId = id,
                        RolId = rolId
                    },
                    tx);

                tx.Commit();

                return Ok(new
                {
                    mensaje =
                        "Rol quitado correctamente."
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();

                return StatusCode(500, new
                {
                    mensaje =
                        "Error al quitar rol del usuario.",
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
        public async Task<IActionResult> CrearUsuario(
            [FromBody] CrearUsuarioRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Usuario) ||
                string.IsNullOrWhiteSpace(req.Nombre) ||
                string.IsNullOrWhiteSpace(req.Clave) ||
                string.IsNullOrWhiteSpace(req.Rol))
            {
                return BadRequest(new
                {
                    mensaje =
                        "Usuario, nombre, clave y rol son obligatorios."
                });
            }

            using var cn =
                new SqlConnection(GetConnection());

            await cn.OpenAsync();

            using var tx =
                cn.BeginTransaction();

            try
            {
                var existe =
                    await cn.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(1)
                          FROM dbo.ApiUsuarios
                          WHERE Usuario = @Usuario",
                        new
                        {
                            req.Usuario
                        },
                        tx);

                if (existe > 0)
                {
                    tx.Rollback();

                    return BadRequest(new
                    {
                        mensaje = "El usuario ya existe."
                    });
                }

                var hash =
                    HashSHA256Hex(req.Clave);


                var usuarioId =
                    await cn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ApiUsuarios
    (Usuario, Nombre, Clave, SlpCode, Activo)
VALUES
    (@Usuario, @Nombre, @Clave, @SlpCode, 1);

SELECT CAST(SCOPE_IDENTITY() AS INT);",
                        new
                        {
                            req.Usuario,
                            req.Nombre,
                            Clave = hash,
                            req.SlpCode
                        },
                        tx);


                var rolId =
                    await cn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1
    Id
FROM dbo.ApiRoles
WHERE LTRIM(RTRIM(Nombre)) =
      LTRIM(RTRIM(@Rol));",
                        new
                        {
                            req.Rol
                        },
                        tx);

                if (rolId == null)
                {
                    tx.Rollback();

                    return BadRequest(new
                    {
                        mensaje =
                            "El rol indicado no existe."
                    });
                }


                await cn.ExecuteAsync(@"
INSERT INTO dbo.ApiUsuarioRoles
    (UsuarioId, RolId)
VALUES
    (@UsuarioId, @RolId);",
                    new
                    {
                        UsuarioId = usuarioId,
                        RolId = rolId.Value
                    },
                    tx);

                tx.Commit();

                return Ok(new
                {
                    mensaje =
                        "Usuario creado correctamente.",
                    usuarioId
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();

                return StatusCode(500, new
                {
                    mensaje =
                        "Error al crear usuario.",
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
        public async Task<IActionResult> CambiarEstado(
            int id,
            [FromBody] EstadoRequest req)
        {
            try
            {
                using var cn =
                    new SqlConnection(GetConnection());

                var filas =
                    await cn.ExecuteAsync(@"
UPDATE dbo.ApiUsuarios
SET Activo = @Activo
WHERE Id = @Id;",
                        new
                        {
                            Id = id,
                            Activo = req.Activo ? 1 : 0
                        });

                if (filas == 0)
                {
                    return NotFound(new
                    {
                        mensaje =
                            "Usuario no encontrado."
                    });
                }

                return Ok(new
                {
                    mensaje =
                        "Estado actualizado correctamente."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje =
                        "Error al actualizar estado.",
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
        public async Task<IActionResult> ResetClave(
            int id,
            [FromBody] ResetClaveRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.NuevaClave))
            {
                return BadRequest(new
                {
                    mensaje =
                        "Debe indicar la nueva clave."
                });
            }

            try
            {
                using var cn =
                    new SqlConnection(GetConnection());

                var hash =
                    HashSHA256Hex(req.NuevaClave);

                var filas =
                    await cn.ExecuteAsync(@"
UPDATE dbo.ApiUsuarios
SET Clave = @Clave
WHERE Id = @Id;",
                        new
                        {
                            Id = id,
                            Clave = hash
                        });

                if (filas == 0)
                {
                    return NotFound(new
                    {
                        mensaje =
                            "Usuario no encontrado."
                    });
                }

                return Ok(new
                {
                    mensaje =
                        "Clave actualizada correctamente."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje =
                        "Error al resetear clave.",
                    detalle = ex.Message
                });
            }
        }


        // ================================================================
        // GENERAR LINK
        // POST /api/admin/usuarios/{id}/generar-link
        // ================================================================

        [HttpPost("usuarios/{id}/generar-link")]
        public async Task<IActionResult> GenerarLink(int id)
        {
            try
            {
                using var cn =
                    new SqlConnection(GetConnection());

                // 1. Obtener SlpCode del usuario
                var slpCode =
                    await cn.ExecuteScalarAsync<int?>(@"
SELECT SlpCode
FROM dbo.ApiUsuarios
WHERE Id = @Id;",
                        new
                        {
                            Id = id
                        });

                if (slpCode == null)
                {
                    return BadRequest(new
                    {
                        mensaje =
                            "El usuario no tiene SlpCode asociado."
                    });
                }


                // 2. Verificar si ya existe link
                var existe =
                    await cn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.ApiVendedorLinks
WHERE SlpCode = @SlpCode;",
                        new
                        {
                            SlpCode = slpCode
                        });

                if (existe == 0)
                {
                    await cn.ExecuteAsync(@"
INSERT INTO dbo.ApiVendedorLinks
    (SlpCode)
VALUES
    (@SlpCode);",
                        new
                        {
                            SlpCode = slpCode
                        });
                }
                else
                {
                    await cn.ExecuteAsync(@"
UPDATE dbo.ApiVendedorLinks
SET Token = NEWID(),
    Activo = 1,
    FechaCreacion = GETDATE()
WHERE SlpCode = @SlpCode;",
                        new
                        {
                            SlpCode = slpCode
                        });
                }


                // 3. Obtener el nuevo token
                var token =
                    await cn.ExecuteScalarAsync<Guid>(@"
SELECT Token
FROM dbo.ApiVendedorLinks
WHERE SlpCode = @SlpCode;",
                        new
                        {
                            SlpCode = slpCode
                        });

                return Ok(new
                {
                    mensaje =
                        "Link generado correctamente.",
                    link =
                        $"https://app.spartan.cl/ingreso.html?token={token}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje =
                        "Error al generar link.",
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
                using var cn =
                    new SqlConnection(GetConnection());

                var sql = @"
DECLARE @BaseUrl NVARCHAR(200) =
    'https://app.spartan.cl/ingreso.html?token=';

SELECT TOP 1
    Link =
        @BaseUrl +
        CONVERT(VARCHAR(36), l.Token)
FROM dbo.ApiUsuarios u
INNER JOIN dbo.ApiVendedorLinks l
    ON l.SlpCode = u.SlpCode
WHERE u.Id = @Id
  AND u.Activo = 1
  AND u.SlpCode IS NOT NULL
  AND ISNULL(l.Activo, 1) = 1;";

                var link =
                    await cn.ExecuteScalarAsync<string?>(
                        sql,
                        new
                        {
                            Id = id
                        });

                if (string.IsNullOrWhiteSpace(link))
                {
                    return NotFound(new
                    {
                        mensaje =
                            "El usuario no tiene link activo."
                    });
                }

                return Ok(new
                {
                    link
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje =
                        "Error al obtener link.",
                    detalle = ex.Message
                });
            }
        }
    }
}
