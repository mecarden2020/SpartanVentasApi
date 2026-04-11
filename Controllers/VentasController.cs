using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SpartanVentasApi.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using ClosedXML.Excel;
using System.IO;
using System.Threading.Tasks;
using Dapper;

namespace SpartanVentasApi.Controllers
{
    // ============================================================
    // Controller principal: /api/ventas/...
    // ============================================================
    [ApiController]
    [Route("api/ventas")]
    [Authorize]
    public class VentasController : ControllerBase
    {
        private readonly IConfiguration _config;

        public VentasController(IConfiguration config)
        {
            _config = config;
        }

        // --------------------------------------------------------
        // Helpers
        // --------------------------------------------------------
        private (string rol, int? slpCode) GetRolYSlpCodeForzado(int? slpCodeQuery = null)
        {
            var rol = User.FindFirst(ClaimTypes.Role)?.Value
                      ?? User.FindFirst("role")?.Value
                      ?? string.Empty;

            var slpClaim = User.FindFirst("SlpCode")?.Value
                          ?? User.FindFirst("slpCode")?.Value;

            // Si es VENDEDOR => forzar SlpCode desde token
            if (rol.Equals("VENDEDOR", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(slpClaim, out var slpFromToken))
                    throw new UnauthorizedAccessException("Vendedor sin SlpCode válido en el token.");

                return (rol, slpFromToken);
            }

            return (rol, slpCodeQuery);
        }

        // ============================================================
        // GET: /api/ventas/periodo
        // ============================================================
        [HttpGet("periodo")]
        public IActionResult GetPeriodo(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode,
            [FromQuery] string? cardCode)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            string rol;
            int? slpFinal;

            try
            {
                (rol, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var ventas = new List<VentaPeriodoDto>();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("sp_VentasPeriodoVendedorCliente", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ventas.Add(new VentaPeriodoDto
                        {
                            Anio = reader["Anio"] == DBNull.Value ? 0 : (int)reader["Anio"],
                            Mes = reader["Mes"] == DBNull.Value ? 0 : (int)reader["Mes"],
                            NombreMes = reader["NombreMes"] as string ?? string.Empty,
                            CodigoCliente = reader["CardCode"] as string ?? string.Empty,
                            NombreCliente = reader["CardName"] as string ?? string.Empty,
                            VentaNetaSN = reader["VentaNetaSN"] == DBNull.Value ? 0m : (decimal)reader["VentaNetaSN"],
                            CantFacturas = reader["CantFacturas"] == DBNull.Value ? 0 : (int)reader["CantFacturas"],
                            CantNC = reader["CantNC"] == DBNull.Value ? 0 : (int)reader["CantNC"]
                        });
                    }
                }
            }

            Console.WriteLine($"[ventas/periodo] rol={rol}, slpCode={slpFinal}, filas={ventas.Count}");
            return Ok(ventas);
        }

        // ============================================================
        // GET: /api/ventas/periodo-clientes
        // ============================================================
        [HttpGet("periodo-clientes")]
        public IActionResult GetPeriodoClientes(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode,
            [FromQuery] string? cardCode)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            string rol;
            int? slpFinal;

            try
            {
                (rol, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var rows = new List<VentaClienteDetalleDto>();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("sp_VentasPeriodoVendedorCliente_ClientesQuimicos", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new VentaClienteDetalleDto
                        {
                            CardCode = reader["CardCode"] as string ?? "",
                            CardName = reader["CardName"] as string ?? "",
                            NetoCliente = reader["NetoCliente"] == DBNull.Value ? 0m : (decimal)reader["NetoCliente"],
                            DetalleJson = reader["DetalleJson"] as string ?? "[]"
                        });
                    }
                }
            }

            Console.WriteLine($"[ventas/periodo-clientes] rol={rol}, slpCode={slpFinal}, filas={rows.Count}");
            return Ok(rows);
        }


        // ========================================================================
        // CLIENTES NUEVOS QUÍMICOS (Gerencia / Portal)
        // ========================================================================
        [HttpGet("clientes-nuevos-quimicos")]
        public async Task<IActionResult> GetClientesNuevosQuimicos(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
                if (hasta < desde)
                    return BadRequest("'Hasta' no puede ser menor que 'Desde'.");

                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));

                var p = new DynamicParameters();
                p.Add("@Desde", desde);
                p.Add("@Hasta", hasta);

                using var multi = await cn.QueryMultipleAsync(
                    "sp_ClientesNuevos_Quimicos",   // <- tu SP real
                    p,
                    commandType: CommandType.StoredProcedure
                );

                var resumen = (await multi.ReadAsync()).ToList();
                var detalle = (await multi.ReadAsync()).ToList();

                return Ok(new { resumen, detalle });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error obteniendo Clientes Nuevos Químicos: {ex.Message}");
            }
        }








        // ============================================================
        // GET: /api/ventas/periodo-lineas-quimicos-excel
        // ============================================================
        [HttpGet("periodo-lineas-quimicos-excel")]
        public IActionResult ExportPeriodoLineasQuimicosExcel(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode,
            [FromQuery] string? cardCode)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            int? slpFinal;
            try
            {
                (_, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var dt = new DataTable("LineasQuimicos");
            dt.Columns.Add("YEAR", typeof(int));
            dt.Columns.Add("NombreMes", typeof(string));
            dt.Columns.Add("TipoDoc", typeof(string));
            dt.Columns.Add("DocDate", typeof(DateTime));
            dt.Columns.Add("FolioNum", typeof(int));
            dt.Columns.Add("CardCode", typeof(string));
            dt.Columns.Add("CardName", typeof(string));
            dt.Columns.Add("ItemCode", typeof(string));
            dt.Columns.Add("Dscription", typeof(string));
            dt.Columns.Add("Quantity", typeof(decimal));
            dt.Columns.Add("Precio", typeof(decimal));
            dt.Columns.Add("NetoLinea", typeof(decimal));

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("dbo.sp_VentasPeriodoVendedorCliente_QuimicosLineas", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());

                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        dt.Rows.Add(
                            r["YEAR"] == DBNull.Value ? 0 : Convert.ToInt32(r["YEAR"]),
                            r["NombreMes"] as string ?? "",
                            r["TipoDoc"] as string ?? "",
                            r["DocDate"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(r["DocDate"]),
                            r["FolioNum"] == DBNull.Value ? 0 : Convert.ToInt32(r["FolioNum"]),
                            r["CardCode"] as string ?? "",
                            r["CardName"] as string ?? "",
                            r["ItemCode"] as string ?? "",
                            r["Dscription"] as string ?? "",
                            r["Quantity"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Quantity"]),
                            r["Precio"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Precio"]),
                            r["NetoLinea"] == DBNull.Value ? 0m : Convert.ToDecimal(r["NetoLinea"])
                        );
                    }
                }
            }

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(dt, "Lineas_Quimicos");

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();

            ws.Column("D").Style.DateFormat.Format = "yyyy-mm-dd";
            ws.Column("M").Style.NumberFormat.Format = "#,##0.00";
            ws.Column("N").Style.NumberFormat.Format = "#,##0";
            ws.Column("O").Style.NumberFormat.Format = "#,##0";

            var table = ws.Tables.FirstOrDefault();
            if (table != null) table.ShowAutoFilter = true;

            var fn = $"ventas_quimicos_lineas_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            return File(
                ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fn
            );
        }

        // ============================================================
        // GET: /api/ventas/ranking
        // ============================================================
        [HttpGet("ranking")]
        public IActionResult GetRanking(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            int? slpFinal;
            try
            {
                (_, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var tabla = new List<Dictionary<string, object?>>();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("SP_Ranking_Ventas_Pivot", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var fila = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var nombreCol = reader.GetName(i);
                            object? valor = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            fila[nombreCol] = valor;
                        }

                        tabla.Add(fila);
                    }
                }
            }

            return Ok(tabla);
        }

        // ============================================================
        // GET: /api/ventas/dashboard1
        // ============================================================
        [HttpGet("dashboard1")]
        public IActionResult GetDashboard1([FromQuery] DateTime? hasta, [FromQuery] int? slpCode, [FromQuery] string? acctCode)
        {
            var fechaHasta = hasta ?? DateTime.Today;

            int? slpFinal;
            string rol;

            try
            {
                (rol, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var result = new Dashboard1Response();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            {
                conn.Open();

                using (var cmd = new SqlCommand("sp_Dashboard_VentasUltimos12", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Hasta", fechaHasta.Date);
                    cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);

                    // Barras SOLO Químicos por defecto
                    cmd.Parameters.AddWithValue("@AcctCode", string.IsNullOrWhiteSpace(acctCode) ? "40101001" : acctCode.Trim());

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Ventas12Meses.Add(new DashboardVentaMesDto
                            {
                                Anio = (int)reader["Anio"],
                                Mes = (int)reader["Mes"],
                                NombreMes = reader["NombreMes"].ToString() ?? "",
                                VentaNetaSN = reader["VentaNetaSN"] == DBNull.Value ? 0m : (decimal)reader["VentaNetaSN"]
                            });
                        }
                    }
                }

                using (var cmd = new SqlCommand("sp_Dashboard_TopClientesUltimos12", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@Hasta", fechaHasta.Date);
                    cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.TopClientes.Add(new DashboardTopClienteDto
                            {
                                CardCode = reader["CardCode"].ToString() ?? "",
                                CardName = reader["CardName"].ToString() ?? "",
                                Venta12Meses = reader["Venta12Meses"] == DBNull.Value ? 0m : (decimal)reader["Venta12Meses"],
                                VentaUlt3Meses = reader["VentaUlt3Meses"] == DBNull.Value ? 0m : (decimal)reader["VentaUlt3Meses"]
                            });
                        }
                    }
                }
            }

            result.Total12Meses = result.Ventas12Meses.Sum(v => v.VentaNetaSN);
            result.ClientesConMovimiento12Meses = result.TopClientes.Count(c => c.Venta12Meses > 0);

            result.TopActivos3Meses = result.TopClientes
                .Where(c => c.VentaUlt3Meses > 0)
                .OrderByDescending(c => c.VentaUlt3Meses)
                .Take(5)
                .ToList();

            result.TopSinCompras3Meses = result.TopClientes
                .Where(c => c.VentaUlt3Meses == 0 && c.Venta12Meses > 0)
                .OrderByDescending(c => c.Venta12Meses)
                .Take(5)
                .ToList();

            Console.WriteLine($"[dashboard1] rol={rol}, slpCode={slpFinal}, meses={result.Ventas12Meses.Count}, clientes={result.TopClientes.Count}");
            return Ok(result);
        }

        // ============================================================
        // GET: /api/ventas/periodo-acct
        // ============================================================
        [HttpGet("periodo-acct")]
        public IActionResult GetPeriodoAcct(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode,
            [FromQuery] string? cardCode,
            [FromQuery] string? acctCode)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            int? slpFinal;
            try
            {
                (_, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var ventas = new List<VentaPeriodoDto>();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("sp_VentasPeriodoVendedorCliente_AcctCode", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());
                cmd.Parameters.AddWithValue("@AcctCode", string.IsNullOrWhiteSpace(acctCode) ? (object)DBNull.Value : acctCode.Trim());

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ventas.Add(new VentaPeriodoDto
                        {
                            Anio = reader["Anio"] == DBNull.Value ? 0 : (int)reader["Anio"],
                            Mes = reader["Mes"] == DBNull.Value ? 0 : (int)reader["Mes"],
                            NombreMes = reader["NombreMes"] as string ?? string.Empty,
                            CodigoCliente = reader["CardCode"] as string ?? string.Empty,
                            NombreCliente = reader["CardName"] as string ?? string.Empty,
                            VentaNetaSN = reader["VentaNetaSN"] == DBNull.Value ? 0m : (decimal)reader["VentaNetaSN"],
                            CantFacturas = reader["CantFacturas"] == DBNull.Value ? 0 : (int)reader["CantFacturas"],
                            CantNC = reader["CantNC"] == DBNull.Value ? 0 : (int)reader["CantNC"]
                        });
                    }
                }
            }

            return Ok(ventas);
        }

        // ============================================================
        // GET: /api/ventas/periodo-excel
        // ============================================================
        [HttpGet("periodo-excel")]
        public IActionResult GetPeriodoExcel(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode,
            [FromQuery] string? cardCode)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            int? slpFinal;
            try
            {
                (_, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var tabla = new DataTable();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("sp_VentasPeriodoVendedorCliente", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());

                conn.Open();
                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(tabla);
                }
            }

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(tabla, "Ventas");

            var tbl = ws.Tables.FirstOrDefault();
            if (tbl != null)
            {
                tbl.ShowAutoFilter = true;
                tbl.HeadersRow().Style.Font.Bold = true;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var bytes = ms.ToArray();

            var fileName = $"ventas_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }

        // ============================================================
        // GET: /api/ventas/ranking-excel
        // ============================================================
        [HttpGet("ranking-excel")]
        public IActionResult GetRankingExcel(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            int? slpFinal;
            try
            {
                (_, slpFinal) = GetRolYSlpCodeForzado(slpCode);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var tabla = new DataTable();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("SP_Ranking_Ventas_Pivot", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpFinal ?? DBNull.Value);

                conn.Open();
                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(tabla);
                }
            }

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(tabla, "Ranking");
            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var bytes = ms.ToArray();

            var fileName = $"ranking_ventas_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }

        // ============================================================
        // GET: /api/ventas/sabana
        // ============================================================
        [HttpGet("sabana")]
        public async Task<IActionResult> GetSabana(
            [FromQuery] int mesReferencia,
            [FromQuery] int? anioReferencia = null,
            [FromQuery] string? cardCode = null)
        {
            try
            {
                if (mesReferencia < 1 || mesReferencia > 12)
                    return BadRequest("El mes de referencia debe estar entre 1 y 12.");

                if (string.IsNullOrWhiteSpace(cardCode))
                    cardCode = null;

                var rol = User.FindFirst(ClaimTypes.Role)?.Value
                          ?? User.FindFirst("role")?.Value
                          ?? string.Empty;

                var slpClaim = User.FindFirst("SlpCode")?.Value
                              ?? User.FindFirst("slpCode")?.Value;

                int? slpCode = null;

                if (rol.Equals("VENDEDOR", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(slpClaim) || !int.TryParse(slpClaim, out var slpParsed))
                        return Unauthorized("No se encontró SlpCode válido en el token.");

                    slpCode = slpParsed;
                }

                var tabla = new DataTable();
                using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
                using (var cmd = new SqlCommand("SP_sabana_ventas", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@MesReferencia", mesReferencia);
                    cmd.Parameters.AddWithValue("@AnioReferencia", (object?)anioReferencia ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SlpCode", (object?)slpCode ?? DBNull.Value);
                    //cmd.Parameters.AddWithValue("@CardCode", (object?)cardCode ?? DBNull.Value);

                    await conn.OpenAsync();
                    using (var da = new SqlDataAdapter(cmd))
                    {
                        da.Fill(tabla);
                    }
                }

                var lista = new List<Dictionary<string, object?>>();
                foreach (DataRow row in tabla.Rows)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (DataColumn col in tabla.Columns)
                        dict[col.ColumnName] = row[col] is DBNull ? null : row[col];

                    lista.Add(dict);
                }

                return Ok(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error en GetSabana: " + ex.Message);
            }
        }

        // ============================================================
        // GET: /api/ventas/sabana-excel
        // ============================================================
        [HttpGet("sabana-excel")]
        public async Task<IActionResult> GetSabanaExcel(
            [FromQuery] int mesReferencia,
            [FromQuery] int? anioReferencia = null,
            [FromQuery] string? cardCode = null)
        {
            try
            {
                if (mesReferencia < 1 || mesReferencia > 12)
                    return BadRequest("El mes de referencia debe estar entre 1 y 12.");

                anioReferencia ??= DateTime.Today.Year;

                var rol = User.FindFirst(ClaimTypes.Role)?.Value
                          ?? User.FindFirst("role")?.Value
                          ?? string.Empty;

                var slpClaim = User.FindFirst("SlpCode")?.Value
                              ?? User.FindFirst("slpCode")?.Value;

                int? slpCode = null;

                if (rol.Equals("VENDEDOR", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(slpClaim, out var slpFromToken))
                        return Forbid("Vendedor sin SlpCode asignado en el token.");

                    slpCode = slpFromToken;
                }

                var tabla = new DataTable();

                using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
                using (var cmd = new SqlCommand("SP_sabana_ventas", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@MesReferencia", mesReferencia);
                    cmd.Parameters.AddWithValue("@AnioReferencia", (object?)anioReferencia ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SlpCode", (object?)slpCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());

                    await conn.OpenAsync();
                    using (var da = new SqlDataAdapter(cmd))
                        da.Fill(tabla);
                    var nombresColumnas = tabla.Columns
                            .Cast<DataColumn>()
                            .Select(c => c.ColumnName)
                            .ToList();

                    return Ok(nombresColumnas);

                }

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("SabanaVentas");

                ws.Cell(1, 1).InsertTable(tabla, "TablaSabana", true);
                var tablaExcel = ws.Table("TablaSabana");
                tablaExcel.ShowAutoFilter = true;
                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents();

                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                var bytes = ms.ToArray();

                var fileName = $"sabana_ventas_{anioReferencia:0000}_{mesReferencia:00}.xlsx";

                return File(
                    bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName
                );
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error generando Excel de sábana: " + ex.Message);
            }
        }

        // ============================================================
        // GET: /api/ventas/estado-pedidos
        // ============================================================
        [HttpGet("estado-pedidos")]
        public async Task<IActionResult> GetEstadoPedidos(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] int? slpCode = null,
            [FromQuery] string? cardCode = null)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            var rol = User.FindFirst(ClaimTypes.Role)?.Value
                      ?? User.FindFirst("role")?.Value
                      ?? string.Empty;

            var slpClaim = User.FindFirst("SlpCode")?.Value
                          ?? User.FindFirst("slpCode")?.Value;

            if (rol.Equals("VENDEDOR", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(slpClaim) || !int.TryParse(slpClaim, out var slpFromToken))
                    return Unauthorized("No se encontró SlpCode válido en el token.");

                slpCode = slpFromToken;
            }

            var tabla = new DataTable();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("SP_estado_pedidos", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());

                await conn.OpenAsync();
                using (var da = new SqlDataAdapter(cmd))
                    da.Fill(tabla);
            }

            var lista = new List<Dictionary<string, object?>>();
            foreach (DataRow row in tabla.Rows)
            {
                var dict = new Dictionary<string, object?>();
                foreach (DataColumn col in tabla.Columns)
                    dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];

                lista.Add(dict);
            }

            return Ok(lista);
        }

        // ============================================================
        // GET: /api/ventas/estado-pedidos-excel
        // ============================================================
        [HttpGet("estado-pedidos-excel")]
        public async Task<IActionResult> GetEstadoPedidosExcel(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] string? cardCode = null)
        {
            if (desde > hasta)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            var rol = User.FindFirst(ClaimTypes.Role)?.Value
                      ?? User.FindFirst("role")?.Value
                      ?? string.Empty;

            var slpClaim = User.FindFirst("SlpCode")?.Value
                          ?? User.FindFirst("slpCode")?.Value;

            int? slpCode = null;

            if (rol.Equals("VENDEDOR", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(slpClaim, out var slpFromToken))
                    return Forbid("Vendedor sin SlpCode asignado en el token.");

                slpCode = slpFromToken;
            }

            var tabla = new DataTable();

            using (var conn = new SqlConnection(_config.GetConnectionString("SAP")))
            using (var cmd = new SqlCommand("SP_estado_pedidos", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                cmd.Parameters.AddWithValue("@SlpCode", (object?)slpCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CardCode", string.IsNullOrWhiteSpace(cardCode) ? (object)DBNull.Value : cardCode.Trim());

                await conn.OpenAsync();
                using var da = new SqlDataAdapter(cmd);
                da.Fill(tabla);
            }

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(tabla, "EstadoPedidos");
            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var bytes = ms.ToArray();

            var fileName = $"estado_pedidos_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.xlsx";

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }
    }

    // ============================================================
    // Controller subtabla Químicos: /api/ventas/quimicos/...
    // ============================================================
    [ApiController]
    [Route("api/ventas/quimicos")]
    [Authorize]
    public class VentasQuimicosController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        public VentasQuimicosController(IConfiguration cfg) => _cfg = cfg;

        // GET: /api/ventas/quimicos/lineas?desde=...&hasta=...&cardCode=...
        [HttpGet("lineas")]
        public async Task<IActionResult> Lineas(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] string cardCode)
        {
            int? slpCode = null;
            var claim = User.FindFirst("SlpCode")?.Value ?? User.FindFirst("slpCode")?.Value;
            if (int.TryParse(claim, out var s)) slpCode = s;

            using var cn = new SqlConnection(_cfg.GetConnectionString("SAP"));

            var rows = (await cn.QueryAsync(
                "dbo.sp_VentasPeriodoVendedorCliente_QuimicosLineas",
                new { Desde = desde.Date, Hasta = hasta.Date, SlpCode = slpCode, CardCode = cardCode },
                commandType: CommandType.StoredProcedure
            )).ToList();

            return Ok(new { ok = true, rows });
        }
    }

       




}
