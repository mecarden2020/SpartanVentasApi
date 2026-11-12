using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Globalization;
using SpartanVentasApi.Models.RindeSpartan.DTO;

namespace SpartanVentasApi.Controllers.RindeSpartan
{
    [ApiController]
    [Route("api/rindespartan")]
    public class RindeController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public RindeController(IConfiguration configuration, IWebHostEnvironment env)
        {
            _config = configuration;
            _env = env;
        }





        // ===================  POST / Nueva =============================


        [HttpPost("nueva")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CrearNuevaRendicion(
            [FromForm] RindeNuevaSolicitudRequest request)
        {
            try
            {
                // ==========================================
                // 1. VALIDACIONES GENERALES
                // ==========================================

                if (request.Documentos == null || request.Documentos.Count == 0)
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "Debe agregar al menos un documento."
                    });

                if (string.IsNullOrWhiteSpace(request.Justificacion))
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "Debe ingresar una justificación."
                    });

                // Compatibilidad con formulario anterior
                var tipoGasto = string.IsNullOrWhiteSpace(request.TipoGasto)
                    ? "MULTIPLE"
                    : request.TipoGasto.Trim();

                if (request.PeriodoDesde.HasValue &&
                    request.PeriodoHasta.HasValue &&
                    request.PeriodoDesde.Value.Date > request.PeriodoHasta.Value.Date)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "El inicio del período no puede superar su fecha de término."
                    });
                }

                if (request.ProvisionAnticipo.HasValue &&
                    request.ProvisionAnticipo.Value < 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "La provisión de anticipo no puede ser negativa."
                    });
                }

                if (request.SupervisorUsuarioId.HasValue &&
                    request.SupervisorUsuarioId.Value <= 0)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "El supervisor indicado no es válido."
                    });
                }

                var extensionesPermitidas = new[]
                {
            ".pdf", ".jpg", ".jpeg", ".png"
        };

                var categoriasPermitidas = new[]
                {
            "ALOJAMIENTO",
            "COMIDAS",
            "TRANSPORTE",
            "COMBUSTIBLE",
            "OTROS"
        };

                foreach (var doc in request.Documentos)
                {
                    if (doc.ArchivoDocumento == null ||
                        doc.ArchivoDocumento.Length == 0)
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Todos los documentos deben tener archivo adjunto."
                        });

                    if (doc.Monto <= 0)
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Todos los documentos deben tener monto mayor a cero."
                        });

                    if (string.IsNullOrWhiteSpace(doc.Proveedor))
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Todos los documentos deben tener proveedor."
                        });

                    if (string.IsNullOrWhiteSpace(doc.TipoDocumento))
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Todos los documentos deben tener tipo de documento."
                        });

                    if (doc.FechaDocumento == default)
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Todos los documentos deben tener fecha."
                        });

                    if (!string.IsNullOrWhiteSpace(doc.CategoriaGasto) &&
                        !categoriasPermitidas.Contains(
                            doc.CategoriaGasto.Trim().ToUpperInvariant()))
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Uno de los documentos tiene una categoría no válida."
                        });

                    var extension = Path.GetExtension(
                        doc.ArchivoDocumento.FileName).ToLowerInvariant();

                    if (!extensionesPermitidas.Contains(extension))
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Formato no permitido. Solo PDF, JPG, JPEG o PNG."
                        });

                    if (doc.ArchivoDocumento.Length > 5 * 1024 * 1024)
                        return BadRequest(new
                        {
                            ok = false,
                            mensaje = "Uno de los archivos supera los 5 MB permitidos."
                        });
                }

                // ==========================================
                // 2. IDENTIFICACIÓN DEL USUARIO
                // ==========================================

                // TEMPORAL: reemplazar por JWT / sesión.
                int usuarioId = 1;
                string usuarioLogin = "mcardenas";

                // ==========================================
                // 3. CÁLCULOS DE LA RENDICIÓN
                // ==========================================

                decimal montoTotal = request.Documentos.Sum(x => x.Monto);

                DateTime fechaDocumentoPrincipal =
                    request.Documentos.Min(x => x.FechaDocumento).Date;

                DateTime fechaInforme =
                    request.FechaInforme?.Date ?? DateTime.Today;

                string objetivo = string.IsNullOrWhiteSpace(request.Objetivo)
                    ? request.Justificacion.Trim()
                    : request.Objetivo.Trim();

                decimal provisionAnticipo = request.ProvisionAnticipo ?? 0m;

                // ==========================================
                // 4. CARPETA DE COMPROBANTES
                // ==========================================

                string anio = DateTime.Now.Year.ToString();
                string mes = DateTime.Now.Month.ToString("00");

                string carpetaRelativa = Path.Combine(
                    "uploads",
                    "rindespartan",
                    anio,
                    mes,
                    usuarioLogin
                );

                string carpetaFisica = Path.Combine(
                    _env.WebRootPath,
                    carpetaRelativa
                );

                Directory.CreateDirectory(carpetaFisica);

                // Archivos que deberán eliminarse si falla SQL
                var archivosCreados = new List<string>();

                try
                {
                    using var cn = new SqlConnection(
                        _config.GetConnectionString("SAP"));

                    await cn.OpenAsync();

                    using var tx = cn.BeginTransaction();

                    try
                    {
                        // ==========================================
                        // 5. CABECERA
                        // ==========================================

                        const string sqlCabecera = @"
INSERT INTO dbo.RindeSolicitudes
(
    UsuarioId,
    FechaDocumento,
    TipoDocumento,
    NumeroDocumento,
    Proveedor,
    TipoGasto,
    Justificacion,
    Monto,
    Estado,
    RutaArchivo,
    NombreArchivo,
    ExtensionArchivo,
    PesoArchivoKB,
    FechaCreacion,

    Objetivo,
    FechaInforme,
    PeriodoDesde,
    PeriodoHasta,
    SupervisorUsuarioId,
    ProvisionAnticipo
)
VALUES
(
    @UsuarioId,
    @FechaDocumento,
    'MULTIPLE',
    NULL,
    'VARIOS DOCUMENTOS',
    @TipoGasto,
    @Justificacion,
    @Monto,
    'PENDIENTE',
    NULL,
    NULL,
    NULL,
    NULL,
    GETDATE(),

    @Objetivo,
    @FechaInforme,
    @PeriodoDesde,
    @PeriodoHasta,
    @SupervisorUsuarioId,
    @ProvisionAnticipo
);

SELECT CAST(SCOPE_IDENTITY() AS INT);
";

                        int solicitudId = await cn.ExecuteScalarAsync<int>(
                            sqlCabecera,
                            new
                            {
                                UsuarioId = usuarioId,
                                FechaDocumento = fechaDocumentoPrincipal,
                                TipoGasto = tipoGasto,
                                Justificacion = request.Justificacion.Trim(),
                                Monto = montoTotal,

                                Objetivo = objetivo,
                                FechaInforme = fechaInforme,
                                PeriodoDesde = request.PeriodoDesde?.Date,
                                PeriodoHasta = request.PeriodoHasta?.Date,
                                SupervisorUsuarioId = request.SupervisorUsuarioId,
                                ProvisionAnticipo = provisionAnticipo
                            },
                            tx
                        );

                        // ==========================================
                        // 6. FOLIO CORRELATIVO
                        // ==========================================

                        string folioRinde =
                            $"RS-{DateTime.Now.Year}-{solicitudId:D6}";

                        await cn.ExecuteAsync(
                            @"
UPDATE dbo.RindeSolicitudes
SET FolioRinde = @FolioRinde
WHERE Id = @SolicitudId;",
                            new
                            {
                                FolioRinde = folioRinde,
                                SolicitudId = solicitudId
                            },
                            tx
                        );

                        // ==========================================
                        // 7. DOCUMENTOS Y COMPROBANTES
                        // ==========================================

                        const string sqlDetalle = @"
INSERT INTO dbo.RindeSolicitudesAdjuntos
(
    SolicitudId,
    TipoDocumento,
    NumeroDocumento,
    Proveedor,
    Monto,
    RutaArchivo,
    NombreArchivo,
    ExtensionArchivo,
    PesoArchivoKB,
    FechaCreacion,

    FechaGasto,
    DescripcionGasto,
    CategoriaGasto
)
VALUES
(
    @SolicitudId,
    @TipoDocumento,
    @NumeroDocumento,
    @Proveedor,
    @Monto,
    @RutaArchivo,
    @NombreArchivo,
    @ExtensionArchivo,
    @PesoArchivoKB,
    GETDATE(),

    @FechaGasto,
    @DescripcionGasto,
    @CategoriaGasto
);";

                        int contador = 1;

                        foreach (var doc in request.Documentos)
                        {
                            var archivo = doc.ArchivoDocumento!;

                            string extension = Path.GetExtension(
                                archivo.FileName).ToLowerInvariant();

                            string nombreArchivo =
                                $"RINDE_{solicitudId}_{contador}_" +
                                $"{Guid.NewGuid():N}{extension}";

                            string rutaFisicaArchivo = Path.Combine(
                                carpetaFisica,
                                nombreArchivo
                            );

                            await using (var stream = new FileStream(
                                rutaFisicaArchivo,
                                FileMode.CreateNew))
                            {
                                // Registrar antes de copiar, para limpiar
                                // también si falla la transferencia.
                                archivosCreados.Add(rutaFisicaArchivo);
                                await archivo.CopyToAsync(stream);
                            }

                            string rutaWeb = "/" + Path.Combine(
                                carpetaRelativa,
                                nombreArchivo
                            ).Replace("\\", "/");

                            string? categoria = string.IsNullOrWhiteSpace(
                                doc.CategoriaGasto)
                                ? null
                                : doc.CategoriaGasto.Trim().ToUpperInvariant();

                            await cn.ExecuteAsync(
                                sqlDetalle,
                                new
                                {
                                    SolicitudId = solicitudId,
                                    doc.TipoDocumento,
                                    doc.NumeroDocumento,
                                    doc.Proveedor,
                                    doc.Monto,

                                    RutaArchivo = rutaWeb,
                                    NombreArchivo = nombreArchivo,
                                    ExtensionArchivo = extension,
                                    PesoArchivoKB =
                                        (int)Math.Ceiling(archivo.Length / 1024m),

                                    FechaGasto =
                                        doc.FechaGasto?.Date ??
                                        doc.FechaDocumento.Date,

                                    DescripcionGasto = doc.DescripcionGasto,
                                    CategoriaGasto = categoria
                                },
                                tx
                            );

                            contador++;
                        }

                        // ==========================================
                        // 8. AUDITORÍA DE CREACIÓN
                        // ==========================================

                        await cn.ExecuteAsync(
                            @"
INSERT INTO dbo.RindeAuditoria
(
    SolicitudId,
    UsuarioId,
    Accion,
    EstadoAnterior,
    EstadoNuevo,
    Observacion,
    FechaAccion,
    Origen
)
VALUES
(
    @SolicitudId,
    @UsuarioId,
    'CREADA',
    NULL,
    'PENDIENTE',
    @Observacion,
    GETDATE(),
    'WEB'
);",
                            new
                            {
                                SolicitudId = solicitudId,
                                UsuarioId = usuarioId,
                                Observacion = request.Justificacion.Trim()
                            },
                            tx
                        );

                        // ==========================================
                        // 9. CONFIRMAR TRANSACCIÓN
                        // ==========================================

                        tx.Commit();

                        return Ok(new
                        {
                            ok = true,
                            mensaje = "Rendición registrada correctamente.",
                            id = solicitudId,
                            folioRinde,
                            montoTotal,
                            cantidadDocumentos = request.Documentos.Count,
                            provisionAnticipo
                        });
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
                catch
                {
                    // Compensación de archivos físicos ante fallo
                    foreach (string ruta in archivosCreados)
                    {
                        try
                        {
                            if (System.IO.File.Exists(ruta))
                                System.IO.File.Delete(ruta);
                        }
                        catch
                        {
                            // No ocultar el error original.
                            // Pendiente registrar en logging.
                        }
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al registrar la rendición.",
                    detalle = ex.Message
                });
            }
        }




        // ================================================================================================








        [HttpGet("mis-rendiciones")]
        public async Task<IActionResult> GetMisRendiciones()
        {
            try
            {
                // TEMPORAL: luego se obtendrá desde JWT / sesión
                int usuarioId = 1;

                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));

                var sql = @"
            SELECT TOP 100
                Id,
                UsuarioId,
                FechaDocumento,
                TipoDocumento,
                NumeroDocumento,
                Proveedor,
                TipoGasto,
                Justificacion,
                Monto,
                Estado,
                RutaArchivo,
                NombreArchivo,
                ExtensionArchivo,
                PesoArchivoKB,
                FechaCreacion,
                FechaAprobacion,
                AprobadoPorUsuarioId,
                ObservacionAprobador,
                FechaRechazo,
                RechazadoPorUsuarioId,
                ObservacionRechazo
            FROM dbo.RindeSolicitudes
            WHERE UsuarioId = @UsuarioId
            ORDER BY Id DESC;
        ";

                var data = await cn.QueryAsync(sql, new
                {
                    UsuarioId = usuarioId
                });

                return Ok(new
                {
                    ok = true,
                    data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al obtener mis rendiciones.",
                    detalle = ex.Message
                });
            }
        }




        [HttpGet("pendientes-aprobacion")]
        public async Task<IActionResult> GetPendientesAprobacion()
        {
            try
            {
                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));

                var sql = @"
            SELECT
                Id,
                UsuarioId,
                FechaDocumento,
                TipoDocumento,
                NumeroDocumento,
                Proveedor,
                TipoGasto,
                Justificacion,
                Monto,
                Estado,
                RutaArchivo,
                NombreArchivo,
                FechaCreacion
            FROM dbo.RindeSolicitudes
            WHERE Estado = 'PENDIENTE'
            ORDER BY Id DESC;
        ";

                var data = await cn.QueryAsync(sql);

                return Ok(new
                {
                    ok = true,
                    data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al obtener pendientes.",
                    detalle = ex.Message
                });
            }
        }


        [HttpPost("aprobar")]
        public async Task<IActionResult> AprobarRendicion([FromBody] RindeAprobacionRequest request)
        {
            try
            {
                if (request.SolicitudId <= 0)
                    return BadRequest(new { ok = false, mensaje = "Solicitud inválida." });

                // TEMPORAL: luego se obtendrá desde JWT / sesión
                int usuarioAprobadorId = 1;

                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));

                var sql = @"
        UPDATE dbo.RindeSolicitudes
        SET
            Estado = 'APROBADO',
            FechaAprobacion = GETDATE(),
            AprobadoPorUsuarioId = @UsuarioAprobadorId,
            ObservacionAprobador = @Observacion,
            FechaRechazo = NULL,
            RechazadoPorUsuarioId = NULL,
            ObservacionRechazo = NULL
        WHERE Id = @SolicitudId
          AND Estado = 'PENDIENTE';

        INSERT INTO dbo.RindeAprobaciones
        (
            SolicitudId,
            UsuarioAprobadorId,
            Accion,
            Observacion,
            FechaAccion
        )
        VALUES
        (
            @SolicitudId,
            @UsuarioAprobadorId,
            'APROBADO',
            @Observacion,
            GETDATE()
        );

        -- =========================================
        -- AUDITORÍA
        -- =========================================
        INSERT INTO dbo.RindeAuditoria
        (
            SolicitudId,
            UsuarioId,
            Accion,
            EstadoAnterior,
            EstadoNuevo,
            Observacion,
            FechaAccion,
            Origen
        )
        VALUES
        (
            @SolicitudId,
            @UsuarioAprobadorId,
            'APROBADA',
            'PENDIENTE',
            'APROBADO',
            @Observacion,
            GETDATE(),
            'WEB'
        );

        SELECT @@ROWCOUNT;
        ";

                var filas = await cn.ExecuteAsync(sql, new
                {
                    request.SolicitudId,
                    UsuarioAprobadorId = usuarioAprobadorId,
                    request.Observacion
                });

                return Ok(new
                {
                    ok = true,
                    mensaje = "Rendición aprobada correctamente."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al aprobar la rendición.",
                    detalle = ex.Message
                });
            }
        }

        [HttpPost("rechazar")]
        public async Task<IActionResult> RechazarRendicion([FromBody] RindeAprobacionRequest request)
        {
            try
            {
                if (request.SolicitudId <= 0)
                    return BadRequest(new { ok = false, mensaje = "Solicitud inválida." });

                // TEMPORAL: luego se obtendrá desde JWT / sesión
                int usuarioRechazoId = 1;

                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));

                var sql = @"
        UPDATE dbo.RindeSolicitudes
        SET
            Estado = 'RECHAZADO',
            FechaRechazo = GETDATE(),
            RechazadoPorUsuarioId = @UsuarioRechazoId,
            ObservacionRechazo = @Observacion,
            FechaAprobacion = NULL,
            AprobadoPorUsuarioId = NULL,
            ObservacionAprobador = NULL
        WHERE Id = @SolicitudId
          AND Estado = 'PENDIENTE';

        INSERT INTO dbo.RindeAprobaciones
        (
            SolicitudId,
            UsuarioAprobadorId,
            Accion,
            Observacion,
            FechaAccion
        )
        VALUES
        (
            @SolicitudId,
            @UsuarioRechazoId,
            'RECHAZADO',
            @Observacion,
            GETDATE()
        );

        -- =========================================
        -- AUDITORÍA
        -- =========================================
        INSERT INTO dbo.RindeAuditoria
        (
            SolicitudId,
            UsuarioId,
            Accion,
            EstadoAnterior,
            EstadoNuevo,
            Observacion,
            FechaAccion,
            Origen
        )
        VALUES
        (
            @SolicitudId,
            @UsuarioRechazoId,
            'RECHAZADA',
            'PENDIENTE',
            'RECHAZADO',
            @Observacion,
            GETDATE(),
            'WEB'
        );

        SELECT @@ROWCOUNT;
        ";

                var filas = await cn.ExecuteAsync(sql, new
                {
                    request.SolicitudId,
                    UsuarioRechazoId = usuarioRechazoId,
                    request.Observacion
                });

                return Ok(new
                {
                    ok = true,
                    mensaje = "Rendición rechazada correctamente."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al rechazar la rendición.",
                    detalle = ex.Message
                });
            }
        }




        [HttpGet("saldo")]
        public async Task<IActionResult> GetSaldoUsuario()
        {
            try
            {
                // TEMPORAL: luego se obtendrá desde JWT / sesión
                int usuarioId = 1;

                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));

                var sql = @"
            SELECT
                TopeMensual = ISNULL(MAX(C.TopeMensual), 0),

                TotalRendido = ISNULL(SUM(S.Monto), 0),

                TotalPendiente = ISNULL(SUM(CASE 
                    WHEN S.Estado = 'PENDIENTE' THEN S.Monto 
                    ELSE 0 
                END), 0),

                TotalAprobado = ISNULL(SUM(CASE 
                    WHEN S.Estado = 'APROBADO' THEN S.Monto 
                    ELSE 0 
                END), 0),

                TotalRechazado = ISNULL(SUM(CASE 
                    WHEN S.Estado = 'RECHAZADO' THEN S.Monto 
                    ELSE 0 
                END), 0),

                SaldoDisponible = ISNULL(MAX(C.TopeMensual), 0)
                    - ISNULL(SUM(CASE 
                        WHEN S.Estado IN ('PENDIENTE', 'APROBADO') THEN S.Monto 
                        ELSE 0 
                    END), 0)

            FROM dbo.RindeUsuariosConfig C
            LEFT JOIN dbo.RindeSolicitudes S
                ON S.UsuarioId = C.UsuarioId
            WHERE C.UsuarioId = @UsuarioId
              AND C.Activo = 1;
        ";

                var saldo = await cn.QueryFirstOrDefaultAsync(sql, new
                {
                    UsuarioId = usuarioId
                });

                return Ok(new
                {
                    ok = true,
                    data = saldo
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al obtener saldo del usuario.",
                    detalle = ex.Message
                });
            }
        }




        [HttpGet("detalle/{id:int}")]
        public async Task<IActionResult> GetDetalleRendicion(int id)
        {
            try
            {
                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));

                var sqlCabecera = @"
            SELECT
                Id,
                FolioRinde,
                UsuarioId,
                FechaDocumento,
                TipoDocumento,
                NumeroDocumento,
                Proveedor,
                TipoGasto,
                Justificacion,
                Monto,
                Estado,
                FechaCreacion,
                FechaAprobacion,
                AprobadoPorUsuarioId,
                ObservacionAprobador,
                FechaRechazo,
                RechazadoPorUsuarioId,
                ObservacionRechazo
            FROM dbo.RindeSolicitudes
            WHERE Id = @Id;
        ";

                var sqlAdjuntos = @"
            SELECT
                Id,
                SolicitudId,
                TipoDocumento,
                NumeroDocumento,
                Proveedor,
                Monto,
                RutaArchivo,
                NombreArchivo,
                ExtensionArchivo,
                PesoArchivoKB,
                FechaCreacion
            FROM dbo.RindeSolicitudesAdjuntos
            WHERE SolicitudId = @Id
            ORDER BY Id;
        ";

                var cabecera = await cn.QueryFirstOrDefaultAsync(sqlCabecera, new { Id = id });

                if (cabecera == null)
                    return NotFound(new { ok = false, mensaje = "No se encontró la rendición." });

                var adjuntos = await cn.QueryAsync(sqlAdjuntos, new { Id = id });

                return Ok(new
                {
                    ok = true,
                    cabecera,
                    adjuntos
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al obtener detalle de la rendición.",
                    detalle = ex.Message
                });
            }
        }

















    }
}
