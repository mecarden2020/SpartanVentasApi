using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Dapper;

namespace SpartanVentasApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        public DashboardController(IConfiguration cfg) => _cfg = cfg;

        // =========================
        // Helpers Claims
        // =========================
        private int? GetSlpCodeFromToken()
        {
            var slpClaim = User.FindFirst("SlpCode")?.Value ?? User.FindFirst("slpCode")?.Value;
            if (int.TryParse(slpClaim, out var s)) return s;
            return null;
        }

        private string GetRoleFromToken()
        {
            return
                User.FindFirst("role")?.Value ??
                User.FindFirst(ClaimTypes.Role)?.Value ??
                string.Empty;
        }

        private SqlConnection OpenSap()
        {
            var cn = new SqlConnection(_cfg.GetConnectionString("SAP"));
            cn.Open();
            return cn;
        }

        private static decimal Nz(object? v) => v == null || v == DBNull.Value ? 0m : Convert.ToDecimal(v);

        // =========================================================================================
        // 1) RESUMEN ACCT + META QUIMICOS (SP: sp_Dashboard_VentasAcct_Resumen_y_MetaQuimicos)
        // GET: /api/dashboard/ventas-acct-resumen?desde=2026-02-01&hasta=2026-02-29
        // =========================================================================================
        [HttpGet("ventas-acct-resumen")]
        public async Task<IActionResult> VentasAcctResumen([FromQuery] DateTime desde, [FromQuery] DateTime hasta)
        {
            if (desde.Date > hasta.Date)
                return BadRequest("La fecha 'desde' no puede ser mayor que 'hasta'.");

            var slpCode = GetSlpCodeFromToken();
            if (slpCode == null)
                return Forbid("No se encontró SlpCode en el token.");

            using var cn = new SqlConnection(_cfg.GetConnectionString("SAP"));

            using var multi = await cn.QueryMultipleAsync(
                "dbo.sp_Dashboard_VentasAcct_Resumen_y_MetaQuimicos",
                new { Desde = desde.Date, Hasta = hasta.Date, SlpCode = slpCode },
                commandType: CommandType.StoredProcedure
            );

            var data = (await multi.ReadAsync()).Select(x => new
            {
                acctCode = (string)x.acctCode,
                label = (string)x.label,
                neto = (decimal)x.neto
            }).ToList();

            var metaRow = await multi.ReadFirstOrDefaultAsync();
            decimal metaQuimicos = 0m;
            if (metaRow != null)
                metaQuimicos = (decimal)metaRow.metaQuimicos;

            decimal q = data.FirstOrDefault(d => d.acctCode == "40101001")?.neto ?? 0m;
            decimal cumplido = Math.Min(q, metaQuimicos);
            decimal faltante = Math.Max(metaQuimicos - q, 0m);
            decimal pctCumplido = metaQuimicos > 0 ? Math.Round((q / metaQuimicos) * 100m, 2) : 0m;

            return Ok(new
            {
                ok = true,
                desde = desde.Date,
                hasta = hasta.Date,
                data,
                metaQuimicos,
                quimicos = new
                {
                    neto = q,
                    cumplido,
                    faltante,
                    pctCumplido
                }
            });
        }

        // =========================================================================================
        // 2) RESUMEN DONAS (Químicos meta vs vendido + Distribución semestral Maq/Acc)
        // GET: /api/dashboard/resumen-donas?anio=2026&mes=2&cardCode=C1234
        // =========================================================================================
        [HttpGet("resumen-donas")]
        public IActionResult ResumenDonas([FromQuery] int anio, [FromQuery] int mes, [FromQuery] string? cardCode = null)
        {
            if (anio < 2000 || anio > 2100) return BadRequest("Año inválido");
            if (mes < 1 || mes > 12) return BadRequest("Mes inválido");

            var slpCode = GetSlpCodeFromToken();
            if (slpCode == null) return Unauthorized("Token sin SlpCode válido");

            using var conn = OpenSap();

            var quimicos = GetQuimicosMetaVsVendido(conn, slpCode.Value, anio, mes, cardCode);
            var maquinas = GetDistribucionSemestral(conn, slpCode.Value, anio, mes, "MAQUINAS", cardCode);
            var accesorios = GetDistribucionSemestral(conn, slpCode.Value, anio, mes, "ACCESORIOS", cardCode);

            return Ok(new
            {
                slpCode,
                anio,
                mes,
                cardCode = string.IsNullOrWhiteSpace(cardCode) ? null : cardCode,
                quimicos,
                maquinas,
                accesorios
            });
        }

        // =========================================================================================
        // 3) YOY MAQUINAS / ACCESORIOS (SP: SP_Dashboard_YOY_MaqAcc)
        // GET: /api/dashboard/yoy-maq-acc?anio=2026&mes=2
        // =========================================================================================
        [HttpGet("yoy-maq-acc")]
        public async Task<IActionResult> GetYoyMaqAcc([FromQuery] int anio, [FromQuery] int mes)
        {
            if (anio < 2000 || anio > 2100) return BadRequest("Año inválido");
            if (mes < 1 || mes > 12) return BadRequest("Mes inválido");

            var slpCode = GetSlpCodeFromToken();
            if (slpCode == null) return Unauthorized("Token sin SlpCode válido");

            decimal maqActual = 0, maqAnterior = 0;
            decimal accActual = 0, accAnterior = 0;

            using var conn = new SqlConnection(_cfg.GetConnectionString("SAP"));
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.SP_Dashboard_YOY_MaqAcc", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@Anio", anio);
            cmd.Parameters.AddWithValue("@Mes", mes);
            cmd.Parameters.AddWithValue("@SlpCode", slpCode.Value);

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var categoria = reader["Categoria"]?.ToString() ?? "";
                    var actual = reader["MesActualSN"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["MesActualSN"]);
                    var anterior = reader["MesAnteriorSN"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["MesAnteriorSN"]);

                    if (categoria.Equals("MAQUINAS", StringComparison.OrdinalIgnoreCase))
                    {
                        maqActual = actual;
                        maqAnterior = anterior;
                    }
                    else if (categoria.Equals("ACCESORIOS", StringComparison.OrdinalIgnoreCase))
                    {
                        accActual = actual;
                        accAnterior = anterior;
                    }
                }
            }

            decimal VarPct(decimal actual, decimal anterior)
            {
                if (anterior <= 0) return actual > 0 ? 100m : 0m;
                return Math.Round(((actual - anterior) / anterior) * 100m, 0);
            }

            return Ok(new
            {
                anio,
                mes,
                maquinas = new { mesActualSN = maqActual, mesAnteriorSN = maqAnterior, variacionPct = VarPct(maqActual, maqAnterior) },
                accesorios = new { mesActualSN = accActual, mesAnteriorSN = accAnterior, variacionPct = VarPct(accActual, accAnterior) }
            });
        }

        // =========================================================================================
        // 4) RESUMEN MINI (1 query) - corrige typos y devuelve JSON listo
        // GET: /api/dashboard/resumen-mini?anio=2026&mes=2&cardCode=C123
        // =========================================================================================
        [HttpGet("resumen-mini")]
        public IActionResult ResumenMini([FromQuery] int anio, [FromQuery] int mes, [FromQuery] string? cardCode = null)
        {
            if (anio < 2000 || anio > 2100) return BadRequest("Año inválido");
            if (mes < 1 || mes > 12) return BadRequest("Mes inválido");

            var slpCode = GetSlpCodeFromToken();
            if (slpCode == null) return Unauthorized("Token sin SlpCode válido");

            var fin = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
            var iniTmp = fin.AddMonths(-5);
            var inicioSem = new DateTime(iniTmp.Year, iniTmp.Month, 1);

            using var conn = OpenSap();

            using var cmd = new SqlCommand(@"
DECLARE @SlpCode INT = @pSlpCode;
DECLARE @Anio INT = @pAnio;
DECLARE @Mes  INT = @pMes;
DECLARE @CardCode NVARCHAR(50) = @pCardCode;
DECLARE @InicioSem DATE = @pInicioSem;
DECLARE @FinSem DATE = @pFinSem;

WITH MetaQuimicos AS (
    SELECT
        SlpCode,
        CAST(CASE @Mes
            WHEN 1  THEN ISNULL(U_METAS_Ene, 0)
            WHEN 2  THEN ISNULL(U_METAS_Feb, 0)
            WHEN 3  THEN ISNULL(U_METAS_Mar, 0)
            WHEN 4  THEN ISNULL(U_METAS_Abr, 0)
            WHEN 5  THEN ISNULL(U_METAS_May, 0)
            WHEN 6  THEN ISNULL(U_METAS_Jun, 0)
            WHEN 7  THEN ISNULL(U_METAS_Jul, 0)
            WHEN 8  THEN ISNULL(U_METAS_Ago, 0)
            WHEN 9  THEN ISNULL(U_METAS_Sep, 0)
            WHEN 10 THEN ISNULL(U_METAS_Oct, 0)
            WHEN 11 THEN ISNULL(U_METAS_Nov, 0)
            WHEN 12 THEN ISNULL(U_METAS_Dic, 0)
        END AS DECIMAL(19,2)) AS MetaSN
    FROM OSLP
    WHERE SlpCode = @SlpCode
),
VentasMes AS (
    SELECT Categoria, SUM(MontoSN) AS VendidoSN
    FROM (
        -- FE
        SELECT cat.Categoria,
               SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
        FROM OINV H
        JOIN INV1 L1 ON L1.DocEntry = H.DocEntry
        JOIN OITM I ON I.ItemCode = L1.ItemCode
        JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
        WHERE H.CANCELED='N'
          AND H.SlpCode=@SlpCode
          AND YEAR(H.DocDate)=@Anio AND MONTH(H.DocDate)=@Mes
          AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)
        GROUP BY cat.Categoria

        UNION ALL

        -- NC resta
        SELECT cat.Categoria,
               -SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
        FROM ORIN H
        JOIN RIN1 L1 ON L1.DocEntry = H.DocEntry
        JOIN OITM I ON I.ItemCode = L1.ItemCode
        JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
        WHERE H.CANCELED='N'
          AND H.SlpCode=@SlpCode
          AND YEAR(H.DocDate)=@Anio AND MONTH(H.DocDate)=@Mes
          AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)
        GROUP BY cat.Categoria
    ) X
    GROUP BY Categoria
),
VentasSemestre AS (
    SELECT Categoria, SUM(MontoSN) AS VendidoSN
    FROM (
        -- FE
        SELECT cat.Categoria,
               SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
        FROM OINV H
        JOIN INV1 L1 ON L1.DocEntry = H.DocEntry
        JOIN OITM I ON I.ItemCode = L1.ItemCode
        JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
        WHERE H.CANCELED='N'
          AND H.SlpCode=@SlpCode
          AND H.DocDate BETWEEN @InicioSem AND @FinSem
          AND cat.Categoria IN ('MAQUINAS','ACCESORIOS')
          AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)
        GROUP BY cat.Categoria

        UNION ALL

        -- NC resta
        SELECT cat.Categoria,
               -SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
        FROM ORIN H
        JOIN RIN1 L1 ON L1.DocEntry = H.DocEntry
        JOIN OITM I ON I.ItemCode = L1.ItemCode
        JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
        WHERE H.CANCELED='N'
          AND H.SlpCode=@SlpCode
          AND H.DocDate BETWEEN @InicioSem AND @FinSem
          AND cat.Categoria IN ('MAQUINAS','ACCESORIOS')
          AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)
        GROUP BY cat.Categoria
    ) X
    GROUP BY Categoria
)
SELECT
    ISNULL((SELECT MetaSN FROM MetaQuimicos),0) AS MetaQuimicosSN,
    ISNULL((SELECT VendidoSN FROM VentasMes WHERE Categoria='QUIMICOS'),0) AS VendidoQuimicosSN,
    ISNULL((SELECT VendidoSN FROM VentasSemestre WHERE Categoria='MAQUINAS'),0) AS VendidoMaquinasSemSN,
    ISNULL((SELECT VendidoSN FROM VentasSemestre WHERE Categoria='ACCESORIOS'),0) AS VendidoAccesoriosSemSN;
", conn);

            cmd.Parameters.AddWithValue("@pSlpCode", slpCode.Value);
            cmd.Parameters.AddWithValue("@pAnio", anio);
            cmd.Parameters.AddWithValue("@pMes", mes);
            cmd.Parameters.AddWithValue("@pCardCode", (object?)cardCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pInicioSem", inicioSem);
            cmd.Parameters.AddWithValue("@pFinSem", fin);

            decimal metaQ = 0, vendidoQ = 0, maqSem = 0, accSem = 0;

            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    metaQ = Nz(r["MetaQuimicosSN"]);
                    vendidoQ = Nz(r["VendidoQuimicosSN"]);
                    maqSem = Nz(r["VendidoMaquinasSemSN"]);
                    accSem = Nz(r["VendidoAccesoriosSemSN"]);
                }
            }

            var pendienteQ = metaQ - vendidoQ;
            if (pendienteQ < 0) pendienteQ = 0;

            return Ok(new
            {
                quimicos = new
                {
                    labels = new[] { "Vendido", "Pendiente" },
                    values = new[] { vendidoQ, pendienteQ },
                    metaSN = metaQ,
                    vendidoSN = vendidoQ,
                    pct = metaQ == 0 ? 0 : Math.Round((double)(vendidoQ / metaQ * 100m), 2)
                },
                maquinas = new { labels = new[] { "Semestre" }, values = new[] { maqSem } },
                accesorios = new { labels = new[] { "Semestre" }, values = new[] { accSem } }
            });
        }

        // =========================================================================================
        // 5) CIERRE SEMANAL (usa ApiCalendarioCierreSemanal + SP_Dashboard_CierreSemanal_Vendedor)
        // GET: /api/dashboard/cierre-semanal?anio=2026&mes=2&semana=2
        // =========================================================================================
        [HttpGet("cierre-semanal")]
        public async Task<IActionResult> CierreSemanal([FromQuery] int? anio = null, [FromQuery] int? mes = null, [FromQuery] int? semana = null)
        {
            var slpCode = GetSlpCodeFromToken();
            if (slpCode == null) return Unauthorized("Token sin SlpCode válido");

            using var conn = new SqlConnection(_cfg.GetConnectionString("SAP"));
            await conn.OpenAsync();

            var hoy = DateTime.Today;
            var baseMes = new DateTime(hoy.Year, hoy.Month, 1);
            int y = anio ?? baseMes.Year;
            int m = mes ?? baseMes.Month;

            DateTime fechaCierre;
            int visibleDias;

            if (semana.HasValue)
            {
                using var cmd = new SqlCommand(@"
SELECT FechaCierre, ISNULL(VisibleDias,3) AS VisibleDias
FROM dbo.ApiCalendarioCierreSemanal
WHERE Anio=@Anio AND Mes=@Mes AND Semana=@Semana;
", conn);

                cmd.Parameters.AddWithValue("@Anio", y);
                cmd.Parameters.AddWithValue("@Mes", m);
                cmd.Parameters.AddWithValue("@Semana", semana.Value);

                using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return Ok(new { isVisible = false, reason = "No existe semana en calendario" });

                fechaCierre = Convert.ToDateTime(r["FechaCierre"]);
                visibleDias = Convert.ToInt32(r["VisibleDias"]);
            }
            else
            {
                using var cmd = new SqlCommand(@"
SELECT TOP 1 Semana, FechaCierre, ISNULL(VisibleDias,3) AS VisibleDias
FROM dbo.ApiCalendarioCierreSemanal
WHERE Anio=@Anio AND Mes=@Mes AND FechaCierre <= @Hoy
ORDER BY FechaCierre DESC;
", conn);

                cmd.Parameters.AddWithValue("@Anio", y);
                cmd.Parameters.AddWithValue("@Mes", m);
                cmd.Parameters.AddWithValue("@Hoy", hoy);

                using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return Ok(new { isVisible = false, reason = "Aún no hay cierre semanal vigente" });

                fechaCierre = Convert.ToDateTime(r["FechaCierre"]);
                visibleDias = Convert.ToInt32(r["VisibleDias"]);
            }

            var desde = GetPrimerDiaHabilMes(y, m);
            var hasta = fechaCierre.Date;
            var visibleHasta = AddBusinessDays(fechaCierre.Date, visibleDias);

            if (hoy > visibleHasta)
                return Ok(new { isVisible = false, reason = "Ventana vencida", visibleHasta = visibleHasta.ToString("yyyy-MM-dd") });

            var tot = await GetTotalesCierreSemanalSp(conn, slpCode.Value, desde, hasta);

            return Ok(new
            {
                isVisible = true,
                label = "Cierre Semana",
                desde = desde.ToString("yyyy-MM-dd"),
                hasta = hasta.ToString("yyyy-MM-dd"),
                visibleHasta = visibleHasta.ToString("yyyy-MM-dd"),
                totales = tot
            });
        }

        private static DateTime GetPrimerDiaHabilMes(int anio, int mes)
        {
            var d = new DateTime(anio, mes, 1);
            while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                d = d.AddDays(1);
            return d.Date;
        }

        private static DateTime AddBusinessDays(DateTime start, int businessDays)
        {
            var d = start.Date;
            int count = 0;
            while (count < businessDays)
            {
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                    count++;
                if (count < businessDays) d = d.AddDays(1);
            }
            return d.Date;
        }

        private async Task<object> GetTotalesCierreSemanalSp(SqlConnection conn, int slpCode, DateTime desde, DateTime hasta)
        {
            using var cmd = new SqlCommand("dbo.SP_Dashboard_CierreSemanal_Vendedor", conn);
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue("@SlpCode", slpCode);
            cmd.Parameters.AddWithValue("@Desde", desde.Date);
            cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
            cmd.Parameters.AddWithValue("@AcctCode", "40101001"); // Químicos

            decimal factura = 0, pedido = 0, entrega = 0, total = 0;

            using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                factura = r["Factura"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Factura"]);
                pedido = r["Pedido"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Pedido"]);
                entrega = r["Entrega"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Entrega"]);
                total = r["Total"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Total"]);
            }

            return new { factura, pedido, entrega, total };
        }

        // =========================================================================================
        // 6) CIERRE MENSUAL (SP: SP_Dashboard_CierreMensual_Vendedor)
        // GET: /api/dashboard/cierre-mensual?anio=2026&mes=1   (por defecto mes anterior)
        // =========================================================================================
        [HttpGet("cierre-mensual")]
        public async Task<IActionResult> GetCierreMensual([FromQuery] int? anio = null, [FromQuery] int? mes = null)
        {
            var slpCode = GetSlpCodeFromToken();
            if (slpCode == null) return Unauthorized(new { error = "Token sin SlpCode válido" });

            var hoy = DateTime.Today;
            var baseMes = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-1);
            int y = anio ?? baseMes.Year;
            int m = mes ?? baseMes.Month;

            if (y < 2000 || y > 2100) return BadRequest(new { error = "Año inválido" });
            if (m < 1 || m > 12) return BadRequest(new { error = "Mes inválido" });

            try
            {
                using var conn = new SqlConnection(_cfg.GetConnectionString("SAP"));
                await conn.OpenAsync();

                using var cmd = new SqlCommand("dbo.SP_Dashboard_CierreMensual_Vendedor", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@SlpCode", slpCode.Value);
                cmd.Parameters.AddWithValue("@Anio", y);
                cmd.Parameters.AddWithValue("@Mes", m);

                var detalle = new List<object>();
                decimal total = 0;

                using (var r = await cmd.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        var categoria = (r["Categoria"]?.ToString() ?? "").Trim();
                        var monto = r["Monto"] == DBNull.Value ? 0m : Convert.ToDecimal(r["Monto"]);
                        detalle.Add(new { categoria, monto });
                        total += monto;
                    }
                }

                var desde = new DateTime(y, m, 1);
                var hasta = new DateTime(y, m, DateTime.DaysInMonth(y, m));

                return Ok(new
                {
                    slpCode,
                    anio = y,
                    mes = m,
                    label = "Cierre Mensual",
                    desde = desde.ToString("yyyy-MM-dd"),
                    hasta = hasta.ToString("yyyy-MM-dd"),
                    total,
                    detalle
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "SQL", message = ex.Message, number = ex.Number });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "APP", message = ex.Message });
            }
        }

        // =========================================================================================
        // Internos: QUIMICOS meta vs vendido (mes)
        // =========================================================================================
        private object GetQuimicosMetaVsVendido(SqlConnection conn, int slpCode, int anio, int mes, string? cardCode)
        {
            using var cmd = new SqlCommand(@"
DECLARE @SlpCode INT = @pSlpCode;
DECLARE @Anio INT = @pAnio;
DECLARE @Mes  INT = @pMes;
DECLARE @CardCode NVARCHAR(50) = @pCardCode;

WITH Meta AS (
    SELECT
        CAST(CASE @Mes
            WHEN 1  THEN ISNULL(U_METAS_Ene, 0)
            WHEN 2  THEN ISNULL(U_METAS_Feb, 0)
            WHEN 3  THEN ISNULL(U_METAS_Mar, 0)
            WHEN 4  THEN ISNULL(U_METAS_Abr, 0)
            WHEN 5  THEN ISNULL(U_METAS_May, 0)
            WHEN 6  THEN ISNULL(U_METAS_Jun, 0)
            WHEN 7  THEN ISNULL(U_METAS_Jul, 0)
            WHEN 8  THEN ISNULL(U_METAS_Ago, 0)
            WHEN 9  THEN ISNULL(U_METAS_Sep, 0)
            WHEN 10 THEN ISNULL(U_METAS_Oct, 0)
            WHEN 11 THEN ISNULL(U_METAS_Nov, 0)
            WHEN 12 THEN ISNULL(U_METAS_Dic, 0)
        END AS DECIMAL(19,2)) AS MetaSN
    FROM OSLP
    WHERE SlpCode = @SlpCode
),
Vendido AS (
    SELECT SUM(MontoSN) AS VendidoSN
    FROM (
        -- FE
        SELECT SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
        FROM OINV H
        JOIN INV1 L1 ON L1.DocEntry = H.DocEntry
        JOIN OITM I ON I.ItemCode = L1.ItemCode
        JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
        WHERE H.CANCELED='N'
          AND H.SlpCode=@SlpCode
          AND YEAR(H.DocDate)=@Anio AND MONTH(H.DocDate)=@Mes
          AND cat.Categoria='QUIMICOS'
          AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)

        UNION ALL

        -- NC resta
        SELECT -SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
        FROM ORIN H
        JOIN RIN1 L1 ON L1.DocEntry = H.DocEntry
        JOIN OITM I ON I.ItemCode = L1.ItemCode
        JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
        WHERE H.CANCELED='N'
          AND H.SlpCode=@SlpCode
          AND YEAR(H.DocDate)=@Anio AND MONTH(H.DocDate)=@Mes
          AND cat.Categoria='QUIMICOS'
          AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)
    ) X
)
SELECT (SELECT MetaSN FROM Meta) AS MetaSN,
       ISNULL((SELECT VendidoSN FROM Vendido),0) AS VendidoSN;
", conn);

            cmd.Parameters.AddWithValue("@pSlpCode", slpCode);
            cmd.Parameters.AddWithValue("@pAnio", anio);
            cmd.Parameters.AddWithValue("@pMes", mes);
            cmd.Parameters.AddWithValue("@pCardCode", (object?)cardCode ?? DBNull.Value);

            decimal meta = 0, vendido = 0;
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    meta = Nz(r["MetaSN"]);
                    vendido = Nz(r["VendidoSN"]);
                }
            }

            var pendiente = meta - vendido;
            if (pendiente < 0) pendiente = 0;

            return new
            {
                tipo = "donut_meta_vs_vendido",
                labels = new[] { "Vendido", "Pendiente" },
                values = new[] { vendido, pendiente },
                metaSN = meta,
                vendidoSN = vendido,
                cumplimientoPct = meta == 0 ? 0 : Math.Round((double)(vendido / meta * 100m), 2)
            };
        }

        // =========================================================================================
        // Internos: Distribución semestral Maq/Acc (últimos 6 meses)
        // =========================================================================================
        private object GetDistribucionSemestral(SqlConnection conn, int slpCode, int anio, int mes, string categoria, string? cardCode)
        {
            var fin = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
            var inicio6 = fin.AddMonths(-5);
            var inicio = new DateTime(inicio6.Year, inicio6.Month, 1);

            using var cmd = new SqlCommand(@"
DECLARE @SlpCode INT = @pSlpCode;
DECLARE @Inicio DATE = @pInicio;
DECLARE @Fin    DATE = @pFin;
DECLARE @Categoria NVARCHAR(30) = @pCategoria;
DECLARE @CardCode NVARCHAR(50) = @pCardCode;

;WITH Ventas AS (
    -- FE
    SELECT YEAR(H.DocDate) AS Anio, MONTH(H.DocDate) AS Mes,
           SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
    FROM OINV H
    JOIN INV1 L1 ON L1.DocEntry = H.DocEntry
    JOIN OITM I ON I.ItemCode = L1.ItemCode
    JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
    WHERE H.CANCELED='N'
      AND H.SlpCode=@SlpCode
      AND H.DocDate BETWEEN @Inicio AND @Fin
      AND cat.Categoria=@Categoria
      AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)
    GROUP BY YEAR(H.DocDate), MONTH(H.DocDate)

    UNION ALL

    -- NC resta
    SELECT YEAR(H.DocDate) AS Anio, MONTH(H.DocDate) AS Mes,
           -SUM(L1.Price * L1.Quantity * (1 - (ISNULL(L1.DiscPrcnt,0)/100.0))) AS MontoSN
    FROM ORIN H
    JOIN RIN1 L1 ON L1.DocEntry = H.DocEntry
    JOIN OITM I ON I.ItemCode = L1.ItemCode
    JOIN dbo.ApiCategoriaItemGroup cat ON cat.ItmsGrpCod = I.ItmsGrpCod
    WHERE H.CANCELED='N'
      AND H.SlpCode=@SlpCode
      AND H.DocDate BETWEEN @Inicio AND @Fin
      AND cat.Categoria=@Categoria
      AND (@CardCode IS NULL OR @CardCode='' OR H.CardCode=@CardCode)
    GROUP BY YEAR(H.DocDate), MONTH(H.DocDate)
)
SELECT Anio, Mes,
       DATENAME(MONTH, DATEFROMPARTS(Anio, Mes, 1)) AS NombreMes,
       SUM(MontoSN) AS VendidoSN
FROM Ventas
GROUP BY Anio, Mes
ORDER BY Anio, Mes;
", conn);

            cmd.Parameters.AddWithValue("@pSlpCode", slpCode);
            cmd.Parameters.AddWithValue("@pInicio", inicio);
            cmd.Parameters.AddWithValue("@pFin", fin);
            cmd.Parameters.AddWithValue("@pCategoria", categoria);
            cmd.Parameters.AddWithValue("@pCardCode", (object?)cardCode ?? DBNull.Value);

            var labels = new List<string>();
            var values = new List<decimal>();

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    labels.Add($"{r["NombreMes"]} {r["Anio"]}");
                    values.Add(Nz(r["VendidoSN"]));
                }
            }

            return new
            {
                tipo = "donut_distribucion_semestral",
                categoria,
                desde = inicio.ToString("yyyy-MM-dd"),
                hasta = fin.ToString("yyyy-MM-dd"),
                labels,
                values
            };
        }
    }
}
