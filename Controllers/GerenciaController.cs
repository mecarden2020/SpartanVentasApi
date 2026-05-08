using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;
using Dapper;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace SpartanVentasApi.Controllers
{
    [ApiController]
    [Route("api/gerencia")]
    [Authorize] // Base: requiere JWT. Roles se aplican por endpoint si corresponde.
    public class GerenciaController : ControllerBase
    {
        private readonly IConfiguration _config;

        public GerenciaController(IConfiguration config)
        {
            _config = config;
        }


        /*private readonly IConfiguration _cfg;

        public GerenciaController(IConfiguration cfg) => _cfg = cfg;   */


        // ----------------- Cumplimiento Vendedores -------------------------------------------
        [HttpGet("cumplimiento-vendedor")]
        [Authorize(Roles = "GERENCIA,ADMIN")]
        public async Task<IActionResult> GetCumplimientoVendedor([FromQuery] int anio, [FromQuery] int? slpCode)
        {
            if (anio < 2000 || anio > 2100)
                return BadRequest("Parámetro 'anio' inválido.");

            int slp = slpCode ?? 209;

            const string sql = @"
;WITH X AS (
    /* =========================
       BASE (FACTURAS / NC / PEDIDOS / ENTREGAS)
       ========================= */

    /* FACTURAS */
    SELECT
        YEAR(H.DocDate) AS Anio,
        MONTH(H.DocDate) AS MesNum,
        DATENAME(MONTH, H.DocDate) AS Mes,
        S.U_ZONA   AS Zona,
        S.U_GRUPO  AS Gerencia,
        S.U_GRUPO2 AS Supervisor,
        S.SlpName  AS Vendedor,
        S.U_Metas_Feb AS MetaMes,
        SUM(L.LineTotal * (1 - H.DiscPrcnt/100.0)) AS Fe,
        CAST(0 AS DECIMAL(19,2)) AS Nc,
        CAST(0 AS DECIMAL(19,2)) AS Fe1,
        CAST(0 AS DECIMAL(19,2)) AS Nc1,
        CAST(0 AS DECIMAL(19,2)) AS Fe2,
        CAST(0 AS DECIMAL(19,2)) AS Nc2
    FROM OINV H
    INNER JOIN INV1 L ON H.DocEntry = L.DocEntry
    INNER JOIN OSLP S ON H.SlpCode  = S.SlpCode
    WHERE H.SlpCode = @SlpCode
      AND YEAR(H.DocDate) IN (@Anio, @Anio - 1)
      AND H.Series IN (9,41,42,43,47)
      AND L.AcctCode = '40101001'
    GROUP BY YEAR(H.DocDate), MONTH(H.DocDate), DATENAME(MONTH,H.DocDate),
             S.U_ZONA, S.U_GRUPO, S.U_GRUPO2, S.SlpName, S.U_Metas_Feb

    UNION ALL

    /* NOTAS DE CRÉDITO */
    SELECT
        YEAR(H.DocDate) AS Anio,
        MONTH(H.DocDate) AS MesNum,
        DATENAME(MONTH,H.DocDate) AS Mes,
        S.U_ZONA,
        S.U_GRUPO,
        S.U_GRUPO2,
        S.SlpName,
        S.U_Metas_Feb,
        CAST(0 AS DECIMAL(19,2)) AS Fe,
        SUM(L.LineTotal * (1 - H.DiscPrcnt/100.0)) AS Nc,
        CAST(0 AS DECIMAL(19,2)) AS Fe1,
        CAST(0 AS DECIMAL(19,2)) AS Nc1,
        CAST(0 AS DECIMAL(19,2)) AS Fe2,
        CAST(0 AS DECIMAL(19,2)) AS Nc2
    FROM ORIN H
    INNER JOIN RIN1 L ON H.DocEntry = L.DocEntry
    INNER JOIN OSLP S ON H.SlpCode  = S.SlpCode
    WHERE H.SlpCode = @SlpCode
      AND YEAR(H.DocDate) IN (@Anio, @Anio - 1)
      AND L.AcctCode = '40101001'
    GROUP BY YEAR(H.DocDate), MONTH(H.DocDate), DATENAME(MONTH,H.DocDate),
             S.U_ZONA, S.U_GRUPO, S.U_GRUPO2, S.SlpName, S.U_Metas_Feb

    UNION ALL

    /* PEDIDOS */
    SELECT
        YEAR(H.DocDate) AS Anio,
        MONTH(H.DocDate) AS MesNum,
        DATENAME(MONTH,H.DocDate) AS Mes,
        S.U_ZONA,
        S.U_GRUPO,
        S.U_GRUPO2,
        S.SlpName,
        S.U_Metas_Feb,
        CAST(0 AS DECIMAL(19,2)) AS Fe,
        CAST(0 AS DECIMAL(19,2)) AS Nc,
        SUM(L.LineTotal * (1 - H.DiscPrcnt/100.0)) AS Fe1,
        CAST(0 AS DECIMAL(19,2)) AS Nc1,
        CAST(0 AS DECIMAL(19,2)) AS Fe2,
        CAST(0 AS DECIMAL(19,2)) AS Nc2
    FROM ORDR H
    INNER JOIN RDR1 L ON H.DocEntry = L.DocEntry
    INNER JOIN OSLP S ON H.SlpCode  = S.SlpCode
    WHERE H.SlpCode = @SlpCode
      AND YEAR(H.DocDate) IN (@Anio, @Anio - 1)
      AND H.VatPaidSys = '0'
      AND H.DocStatus <> 'C'
      AND L.AcctCode = '40101001'
    GROUP BY YEAR(H.DocDate), MONTH(H.DocDate), DATENAME(MONTH,H.DocDate),
             S.U_ZONA, S.U_GRUPO, S.U_GRUPO2, S.SlpName, S.U_Metas_Feb

    UNION ALL

    /* ENTREGAS */
    SELECT
        YEAR(H.DocDate) AS Anio,
        MONTH(H.DocDate) AS MesNum,
        DATENAME(MONTH,H.DocDate) AS Mes,
        S.U_ZONA,
        S.U_GRUPO,
        S.U_GRUPO2,
        S.SlpName,
        S.U_Metas_Feb,
        CAST(0 AS DECIMAL(19,2)) AS Fe,
        CAST(0 AS DECIMAL(19,2)) AS Nc,
        CAST(0 AS DECIMAL(19,2)) AS Fe1,
        CAST(0 AS DECIMAL(19,2)) AS Nc1,
        SUM(L.LineTotal * (1 - H.DiscPrcnt/100.0)) AS Fe2,
        CAST(0 AS DECIMAL(19,2)) AS Nc2
    FROM ODLN H
    INNER JOIN DLN1 L ON H.DocEntry = L.DocEntry
    INNER JOIN OSLP S ON H.SlpCode  = S.SlpCode
    WHERE H.SlpCode = @SlpCode
      AND YEAR(H.DocDate) IN (@Anio, @Anio - 1)
      AND H.VatPaidSys = '0'
      AND H.DocStatus = 'O'
      AND H.InvntSttus = 'O'
      AND L.AcctCode = '40101001'
    GROUP BY YEAR(H.DocDate), MONTH(H.DocDate), DATENAME(MONTH,H.DocDate),
             S.U_ZONA, S.U_GRUPO, S.U_GRUPO2, S.SlpName, S.U_Metas_Feb
),
R AS (
    SELECT
        Anio, MesNum, Mes, Zona, Gerencia, Supervisor, Vendedor,
        MAX(MetaMes) AS MetaMes,
        SUM(Fe)-SUM(Nc)   AS Facturacion,
        SUM(Fe1)-SUM(Nc1) AS Pedidos,
        SUM(Fe2)-SUM(Nc2) AS Entregas,
        (SUM(Fe)-SUM(Nc))+(SUM(Fe1)-SUM(Nc1))+(SUM(Fe2)-SUM(Nc2)) AS TotalNeto
    FROM X
    GROUP BY Anio, MesNum, Mes, Zona, Gerencia, Supervisor, Vendedor
),
CUR AS (
    SELECT * FROM R WHERE Anio = @Anio
),
PREV AS (
    SELECT * FROM R WHERE Anio = @Anio - 1
)
SELECT
    c.Anio,
    c.MesNum,
    c.Mes,
    c.Zona,
    c.Gerencia,
    c.Supervisor,
    c.Vendedor,
    c.MetaMes,
    c.Facturacion,
    c.Pedidos,
    c.Entregas,
    c.TotalNeto,
    p.TotalNeto AS TotalNetoAA,
    (c.TotalNeto - ISNULL(p.TotalNeto, 0)) AS YoY_Valor,
    CAST(
        CASE
            WHEN NULLIF(p.TotalNeto, 0) IS NULL THEN NULL
            ELSE ((c.TotalNeto - p.TotalNeto) / NULLIF(p.TotalNeto, 0)) * 100.0
        END
    AS DECIMAL(10,2)) AS YoY_Pct,
    CAST(
        CASE
            WHEN NULLIF(c.MetaMes,0) IS NULL THEN NULL
            ELSE (c.TotalNeto / NULLIF(c.MetaMes,0)) * 100.0
        END
    AS DECIMAL(10,2)) AS CumplimientoNum
FROM CUR c
LEFT JOIN PREV p
    ON p.Vendedor = c.Vendedor
   AND p.MesNum = c.MesNum
ORDER BY c.Anio, c.MesNum;";

            try
            {
                using var cn = new SqlConnection(_config.GetConnectionString("SAP"));
                var rows = (await cn.QueryAsync(sql, new { Anio = anio, SlpCode = slp })).ToList();
                return Ok(rows);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error al consultar cumplimiento.", detail = ex.Message });
            }
        }









        // ✅ Patrón único solicitado
        private string ConnStr => _config.GetConnectionString("SAP")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:SAP");

        // ============================================================
        // Helper DataTable -> List<Dictionary<string, object?>>
        // ============================================================
        private static List<Dictionary<string, object?>> ToList(DataTable dt)
        {
            var list = new List<Dictionary<string, object?>>(dt.Rows.Count);
            foreach (DataRow row in dt.Rows)
            {
                var dic = new Dictionary<string, object?>(dt.Columns.Count);
                foreach (DataColumn col in dt.Columns)
                    dic[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                list.Add(dic);
            }
            return list;
        }
        private string GetUsernameFromToken()
        {
            return
                User.Claims.FirstOrDefault(c => c.Type == "login")?.Value
                ?? User.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                ?? User.Identity?.Name
                ?? "";
        }

        private static string NormalizarCategoria(string categoria)
        {
            return (categoria ?? "Ventas Quimicos")
                .Replace("Químicos", "Quimicos")
                .Replace("Máquinas", "Maquinas")
                .Replace("Técnico", "Tecnico")
                .Replace("á", "a")
                .Replace("é", "e")
                .Replace("í", "i")
                .Replace("ó", "o")
                .Replace("ú", "u")
                .Trim();
        }

        private static string GetSqlMetasVendedores(int mes)
        {
            var campoMeta = mes switch
            {
                1 => "U_METAS_Ene",
                2 => "U_METAS_Feb",
                3 => "U_METAS_Mar",
                4 => "U_METAS_Abr",
                5 => "U_METAS_May",
                6 => "U_METAS_Jun",
                7 => "U_METAS_Jul",
                8 => "U_METAS_Ago",
                9 => "U_METAS_Sep",
                10 => "U_METAS_Oct",
                11 => "U_METAS_Nov",
                12 => "U_METAS_Dic",
                _ => "U_METAS_Ene"
            };

            return $@"
                    SELECT
                        CASE 
                            WHEN U_Div_Pref = 'BSC' THEN 'Equipo Nelson Norambuena'
                            WHEN U_Div_Pref IN ('FB','IN') THEN 'Equipo Claudia Borquez'
                            WHEN U_Div_Pref = 'HC' THEN 'Equipo Ives Camousseight'
                            WHEN U_Div_Pref = 'IND' THEN 'Equipo Alberto Damm'
                            WHEN U_Div_Pref = 'IND_HL' THEN 'Equipo Hernan Lopez'
                            WHEN U_Div_Pref = 'IND_PR' THEN 'Equipo Patricio Roco'
                            ELSE 'Sin Equipo'
                        END AS Equipo,
                        SlpName AS Vendedor,
                        CAST(ISNULL({campoMeta}, 0) AS decimal(19,2)) AS Meta
                    FROM OSLP
                    WHERE Active = 'Y';";
        }















        private async Task<int?> GetUserIdByUsername(SqlConnection conn, string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            var id = await conn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 Id
FROM dbo.ApiUsuarios
WHERE Usuario = @u AND Activo = 1;", new { u = username.Trim() });

            return id;
        }

        private async Task<HashSet<int>> GetAllowedCentroCosto(SqlConnection conn, int userId)
        {
            // ADMIN: todo
            if (User.IsInRole("ADMIN"))
                return new HashSet<int> { 10, 30, 40, 50 };

            // GERENCIA: según tabla
            var rows = await conn.QueryAsync<int>(@"
SELECT DISTINCT PrcCode
FROM dbo.ApiUsuarioCentroCosto
WHERE UsuarioId = @userId AND Activo = 1;", new { userId });

            var set = rows.ToHashSet();

            // Si no hay asignación y es GERENCIA, por seguridad NO mostrar nada (Forbid)
            // (si quieres que “gerencia general” vea todo, asígnale 10,30,40,50)
            return set;
        }

        private static string ToCsv(HashSet<int> cc) => string.Join(",", cc.OrderBy(x => x));



        // ============================================================
        // CIERRE SEMANAL (VENDEDOR) CONGELADO 3 DIAS (SNAPSHOT)
        // - Basado en dbo.ApiCalendarioCierreSemanal (semana cerrada vigente)
        // - Congela resultado por FreezeDias (ej: 3 dias) para que no cambie
        // GET /api/ventas/cierre-semanal?v=1&anio=2026&mes=1   (anio/mes opcional)
        // ============================================================
        // ⚠️ Este endpoint normalmente es de VENDEDOR/SUPERVISOR también.
        // Si lo quieres solo GERENCIA/ADMIN, agrega Roles aquí.
        [HttpGet("/api/ventas/cierre-semanal")]
        public async Task<IActionResult> GetCierreSemanalVendedorCongelado(
            [FromQuery] int? anio,
            [FromQuery] int? mes
        )
        {
            var hoy = DateTime.Today;
            int y = anio ?? hoy.Year;
            int m = mes ?? hoy.Month;

            if (y < 2000 || y > 2100) return BadRequest("Año inválido.");
            if (m < 1 || m > 12) return BadRequest("Mes inválido.");

            // Obtener SlpCode desde el JWT (según tus claims)
            var slpClaim =
                User.Claims.FirstOrDefault(c => c.Type == "SlpCode")?.Value ??
                User.Claims.FirstOrDefault(c => c.Type == "slpCode")?.Value ??
                User.Claims.FirstOrDefault(c => c.Type == "slpcode")?.Value ??
                User.Claims.FirstOrDefault(c => c.Type == "slp")?.Value;

            if (string.IsNullOrWhiteSpace(slpClaim) || !int.TryParse(slpClaim, out int slpCode))
                return Unauthorized("No se pudo determinar SlpCode del usuario.");

            const int freezeDias = 3;
            const string acctCode = "40101001"; // Químicos fijo

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                await using var cmd = new SqlCommand("dbo.SP_Api_GetCierreSemanalCongelado_Vendedor", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                cmd.Parameters.Add(new SqlParameter("@Anio", SqlDbType.Int) { Value = y });
                cmd.Parameters.Add(new SqlParameter("@Mes", SqlDbType.Int) { Value = m });
                cmd.Parameters.Add(new SqlParameter("@SlpCode", SqlDbType.Int) { Value = slpCode });
                cmd.Parameters.Add(new SqlParameter("@FreezeDias", SqlDbType.Int) { Value = freezeDias });
                cmd.Parameters.Add(new SqlParameter("@AcctCode", SqlDbType.VarChar, 20) { Value = acctCode });

                using var da = new SqlDataAdapter(cmd);
                var dt = new DataTable();
                da.Fill(dt);

                if (dt.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        anio = y,
                        mes = m,
                        slpCode,
                        semana = 0,
                        fechaCierre = (string?)null,
                        desde = (string?)null,
                        hasta = (string?)null,
                        congelado = false,
                        freezeUntil = (string?)null,
                        factura = 0m,
                        pedido = 0m,
                        entrega = 0m,
                        total = 0m
                    });
                }

                var r = dt.Rows[0];

                decimal fac = r["Facturas"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Facturas"]);
                decimal ped = r["Pedidos"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Pedidos"]);
                decimal ent = r["Entregas"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Entregas"]);
                decimal tot = r["Total"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Total"]);

                bool congelado = r["Congelado"] != DBNull.Value && Convert.ToBoolean(r["Congelado"]);

                string? fechaCierre = r["FechaCierre"] == DBNull.Value ? null : Convert.ToDateTime(r["FechaCierre"]).ToString("yyyy-MM-dd");
                string? desde = r["Desde"] == DBNull.Value ? null : Convert.ToDateTime(r["Desde"]).ToString("yyyy-MM-dd");
                string? hasta = r["Hasta"] == DBNull.Value ? null : Convert.ToDateTime(r["Hasta"]).ToString("yyyy-MM-dd");
                string? freezeUntil = r["FreezeUntil"] == DBNull.Value ? null : Convert.ToDateTime(r["FreezeUntil"]).ToString("yyyy-MM-dd");

                int semana = r["Semana"] == DBNull.Value ? 0 : Convert.ToInt32(r["Semana"]);

                return Ok(new
                {
                    anio = y,
                    mes = m,
                    slpCode,
                    semana,
                    fechaCierre,
                    desde,
                    hasta,
                    congelado,
                    freezeUntil,
                    factura = fac,
                    pedido = ped,
                    entrega = ent,
                    total = tot
                });
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // 1) ENDPOINT EXISTENTE (NO TOCAR)
        // ============================================================
        // ⚠️ Este suele ser GERENCIA/ADMIN. Ajusta si corresponde.
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("resultados-total")]
        public async Task<IActionResult> GetResultadosTotal(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta,
            [FromQuery] decimal umbralPctGastoVenta = 35,
            [FromQuery] int topN = 10,
            [FromQuery] string? profitCode = null,
            [FromQuery] bool incluirRankings = false,
            [FromQuery] bool incluirCatalogo = true
        )
        {
            if (desde == default || hasta == default)
                return BadRequest("Debe indicar desde y hasta.");
            if (hasta.Date < desde.Date)
                return BadRequest("El rango de fechas es inválido: hasta < desde.");
            if (topN <= 0) topN = 10;

            var usuario =
                User.Claims.FirstOrDefault(c => c.Type == "usuario")?.Value
                ?? User.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                ?? User.Identity?.Name
                ?? "";

            if (string.IsNullOrWhiteSpace(usuario))
                return Unauthorized("No se pudo identificar el usuario desde el token.");

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                await using var cmd = new SqlCommand("dbo.SP_GER_Resultados_Total_Division", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                cmd.Parameters.Add(new SqlParameter("@Desde", SqlDbType.Date) { Value = desde.Date });
                cmd.Parameters.Add(new SqlParameter("@Hasta", SqlDbType.Date) { Value = hasta.Date });
                cmd.Parameters.Add(new SqlParameter("@Usuario", SqlDbType.NVarChar, 50) { Value = usuario });
                cmd.Parameters.Add(new SqlParameter("@UmbralPctGastoVenta", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = umbralPctGastoVenta });
                cmd.Parameters.Add(new SqlParameter("@TopN", SqlDbType.Int) { Value = topN });

                cmd.Parameters.Add(new SqlParameter("@ProfitCode", SqlDbType.NVarChar, 20)
                {
                    Value = (object?)profitCode ?? DBNull.Value
                });

                cmd.Parameters.Add(new SqlParameter("@IncluirRankings", SqlDbType.Bit) { Value = incluirRankings });
                cmd.Parameters.Add(new SqlParameter("@IncluirCatalogo", SqlDbType.Bit) { Value = incluirCatalogo });

                var ds = new DataSet();
                using (var da = new SqlDataAdapter(cmd))
                {
                    da.TableMappings.Add("Table", "catalogoCC");          // #0
                    da.TableMappings.Add("Table1", "resumenDivisiones");  // #1
                    da.TableMappings.Add("Table2", "estacionalidad");     // #2
                    da.TableMappings.Add("Table3", "topGastos");          // #3
                    da.TableMappings.Add("Table4", "bottomResultado");    // #4
                    da.TableMappings.Add("Table5", "topPctGastoVenta");   // #5
                    da.Fill(ds);
                }

                DataTable? tCatalogo = ds.Tables.Contains("catalogoCC") ? ds.Tables["catalogoCC"] : null;
                DataTable? tResumen = ds.Tables.Contains("resumenDivisiones") ? ds.Tables["resumenDivisiones"] : null;
                DataTable? tEst = ds.Tables.Contains("estacionalidad") ? ds.Tables["estacionalidad"] : null;

                DataTable? tTopG = ds.Tables.Contains("topGastos") ? ds.Tables["topGastos"] : null;
                DataTable? tBottom = ds.Tables.Contains("bottomResultado") ? ds.Tables["bottomResultado"] : null;
                DataTable? tTopPct = ds.Tables.Contains("topPctGastoVenta") ? ds.Tables["topPctGastoVenta"] : null;

                return Ok(new
                {
                    desde = desde.Date,
                    hasta = hasta.Date,
                    usuario,
                    umbralPctGastoVenta,
                    topN,
                    profitCode,
                    catalogoCC = tCatalogo != null ? ToList(tCatalogo) : new List<Dictionary<string, object?>>(),
                    resumenDivisiones = tResumen != null ? ToList(tResumen) : new List<Dictionary<string, object?>>(),
                    estacionalidadMensual = tEst != null ? ToList(tEst) : new List<Dictionary<string, object?>>(),
                    rankings = new
                    {
                        topGastos = tTopG != null ? ToList(tTopG) : new List<Dictionary<string, object?>>(),
                        bottomResultado = tBottom != null ? ToList(tBottom) : new List<Dictionary<string, object?>>(),
                        topPctGastoVenta = tTopPct != null ? ToList(tTopPct) : new List<Dictionary<string, object?>>()
                    }
                });
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // 2) NUEVO: Ranking Vendedores (Químicos) - Mes en curso
        // GET /api/gerencia/quimicos-ranking-vendedores-mes
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("quimicos-ranking-vendedores-mes")]
        public async Task<IActionResult> GetQuimicosRankingVendedoresMes()
        {
            const string sql = @"
DECLARE @Hoy   date = CAST(GETDATE() AS date);
DECLARE @Desde date = DATEFROMPARTS(YEAR(@Hoy), MONTH(@Hoy), 1);
DECLARE @Hasta date = EOMONTH(@Hoy);

;WITH Vendedores AS (
    SELECT
        S.SlpCode,
        S.SlpName,
        CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) AS CentroCosto,
        CAST(CASE MONTH(@Hoy)
            WHEN 1  THEN ISNULL(S.U_METAS_Ene, 0)
            WHEN 2  THEN ISNULL(S.U_METAS_Feb, 0)
            WHEN 3  THEN ISNULL(S.U_METAS_Mar, 0)
            WHEN 4  THEN ISNULL(S.U_METAS_Abr, 0)
            WHEN 5  THEN ISNULL(S.U_METAS_May, 0)
            WHEN 6  THEN ISNULL(S.U_METAS_Jun, 0)
            WHEN 7  THEN ISNULL(S.U_METAS_Jul, 0)
            WHEN 8  THEN ISNULL(S.U_METAS_Ago, 0)
            WHEN 9  THEN ISNULL(S.U_METAS_Sep, 0)
            WHEN 10 THEN ISNULL(S.U_METAS_Oct, 0)
            WHEN 11 THEN ISNULL(S.U_METAS_Nov, 0)
            WHEN 12 THEN ISNULL(S.U_METAS_Dic, 0)
        END AS decimal(19,2)) AS MetaMes
    FROM OSLP S
    WHERE
        S.U_Centro_Costo IS NOT NULL
        AND LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) <> ''
        AND ISNUMERIC(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) ) = 1
        AND CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) IN (10, 30, 40, 50)
),
Mov AS (
    -- FACTURAS (aplica descuento de documento)
    SELECT
        I.SlpCode,
        SUM( I1.LineTotal * (1 - (ISNULL(I.DiscPrcnt,0) / 100.0)) ) AS Neto
    FROM OINV I
    INNER JOIN INV1 I1 ON I.DocEntry = I1.DocEntry
    WHERE I.CANCELED = 'N'
      AND I.DocDate BETWEEN @Desde AND @Hasta
      AND I1.AcctCode = '40101001'
    GROUP BY I.SlpCode
    UNION ALL
    -- NOTAS DE CRÉDITO (aplica descuento de documento)
    SELECT
        R.SlpCode,
        -SUM( R1.LineTotal * (1 - (ISNULL(R.DiscPrcnt,0) / 100.0)) ) AS Neto
    FROM ORIN R
    INNER JOIN RIN1 R1 ON R.DocEntry = R1.DocEntry
    WHERE R.CANCELED = 'N'
      AND R.DocDate BETWEEN @Desde AND @Hasta
      AND R1.AcctCode = '40101001'
    GROUP BY R.SlpCode
),
TotVendedor AS (
    SELECT SlpCode, SUM(Neto) AS Neto_Real
    FROM Mov
    GROUP BY SlpCode
)
SELECT
    V.CentroCosto,
    CASE V.CentroCosto
        WHEN 10 THEN 'División Industrial'
        WHEN 30 THEN 'División Health Care'
        WHEN 40 THEN 'División Food & Beverages'
        WHEN 50 THEN 'División Institucional'
    END AS Division,
    V.SlpCode,
    V.SlpName AS Vendedor,
    V.MetaMes AS Meta_Mes,
    CAST(ISNULL(T.Neto_Real,0) AS decimal(19,2)) AS Neto_Real,
    CAST(CASE WHEN V.MetaMes <= 0 THEN 0 ELSE (ISNULL(T.Neto_Real,0) / NULLIF(V.MetaMes,0)) * 100 END AS decimal(19,2)) AS Cumplimiento_Pct,
    CAST(CASE WHEN V.MetaMes <= 0 THEN 0 WHEN ISNULL(T.Neto_Real,0) >= V.MetaMes THEN 0 ELSE (V.MetaMes - ISNULL(T.Neto_Real,0)) END AS decimal(19,2)) AS Faltante,
    CASE
        WHEN V.MetaMes <= 0 THEN 'SIN META'
        WHEN (ISNULL(T.Neto_Real,0) / NULLIF(V.MetaMes,0)) < 0.60 THEN 'ROJO'
        WHEN (ISNULL(T.Neto_Real,0) / NULLIF(V.MetaMes,0)) < 1.00 THEN 'AMARILLO'
        ELSE 'VERDE'
    END AS Estado,
    CASE
        WHEN V.MetaMes <= 0 THEN '#6c757d'
        WHEN (ISNULL(T.Neto_Real,0) / NULLIF(V.MetaMes,0)) < 0.60 THEN '#dc3545'
        WHEN (ISNULL(T.Neto_Real,0) / NULLIF(V.MetaMes,0)) < 1.00 THEN '#ffc107'
        ELSE '#198754'
    END AS Color
FROM Vendedores V
LEFT JOIN TotVendedor T ON T.SlpCode = V.SlpCode
ORDER BY Neto_Real DESC, V.SlpName;";

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                await using var cmd = new SqlCommand(sql, conn)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 120
                };

                var dt = new DataTable();
                using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);

                return Ok(ToList(dt));
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // 3) NUEVO: Ranking Divisiones (Químicos) - Mes en curso
        // GET /api/gerencia/quimicos-divisiones-mes
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("quimicos-divisiones-mes")]
        public async Task<IActionResult> GetQuimicosDivisionesMes()
        {
            const string sql = @"
DECLARE @Hoy   date = CAST(GETDATE() AS date);
DECLARE @Desde date = DATEFROMPARTS(YEAR(@Hoy), MONTH(@Hoy), 1);
DECLARE @Hasta date = EOMONTH(@Hoy);

;WITH OslpFiltrado AS (
    SELECT
        S.SlpCode,
        CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) AS CentroCosto,
        CAST(CASE MONTH(@Hoy)
            WHEN 1  THEN ISNULL(S.U_METAS_Ene, 0)
            WHEN 2  THEN ISNULL(S.U_METAS_Feb, 0)
            WHEN 3  THEN ISNULL(S.U_METAS_Mar, 0)
            WHEN 4  THEN ISNULL(S.U_METAS_Abr, 0)
            WHEN 5  THEN ISNULL(S.U_METAS_May, 0)
            WHEN 6  THEN ISNULL(S.U_METAS_Jun, 0)
            WHEN 7  THEN ISNULL(S.U_METAS_Jul, 0)
            WHEN 8  THEN ISNULL(S.U_METAS_Ago, 0)
            WHEN 9  THEN ISNULL(S.U_METAS_Sep, 0)
            WHEN 10 THEN ISNULL(S.U_METAS_Oct, 0)
            WHEN 11 THEN ISNULL(S.U_METAS_Nov, 0)
            WHEN 12 THEN ISNULL(S.U_METAS_Dic, 0)
        END AS decimal(19,2)) AS MetaMes
    FROM OSLP S
    WHERE
        S.U_Centro_Costo IS NOT NULL
        AND LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) <> ''
        AND ISNUMERIC(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) ) = 1
        AND CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) IN (10, 30, 40, 50)
),
Mov AS (
    SELECT I.SlpCode, SUM(I1.LineTotal * (1 - (ISNULL(I.DiscPrcnt,0) / 100.0))) AS Neto
    FROM OINV I
    INNER JOIN INV1 I1 ON I.DocEntry = I1.DocEntry
    WHERE I.CANCELED = 'N'
      AND I.DocDate BETWEEN @Desde AND @Hasta
      AND I1.AcctCode = '40101001'
    GROUP BY I.SlpCode
    UNION ALL
    SELECT R.SlpCode, -SUM(R1.LineTotal * (1 - (ISNULL(R.DiscPrcnt,0) / 100.0))) AS Neto
    FROM ORIN R
    INNER JOIN RIN1 R1 ON R.DocEntry = R1.DocEntry
    WHERE R.CANCELED = 'N'
      AND R.DocDate BETWEEN @Desde AND @Hasta
      AND R1.AcctCode = '40101001'
    GROUP BY R.SlpCode
),
TotVendedor AS (
    SELECT SlpCode, SUM(Neto) AS Neto_Real
    FROM Mov
    GROUP BY SlpCode
),
AgrDiv AS (
    SELECT
        O.CentroCosto,
        CAST(SUM(ISNULL(T.Neto_Real,0)) AS decimal(19,2)) AS Neto_Real_Div,
        CAST(SUM(O.MetaMes) AS decimal(19,2)) AS Meta_Mes_Div
    FROM OslpFiltrado O
    LEFT JOIN TotVendedor T ON T.SlpCode = O.SlpCode
    GROUP BY O.CentroCosto
)
SELECT
    A.CentroCosto,
    CASE A.CentroCosto
        WHEN 10 THEN 'División Industrial'
        WHEN 30 THEN 'División Health Care'
        WHEN 40 THEN 'División Food & Beverages'
        WHEN 50 THEN 'División Institucional'
    END AS Division,
    A.Neto_Real_Div,
    A.Meta_Mes_Div,
    CAST(CASE WHEN A.Meta_Mes_Div <= 0 THEN 0 ELSE (A.Neto_Real_Div / NULLIF(A.Meta_Mes_Div,0)) * 100 END AS decimal(19,2)) AS Cumplimiento_Pct_Div,
    CAST(CASE WHEN A.Meta_Mes_Div <= 0 THEN 0 WHEN A.Neto_Real_Div >= A.Meta_Mes_Div THEN 0 ELSE (A.Meta_Mes_Div - A.Neto_Real_Div) END AS decimal(19,2)) AS Faltante_Div,
    CASE
        WHEN A.Meta_Mes_Div <= 0 THEN 'SIN META'
        WHEN (A.Neto_Real_Div / NULLIF(A.Meta_Mes_Div,0)) < 0.60 THEN 'ROJO'
        WHEN (A.Neto_Real_Div / NULLIF(A.Meta_Mes_Div,0)) < 1.00 THEN 'AMARILLO'
        ELSE 'VERDE'
    END AS Estado,
    CASE
        WHEN A.Meta_Mes_Div <= 0 THEN '#6c757d'
        WHEN (A.Neto_Real_Div / NULLIF(A.Meta_Mes_Div,0)) < 0.60 THEN '#dc3545'
        WHEN (A.Neto_Real_Div / NULLIF(A.Meta_Mes_Div,0)) < 1.00 THEN '#ffc107'
        ELSE '#198754'
    END AS Color
FROM AgrDiv A
ORDER BY A.Neto_Real_Div DESC;";

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                await using var cmd = new SqlCommand(sql, conn)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 120
                };

                var dt = new DataTable();
                using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);

                return Ok(ToList(dt));
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // Ranking Zona Químicos - Mes
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("ranking-zona-quimicos-mes")]
        public async Task<IActionResult> GetRankingZonaQuimicosMes()
        {
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                await using var cmd = new SqlCommand("dbo.SP_GER_Ranking_Quimicos_Zona_MesActual", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                var dt = new DataTable();
                using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);

                return Ok(ToList(dt));
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // KPI Químicos Resumen (mes | mesanterior | ytd)
        // GET /api/gerencia/kpi-quimicos-resumen?periodo=mes
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("kpi-quimicos-resumen")]
        public async Task<IActionResult> GetKpiQuimicosResumen([FromQuery] string periodo = "mes")
        {
            periodo = (periodo ?? "mes").Trim().ToLowerInvariant();

            DateTime hoy = DateTime.Today;
            DateTime desde;
            DateTime hasta;

            if (periodo == "mesanterior")
            {
                var prev = hoy.AddMonths(-1);
                desde = new DateTime(prev.Year, prev.Month, 1);
                hasta = new DateTime(prev.Year, prev.Month, DateTime.DaysInMonth(prev.Year, prev.Month));
            }
            else if (periodo == "ytd")
            {
                desde = new DateTime(hoy.Year, 1, 1);
                hasta = hoy;
            }
            else
            {
                desde = new DateTime(hoy.Year, hoy.Month, 1);
                hasta = hoy;
                periodo = "mes";
            }

            const string sql = @"
DECLARE @Desde date = @P_Desde;
DECLARE @Hasta date = @P_Hasta;
DECLARE @Mes int = MONTH(@Desde);

;WITH Vendedores AS (
    SELECT
        S.SlpCode,
        CAST(
            CASE 
                WHEN @P_Periodo = 'ytd' THEN
                    ISNULL(S.U_METAS_Ene,0)
                    + CASE WHEN @Mes >= 2  THEN ISNULL(S.U_METAS_Feb,0) ELSE 0 END
                    + CASE WHEN @Mes >= 3  THEN ISNULL(S.U_METAS_Mar,0) ELSE 0 END
                    + CASE WHEN @Mes >= 4  THEN ISNULL(S.U_METAS_Abr,0) ELSE 0 END
                    + CASE WHEN @Mes >= 5  THEN ISNULL(S.U_METAS_May,0) ELSE 0 END
                    + CASE WHEN @Mes >= 6  THEN ISNULL(S.U_METAS_Jun,0) ELSE 0 END
                    + CASE WHEN @Mes >= 7  THEN ISNULL(S.U_METAS_Jul,0) ELSE 0 END
                    + CASE WHEN @Mes >= 8  THEN ISNULL(S.U_METAS_Ago,0) ELSE 0 END
                    + CASE WHEN @Mes >= 9  THEN ISNULL(S.U_METAS_Sep,0) ELSE 0 END
                    + CASE WHEN @Mes >= 10 THEN ISNULL(S.U_METAS_Oct,0) ELSE 0 END
                    + CASE WHEN @Mes >= 11 THEN ISNULL(S.U_METAS_Nov,0) ELSE 0 END
                    + CASE WHEN @Mes >= 12 THEN ISNULL(S.U_METAS_Dic,0) ELSE 0 END
                ELSE
                    CASE @Mes
                        WHEN 1  THEN ISNULL(S.U_METAS_Ene, 0)
                        WHEN 2  THEN ISNULL(S.U_METAS_Feb, 0)
                        WHEN 3  THEN ISNULL(S.U_METAS_Mar, 0)
                        WHEN 4  THEN ISNULL(S.U_METAS_Abr, 0)
                        WHEN 5  THEN ISNULL(S.U_METAS_May, 0)
                        WHEN 6  THEN ISNULL(S.U_METAS_Jun, 0)
                        WHEN 7  THEN ISNULL(S.U_METAS_Jul, 0)
                        WHEN 8  THEN ISNULL(S.U_METAS_Ago, 0)
                        WHEN 9  THEN ISNULL(S.U_METAS_Sep, 0)
                        WHEN 10 THEN ISNULL(S.U_METAS_Oct, 0)
                        WHEN 11 THEN ISNULL(S.U_METAS_Nov, 0)
                        WHEN 12 THEN ISNULL(S.U_METAS_Dic, 0)
                    END
            END
        AS decimal(19,2)) AS MetaVendedor
    FROM OSLP S
    WHERE
        S.Active = 'Y'
        AND S.U_Centro_Costo IS NOT NULL
        AND LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) <> ''
        AND ISNUMERIC(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50))))) = 1
        AND CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) IN (10, 30, 40, 50)
),
Mov AS (
    SELECT I.SlpCode, SUM(I1.LineTotal * (1 - (ISNULL(I.DiscPrcnt,0)/100.0))) AS Neto
    FROM OINV I
    INNER JOIN INV1 I1 ON I.DocEntry = I1.DocEntry
    WHERE I.CANCELED = 'N'
      AND I.DocDate BETWEEN @Desde AND @Hasta
      AND I1.AcctCode = '40101001'
    GROUP BY I.SlpCode
    UNION ALL
    SELECT R.SlpCode, - SUM(R1.LineTotal * (1 - (ISNULL(R.DiscPrcnt,0)/100.0))) AS Neto
    FROM ORIN R
    INNER JOIN RIN1 R1 ON R.DocEntry = R1.DocEntry
    WHERE R.CANCELED = 'N'
      AND R.DocDate BETWEEN @Desde AND @Hasta
      AND R1.AcctCode = '40101001'
    GROUP BY R.SlpCode
),
Tot AS (
    SELECT
        CAST(SUM(ISNULL(M.Neto,0)) AS decimal(19,2)) AS NetoTotal,
        CAST(SUM(V.MetaVendedor) AS decimal(19,2)) AS MetaTotal
    FROM Vendedores V
    LEFT JOIN Mov M ON M.SlpCode = V.SlpCode
)
SELECT
    NetoTotal,
    MetaTotal,
    CAST(CASE WHEN MetaTotal <= 0 THEN 0 ELSE (NetoTotal / NULLIF(MetaTotal,0)) * 100 END AS decimal(19,2)) AS CumplimientoPct,
    CAST(CASE WHEN MetaTotal <= 0 THEN 0 WHEN NetoTotal >= MetaTotal THEN 0 ELSE (MetaTotal - NetoTotal) END AS decimal(19,2)) AS Faltante
FROM Tot;";

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                await using var cmd = new SqlCommand(sql, conn)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 120
                };

                cmd.Parameters.Add(new SqlParameter("@P_Desde", SqlDbType.Date) { Value = desde.Date });
                cmd.Parameters.Add(new SqlParameter("@P_Hasta", SqlDbType.Date) { Value = hasta.Date });
                cmd.Parameters.Add(new SqlParameter("@P_Periodo", SqlDbType.NVarChar, 20) { Value = periodo });

                var dt = new DataTable();
                using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);

                var row = dt.Rows.Count > 0 ? dt.Rows[0] : null;

                return Ok(new
                {
                    periodo,
                    desde = desde.Date,
                    hasta = hasta.Date,
                    netoTotal = row == null ? 0 : row["NetoTotal"],
                    metaTotal = row == null ? 0 : row["MetaTotal"],
                    cumplimientoPct = row == null ? 0 : row["CumplimientoPct"],
                    faltante = row == null ? 0 : row["Faltante"]
                });
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // CIERRE SEMANAL (GERENCIA) PERO EN MODO MTD
        // GET /api/gerencia/cierre-semanal-mtd?anio=2026&mes=1
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("cierre-semanal-mtd")]
        public async Task<IActionResult> GetCierreSemanalMTD([FromQuery] int anio, [FromQuery] int mes)
        {
            if (anio < 2000 || anio > 2100) return BadRequest("Año inválido.");
            if (mes < 1 || mes > 12) return BadRequest("Mes inválido.");

            const string acctCode = "40101001";

            

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                // ===================== FILTRO POR DIVISION (CentroCosto) =====================
                var username = GetUsernameFromToken();
                var userId = await GetUserIdByUsername(conn, username);
                if (userId == null) return Unauthorized("Usuario no identificado.");

                var allowedCC = await GetAllowedCentroCosto(conn, userId.Value);
                if (allowedCC.Count == 0) return Forbid("No tienes divisiones asignadas para Gerencia.");

                var allowedCsv = ToCsv(allowedCC);


                DateTime hoy = DateTime.Today;
                DateTime? fechaCorte = null;

                await using (var cmdCorte = new SqlCommand(@"
SELECT MAX(FechaCierre) AS FechaCorte
FROM dbo.ApiCalendarioCierreSemanal
WHERE Anio = @Anio
  AND Mes  = @Mes
  AND FechaCierre <= @Hoy;", conn))
                {
                    cmdCorte.Parameters.Add(new SqlParameter("@Anio", SqlDbType.Int) { Value = anio });
                    cmdCorte.Parameters.Add(new SqlParameter("@Mes", SqlDbType.Int) { Value = mes });
                    cmdCorte.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.Date) { Value = hoy });

                    var obj = await cmdCorte.ExecuteScalarAsync();
                    if (obj != null && obj != DBNull.Value) fechaCorte = Convert.ToDateTime(obj).Date;
                }

                var desde = new DateTime(anio, mes, 1).Date;
                var hasta = (fechaCorte ?? hoy).Date;

                if (hasta.Month != mes || hasta.Year != anio)
                    hasta = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes)).Date;

                var dtVend = new DataTable();
                await using (var cmdV = new SqlCommand(@"
SELECT S.SlpCode, S.SlpName, ISNULL(S.U_Zona,'') AS ZonaChile
FROM OSLP S
WHERE S.Active = 'Y'
  AND S.U_Centro_Costo IS NOT NULL
  AND LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) <> ''
  AND ISNUMERIC(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50))))) = 1
  AND CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) IN
    (
        SELECT TRY_CAST(value AS int)
        FROM STRING_SPLIT(@AllowedCC, ',')
        WHERE TRY_CAST(value AS int) IS NOT NULL
    )
ORDER BY S.SlpName;", conn))

                {
                    cmdV.Parameters.Add(new SqlParameter("@AllowedCC", SqlDbType.VarChar, 100) { Value = allowedCsv });
                    using var daV = new SqlDataAdapter(cmdV);
                    daV.Fill(dtVend);
                }


                var detalle = new DataTable();
                detalle.Columns.Add("YEAR", typeof(int));
                detalle.Columns.Add("Mes", typeof(string));
                detalle.Columns.Add("Desde", typeof(DateTime));
                detalle.Columns.Add("Hasta", typeof(DateTime));
                detalle.Columns.Add("ZonaChile", typeof(string));
                detalle.Columns.Add("Vendedor", typeof(string));
                detalle.Columns.Add("SlpCode", typeof(int));
                detalle.Columns.Add("Facturas", typeof(decimal));
                detalle.Columns.Add("Pedidos", typeof(decimal));
                detalle.Columns.Add("Entregas", typeof(decimal));
                detalle.Columns.Add("Total", typeof(decimal));

                await using var cmdSP = new SqlCommand("dbo.SP_Dashboard_CierreSemanal_Vendedor", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                var pSlp = cmdSP.Parameters.Add("@SlpCode", SqlDbType.Int);
                var pDesde = cmdSP.Parameters.Add("@Desde", SqlDbType.Date);
                var pHasta = cmdSP.Parameters.Add("@Hasta", SqlDbType.Date);
                cmdSP.Parameters.Add(new SqlParameter("@AcctCode", SqlDbType.VarChar, 20) { Value = acctCode });

                using var daSP = new SqlDataAdapter(cmdSP);

                foreach (DataRow v in dtVend.Rows)
                {
                    int slpCode = Convert.ToInt32(v["SlpCode"]);
                    string vendedor = v["SlpName"]?.ToString() ?? "";
                    string zona = v["ZonaChile"]?.ToString() ?? "";

                    pSlp.Value = slpCode;
                    pDesde.Value = desde;
                    pHasta.Value = hasta;

                    var dt = new DataTable();
                    daSP.Fill(dt);

                    decimal fac = 0, ped = 0, ent = 0, tot = 0;
                    if (dt.Rows.Count > 0)
                    {
                        var r = dt.Rows[0];
                        fac = r["Factura"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Factura"]);
                        ped = r["Pedido"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Pedido"]);
                        ent = r["Entrega"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Entrega"]);
                        tot = r["Total"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Total"]);
                    }

                    if (fac == 0 && ped == 0 && ent == 0 && tot == 0) continue;

                    detalle.Rows.Add(
                        anio,
                        new DateTime(anio, mes, 1).ToString("MMMM", new CultureInfo("es-CL")),
                        desde, hasta,
                        zona,
                        vendedor, slpCode,
                        fac, ped, ent, tot
                    );
                }

                decimal totalFact = 0, totalPed = 0, totalEnt = 0, totalAll = 0;
                foreach (DataRow r in detalle.Rows)
                {
                    totalFact += Convert.ToDecimal(r["Facturas"]);
                    totalPed += Convert.ToDecimal(r["Pedidos"]);
                    totalEnt += Convert.ToDecimal(r["Entregas"]);
                    totalAll += Convert.ToDecimal(r["Total"]);
                }

                return Ok(new
                {
                    anio,
                    mes,
                    desde,
                    hasta,
                    vendedoresConsiderados = dtVend.Rows.Count,
                    vendedoresConMovimiento = detalle.Rows.Count,
                    totales = new
                    {
                        facturas = totalFact,
                        pedidos = totalPed,
                        entregas = totalEnt,
                        total = totalAll
                    },
                    detalle = ToList(detalle)
                });
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("proyeccion-cierre")]
        public async Task<IActionResult> GetProyeccionCierre(
    [FromQuery] int anio,
    [FromQuery] int mes,
    [FromQuery] string categoria = "Ventas Quimicos",
    [FromQuery] string division = "Todas")
        {
            try
            {
                if (anio < 2000 || anio > 2100)
                    return BadRequest("Año inválido.");

                if (mes < 1 || mes > 12)
                    return BadRequest("Mes inválido.");

                categoria = NormalizarCategoria(categoria);
                division = string.IsNullOrWhiteSpace(division) ? "Todas" : division;

                var desde = new DateTime(anio, mes, 1);
                var hasta = desde.AddMonths(1).AddDays(-1);

                await using var conn = new SqlConnection(ConnStr);

                using var multi = await conn.QueryMultipleAsync(
                    "dbo.sp_BI_ProyeccionCierre",
                    new
                    {
                        Desde = desde,
                        Hasta = hasta,
                        Categoria = categoria,
                        Division = division
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 180
                );

                var totalesSp = await multi.ReadFirstOrDefaultAsync<ProyeccionCierreTotalesDto>();
                var equiposSp = (await multi.ReadAsync<ProyeccionCierreEquipoBaseDto>()).ToList();
                var vendedoresSp = (await multi.ReadAsync<ProyeccionCierreVendedorBaseDto>()).ToList();

                var metasVendedores = (await conn.QueryAsync<MetaVendedorDto>(
                    GetSqlMetasVendedores(mes)
                )).ToList();

                var metasEquipos = metasVendedores
                    .GroupBy(x => x.Equipo)
                    .Select(g => new
                    {
                        Equipo = g.Key,
                        Meta = g.Sum(x => x.Meta)
                    })
                    .ToList();

                var equipos = equiposSp.Select(x =>
                {
                    var meta = metasEquipos.FirstOrDefault(m => m.Equipo == x.Equipo)?.Meta ?? 0;
                    var diferencia = x.Total - meta;

                    return new
                    {
                        equipo = x.Equipo,
                        facturas = x.Facturas,
                        pedidos = x.Pedidos,
                        entregas = x.Entregas,
                        proyeccionCierre = x.Total,
                        metaTotalEquipo = meta,
                        diferenciaProyectada = diferencia,
                        cumplimientoPct = meta > 0 ? Math.Round((x.Total / meta) * 100, 2) : 0,
                        estado = diferencia >= 0 ? "Cumplido" : "Oportunidad"
                    };
                }).OrderByDescending(x => x.proyeccionCierre).ToList();

                var vendedores = vendedoresSp.Select(x =>
                {
                    var meta = metasVendedores
                        .FirstOrDefault(m =>
                            m.Equipo == x.Equipo &&
                            m.Vendedor.Trim().ToUpper() == (x.Vendedor ?? "").Trim().ToUpper()
                        )?.Meta ?? 0;

                    var diferencia = x.Total - meta;

                    return new
                    {
                        year = x.YEAR,
                        mes = x.Mes,
                        equipo = x.Equipo,
                        div = x.Div,
                        vendedor = x.Vendedor,
                        facturas = x.Facturas,
                        pedidos = x.Pedidos,
                        entregas = x.Entregas,
                        proyeccionCierre = x.Total,
                        metaTotalVendedor = meta,
                        diferenciaProyectada = diferencia,
                        cumplimientoPct = meta > 0 ? Math.Round((x.Total / meta) * 100, 2) : 0,
                        estado = diferencia >= 0 ? "Cumplido" : "Oportunidad"
                    };
                }).OrderBy(x => x.equipo).ThenByDescending(x => x.proyeccionCierre).ToList();

                var metaTotal = metasEquipos.Sum(x => x.Meta);
                var totalProyectado = totalesSp?.Total ?? 0;
                var diferenciaTotal = totalProyectado - metaTotal;

                return Ok(new
                {
                    ok = true,
                    mensaje = "Proyección de cierre obtenida correctamente.",
                    data = new
                    {
                        kpis = new
                        {
                            facturas = totalesSp?.Facturas ?? 0,
                            pedidos = totalesSp?.Pedidos ?? 0,
                            entregas = totalesSp?.Entregas ?? 0,
                            proyeccionCierre = totalProyectado,
                            metaTotal,
                            diferenciaProyectada = diferenciaTotal,
                            cumplimientoPct = metaTotal > 0 ? Math.Round((totalProyectado / metaTotal) * 100, 2) : 0
                        },
                        equipos,
                        vendedores
                    }
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "SQL Error al obtener proyección de cierre",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al obtener proyección de cierre",
                    error = ex.Message
                });
            }
        }












        // ============================================================
        // NUEVO: Cierre Semanal Gerencia (DETALLE por vendedor)
        // GET /api/gerencia/cierre-semanal-detalle?anio=2026&mes=1
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("cierre-semanal-detalle")]
        public async Task<IActionResult> GetCierreSemanalDetalle([FromQuery] int anio, [FromQuery] int mes)
        {
            if (anio < 2000 || anio > 2100) return BadRequest("Año inválido.");
            if (mes < 1 || mes > 12) return BadRequest("Mes inválido.");

            const string acctCode = "40101001";

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                var dtCal = new DataTable();
                await using (var cmdCal = new SqlCommand(@"
SELECT Anio, Mes, Semana, FechaCierre, VisibleDias
FROM dbo.ApiCalendarioCierreSemanal
WHERE Anio = @Anio AND Mes = @Mes
ORDER BY Semana;", conn))
                {
                    cmdCal.Parameters.Add(new SqlParameter("@Anio", SqlDbType.Int) { Value = anio });
                    cmdCal.Parameters.Add(new SqlParameter("@Mes", SqlDbType.Int) { Value = mes });
                    using var da = new SqlDataAdapter(cmdCal);
                    da.Fill(dtCal);
                }

                var dtVend = new DataTable();
                await using (var cmdV = new SqlCommand(@"
SELECT
    S.SlpCode,
    S.SlpName,
    ISNULL(CAST(S.U_Zona AS nvarchar(100)), '') AS ZonaChile,
    '' AS Gerencia,
    '' AS Supervisor
FROM OSLP S
WHERE S.Active = 'Y'
  AND S.U_Centro_Costo IS NOT NULL
  AND LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) <> ''
  AND ISNUMERIC(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50))))) = 1
  AND CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) IN (10,30,40,50)
ORDER BY S.SlpName;", conn))
                {
                    using var daV = new SqlDataAdapter(cmdV);
                    daV.Fill(dtVend);
                }

                var detalle = new DataTable();
                detalle.Columns.Add("YEAR", typeof(int));
                detalle.Columns.Add("Mes", typeof(string));
                detalle.Columns.Add("Semana", typeof(int));
                detalle.Columns.Add("FechaCierre", typeof(DateTime));
                detalle.Columns.Add("ZonaChile", typeof(string));
                detalle.Columns.Add("Gerencia", typeof(string));
                detalle.Columns.Add("Supervisor", typeof(string));
                detalle.Columns.Add("Vendedor", typeof(string));
                detalle.Columns.Add("SlpCode", typeof(int));
                detalle.Columns.Add("Facturas", typeof(decimal));
                detalle.Columns.Add("Pedidos", typeof(decimal));
                detalle.Columns.Add("Entregas", typeof(decimal));
                detalle.Columns.Add("Total", typeof(decimal));

                await using var cmdSP = new SqlCommand("dbo.SP_Dashboard_CierreSemanal_Vendedor", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                var pSlp = cmdSP.Parameters.Add("@SlpCode", SqlDbType.Int);
                var pDesde = cmdSP.Parameters.Add("@Desde", SqlDbType.Date);
                var pHasta = cmdSP.Parameters.Add("@Hasta", SqlDbType.Date);
                cmdSP.Parameters.Add(new SqlParameter("@AcctCode", SqlDbType.VarChar, 20) { Value = acctCode });

                using var daSP = new SqlDataAdapter(cmdSP);

                foreach (DataRow w in dtCal.Rows)
                {
                    var fechaCierre = Convert.ToDateTime(w["FechaCierre"]);
                    var visible = Convert.ToInt32(w["VisibleDias"]);

                    var hasta = fechaCierre.Date;
                    var desde = fechaCierre.AddDays(-(visible - 1)).Date;

                    foreach (DataRow v in dtVend.Rows)
                    {
                        int slpCode = Convert.ToInt32(v["SlpCode"]);
                        string vendedor = v["SlpName"]?.ToString() ?? "";
                        string zona = v["ZonaChile"]?.ToString() ?? "";
                        string ger = v["Gerencia"]?.ToString() ?? "";
                        string sup = v["Supervisor"]?.ToString() ?? "";

                        pSlp.Value = slpCode;
                        pDesde.Value = desde;
                        pHasta.Value = hasta;

                        var dt = new DataTable();
                        daSP.Fill(dt);

                        decimal fac = 0, ped = 0, ent = 0, tot = 0;
                        if (dt.Rows.Count > 0)
                        {
                            var r = dt.Rows[0];
                            fac = r["Factura"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Factura"]);
                            ped = r["Pedido"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Pedido"]);
                            ent = r["Entrega"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Entrega"]);
                            tot = r["Total"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Total"]);
                        }

                        if (fac == 0 && ped == 0 && ent == 0 && tot == 0) continue;

                        detalle.Rows.Add(
                            anio,
                            new DateTime(anio, mes, 1).ToString("MMMM", new CultureInfo("es-CL")),
                            Convert.ToInt32(w["Semana"]),
                            fechaCierre.Date,
                            zona, ger, sup,
                            vendedor, slpCode,
                            fac, ped, ent, tot
                        );
                    }
                }

                decimal totalFact = 0, totalPed = 0, totalEnt = 0, totalAll = 0;
                foreach (DataRow r in detalle.Rows)
                {
                    totalFact += Convert.ToDecimal(r["Facturas"]);
                    totalPed += Convert.ToDecimal(r["Pedidos"]);
                    totalEnt += Convert.ToDecimal(r["Entregas"]);
                    totalAll += Convert.ToDecimal(r["Total"]);
                }

                return Ok(new
                {
                    anio,
                    mes,
                    vendedoresConsiderados = dtVend.Rows.Count,
                    semanas = dtCal.Rows.Count,
                    totales = new
                    {
                        facturas = totalFact,
                        pedidos = totalPed,
                        entregas = totalEnt,
                        total = totalAll
                    },
                    detalle = ToList(detalle)
                });
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // 5) NUEVO: Cierre Semanal Gerencia (desde calendario) - Químicos por defecto
        // GET /api/gerencia/cierre-semanal-gerencia?anio=2026&mes=1&acctCode=40101001
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("cierre-semanal-gerencia")]
        public async Task<IActionResult> GetCierreSemanalGerencia([FromQuery] int anio, [FromQuery] int mes, [FromQuery] string? acctCode = "40101001")
        {
            if (anio <= 2000 || anio >= 2100) return BadRequest("Año inválido.");
            if (mes < 1 || mes > 12) return BadRequest("Mes inválido.");

            acctCode = (acctCode ?? "40101001").Trim();

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                var dtCal = new DataTable();
                await using (var cmdCal = new SqlCommand(@"
SELECT Anio, Mes, Semana, FechaCierre, VisibleDias
FROM dbo.ApiCalendarioCierreSemanal
WHERE Anio = @Anio AND Mes = @Mes
ORDER BY Semana;", conn))
                {
                    cmdCal.CommandTimeout = 120;
                    cmdCal.Parameters.Add(new SqlParameter("@Anio", SqlDbType.Int) { Value = anio });
                    cmdCal.Parameters.Add(new SqlParameter("@Mes", SqlDbType.Int) { Value = mes });

                    using var da = new SqlDataAdapter(cmdCal);
                    da.Fill(dtCal);
                }

                var dtVendedores = new DataTable();
                await using (var cmdV = new SqlCommand(@"
SELECT S.SlpCode
FROM OSLP S
WHERE S.Active = 'Y'
  AND S.U_Centro_Costo IS NOT NULL
  AND LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) <> ''
  AND ISNUMERIC(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50))))) = 1
  AND CAST(LTRIM(RTRIM(CAST(S.U_Centro_Costo AS varchar(50)))) AS int) IN (10,30,40,50);", conn))
                {
                    cmdV.CommandTimeout = 120;
                    using var daV = new SqlDataAdapter(cmdV);
                    daV.Fill(dtVendedores);
                }

                var salida = new List<Dictionary<string, object?>>();
                decimal acumFactura = 0;

                foreach (DataRow w in dtCal.Rows)
                {
                    int semana = Convert.ToInt32(w["Semana"]);
                    DateTime fechaCierre = Convert.ToDateTime(w["FechaCierre"]).Date;
                    int visibleDias = Convert.ToInt32(w["VisibleDias"]);

                    DateTime hasta = fechaCierre;
                    DateTime desde = fechaCierre.AddDays(-(visibleDias - 1));

                    decimal sumFactura = 0, sumPedido = 0, sumEntrega = 0, sumTotal = 0;

                    foreach (DataRow v in dtVendedores.Rows)
                    {
                        int slpCode = Convert.ToInt32(v["SlpCode"]);

                        var dt = new DataTable();
                        await using var cmd = new SqlCommand("dbo.SP_Dashboard_CierreSemanal_Vendedor", conn)
                        {
                            CommandType = CommandType.StoredProcedure,
                            CommandTimeout = 120
                        };

                        cmd.Parameters.Add(new SqlParameter("@SlpCode", SqlDbType.Int) { Value = slpCode });
                        cmd.Parameters.Add(new SqlParameter("@Desde", SqlDbType.Date) { Value = desde });
                        cmd.Parameters.Add(new SqlParameter("@Hasta", SqlDbType.Date) { Value = hasta });
                        cmd.Parameters.Add(new SqlParameter("@AcctCode", SqlDbType.VarChar, 20)
                        {
                            Value = string.IsNullOrWhiteSpace(acctCode) ? DBNull.Value : acctCode
                        });

                        using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);

                        if (dt.Rows.Count > 0)
                        {
                            var r = dt.Rows[0];
                            sumFactura += r["Factura"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Factura"]);
                            sumPedido += r["Pedido"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Pedido"]);
                            sumEntrega += r["Entrega"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Entrega"]);
                            sumTotal += r["Total"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Total"]);
                        }
                    }

                    acumFactura += sumFactura;

                    salida.Add(new Dictionary<string, object?>
                    {
                        ["Anio"] = anio,
                        ["Mes"] = mes,
                        ["Semana"] = semana,
                        ["PeriodoKey"] = $"{anio}-{mes:00}-W{semana:00}",
                        ["FechaCierre"] = fechaCierre,
                        ["VisibleDias"] = visibleDias,
                        ["Desde"] = desde,
                        ["Hasta"] = hasta,
                        ["Factura"] = sumFactura,
                        ["Pedido"] = sumPedido,
                        ["Entrega"] = sumEntrega,
                        ["Total"] = sumTotal,
                        ["AcumFactura"] = acumFactura
                    });
                }

                return Ok(new
                {
                    anio,
                    mes,
                    acctCode,
                    semanas = salida,
                    vendedoresConsiderados = dtVendedores.Rows.Count
                });
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }

        // ============================================================
        // CIERRE MENSUAL GERENCIA
        // GET /api/gerencia/cierre-mensual-gerencia?anio=2026&mes=1
        // ============================================================
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("cierre-mensual-gerencia")]
        public async Task<IActionResult> GetCierreMensualGerencia([FromQuery] int anio, [FromQuery] int mes)
        {
            if (anio <= 2000 || anio >= 2100) return BadRequest("Año inválido.");
            if (mes < 1 || mes > 12) return BadRequest("Mes inválido.");

            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync();

                await using var cmd = new SqlCommand("dbo.SP_GER_CierreMensual_Categorias", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                cmd.Parameters.Add(new SqlParameter("@Anio", SqlDbType.Int) { Value = anio });
                cmd.Parameters.Add(new SqlParameter("@Mes", SqlDbType.Int) { Value = mes });

                var dt = new DataTable();
                using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);

                decimal sum(string col)
                {
                    decimal acc = 0;
                    foreach (DataRow r in dt.Rows)
                        acc += r[col] == DBNull.Value ? 0 : Convert.ToDecimal(r[col]);
                    return acc;
                }

                var kpis = new
                {
                    quimicos = sum("Quimicos"),
                    accesorios = sum("Accesorios"),
                    maquinas = sum("Maquinas"),
                    repuestosMaquinas = sum("Repuestos Maquinas"),
                    servicioTecnico = sum("Servicio Tecnico"),
                    otrasVentas = sum("Otras Ventas"),
                    totalGeneral = sum("Total general")
                };

                return Ok(new { kpis, rows = ToList(dt) });
            }
            catch (SqlException ex) { return StatusCode(500, "SQL Error: " + ex.Message); }
            catch (Exception ex) { return StatusCode(500, "Error: " + ex.Message); }
        }


       


        // ------------------------------------------------------------
        // GET: /api/gerencia/clientes-nuevos?desde=YYYY-MM-DD&hasta=YYYY-MM-DD
        // Endpoint  Retorna: { resumen: [...], detalle: [...] }
        // ------------------------------------------------------------
        // ⚠️ Si este informe es SOLO gerencia:
        [Authorize(Roles = "GERENCIA,ADMIN")]
        [HttpGet("clientes-nuevos")]
        public async Task<IActionResult> GetClientesNuevos([FromQuery] DateTime desde, [FromQuery] DateTime hasta)
        {
            if (desde == default || hasta == default)
                return BadRequest("Parámetros inválidos. Debes enviar 'desde' y 'hasta' (YYYY-MM-DD).");

            if (hasta < desde)
                return BadRequest("'hasta' no puede ser menor que 'desde'.");

            const string sql = @"
SET NOCOUNT ON;

;WITH Docs AS (
    SELECT 'FE' AS TipoDoc,
           T0.DocEntry, T0.DocDate, T0.FolioPref, T0.FolioNum,
           T0.CardCode, T0.CardName, T0.LicTradNum, T0.SlpCode
    FROM OINV T0
    WHERE T0.DocType = 'I'
      AND T0.Series IN ('42','47')
      AND T0.DocDate >= @Desde AND T0.DocDate <= @Hasta

    UNION ALL

    SELECT 'NC' AS TipoDoc,
           T0.DocEntry, T0.DocDate, T0.FolioPref, T0.FolioNum,
           T0.CardCode, T0.CardName, T0.LicTradNum, T0.SlpCode
    FROM ORIN T0
    WHERE T0.DocType = 'I'
      AND T0.DocDate >= @Desde AND T0.DocDate <= @Hasta
),
Lines AS (
    SELECT
        D.TipoDoc,
        D.DocEntry,
        D.DocDate,
        D.FolioPref,
        D.FolioNum,
        D.LicTradNum,
        D.CardCode,
        D.CardName,
        D.SlpCode,
        L.ItemCode,
        L.Dscription,
        CASE WHEN D.TipoDoc='NC' THEN L.Quantity   * -1 ELSE L.Quantity   END AS Cantidad,
        CASE WHEN D.TipoDoc='NC' THEN L.PriceBefDi * -1 ELSE L.PriceBefDi END AS Precio,
        CASE WHEN D.TipoDoc='NC' THEN L.LineTotal  * -1 ELSE L.LineTotal  END AS Total
    FROM Docs D
    INNER JOIN (
        SELECT DocEntry, ItemCode, Dscription, Quantity, PriceBefDi, LineTotal, AcctCode FROM INV1
        UNION ALL
        SELECT DocEntry, ItemCode, Dscription, Quantity, PriceBefDi, LineTotal, AcctCode FROM RIN1
    ) L ON L.DocEntry = D.DocEntry
    WHERE L.AcctCode = '40101001'
      AND (L.ItemCode LIKE 'PTI%' OR L.ItemCode LIKE 'PTN%' OR L.ItemCode LIKE 'PTQ%' OR L.ItemCode LIKE 'PTS%')
),
Filtrado AS (
    SELECT DISTINCT
        L.TipoDoc,
        L.DocDate,
        L.FolioPref,
        L.FolioNum,
        L.ItemCode,
        L.Dscription,
        L.Cantidad,
        L.Precio,
        L.Total,
        L.LicTradNum,
        L.CardCode,
        L.CardName,
        L.SlpCode,
        B.CreateDate AS FechaCreacion
    FROM Lines L
    INNER JOIN OCRD B ON B.CardCode = L.CardCode
    WHERE B.CreateDate >= @Desde AND B.CreateDate <= @Hasta
      AND (
            (LEN(B.CardCode)=11 AND B.CardCode LIKE 'C[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9K]')
         OR (LEN(B.CardCode)=10 AND B.CardCode LIKE 'C[0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9K]')
      )
      AND NOT EXISTS (
            SELECT 1
            FROM OCRD X
            WHERE
                (
                    (LEN(B.CardCode)=11 AND LEFT(X.CardCode,11)=B.CardCode AND LEN(X.CardCode) > 11)
                 OR (LEN(B.CardCode)=10 AND LEFT(X.CardCode,10)=B.CardCode AND LEN(X.CardCode) > 10)
                )
      )
)
SELECT *
INTO #Filtrado
FROM Filtrado;

-- 1) RESUMEN (con TotalVentas neto con signo)
SELECT
    S.SlpName AS EmpleadoVentas,
    COUNT(DISTINCT F.CardCode) AS ClientesNuevos,
    SUM(F.Total) AS TotalVentas
FROM #Filtrado F
INNER JOIN OSLP S ON S.SlpCode = F.SlpCode
GROUP BY S.SlpName
ORDER BY ClientesNuevos DESC, EmpleadoVentas;

-- 2) DETALLE
SELECT
    S.SlpName        AS EmpleadoVentas,
    F.TipoDoc        AS Docto,
    F.FolioNum       AS NFolio,
    F.DocDate        AS FechaContabliz,
    F.ItemCode       AS CodigoArticulo,
    F.Dscription     AS Articulo,
    F.Cantidad       AS Cantidad,
    F.Precio         AS Precio,
    F.Total          AS Total,
    F.LicTradNum     AS Rut,
    F.CardCode       AS CodigoCliente,
    F.CardName       AS Cliente,
    F.FechaCreacion  AS FechaCreacion
FROM #Filtrado F
INNER JOIN OSLP S ON S.SlpCode = F.SlpCode
ORDER BY EmpleadoVentas, FechaContabliz;

DROP TABLE #Filtrado;
";




            var args = new { Desde = desde.Date, Hasta = hasta.Date };

            await using var cn = new SqlConnection(ConnStr);
            await cn.OpenAsync();

            using var multi = await cn.QueryMultipleAsync(sql, args, commandType: CommandType.Text);

            var resumen = (await multi.ReadAsync<ClientesNuevosResumenRow>()).ToList();
            var detalle = (await multi.ReadAsync<ClientesNuevosDetalleRow>()).ToList();

            return Ok(new { resumen, detalle });
        }


        [Authorize(Roles = "ADMIN")]
        [HttpPost("cerrar-mes")]
        public async Task<IActionResult> CerrarMes(
            [FromQuery] int anio,
            [FromQuery] int mes,
            [FromQuery] string categoriaVenta = "Ventas Quimicos",
            [FromQuery] string? division = null,
            [FromQuery] bool forzarReproceso = false,
            [FromQuery] string? observacion = null)
        {
            try
            {
                using var connection = new SqlConnection(_config.GetConnectionString("SAP"));

                var usuario = User?.Identity?.Name ?? "ADMIN";

                var data = await connection.QueryAsync(
                    "dbo.sp_BI_GenerarCierreMensualGerencial",
                    new
                    {
                        Anio = anio,
                        Mes = mes,
                        CategoriaVenta = categoriaVenta,
                        Division = string.IsNullOrWhiteSpace(division) || division == "Todas" ? null : division,
                        UsuarioAccion = usuario,
                        ForzarReproceso = forzarReproceso,
                        Observacion = observacion
                    },
                    commandType: CommandType.StoredProcedure
                );

                return Ok(new
                {
                    ok = true,
                    mensaje = forzarReproceso
                        ? "Cierre mensual reprocesado correctamente."
                        : "Cierre mensual generado correctamente.",
                    data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    ok = false,
                    mensaje = "Error al generar/reprocesar el cierre mensual.",
                    detalle = ex.Message
                });
            }
        }

        
        [HttpGet("cierre-mensual")]
        public async Task<IActionResult> GetCierreMensual(
            [FromQuery] int anio,
            [FromQuery] int mes,
            [FromQuery] string categoriaVenta = "Ventas Quimicos",
            [FromQuery] string? division = null)
        {
            try
            {
                using var connection = new SqlConnection(_config.GetConnectionString("SAP"));

                var sql = @"
                            SELECT
                                Anio,
                                Mes,
                                CategoriaVenta,
                                Division,
                                Equipo,
                                Venta,
                                Meta,
                                CumplimientoPct,
                                Brecha,
                                Estado,
                                EsReproceso,
                                FechaCierre,
                                UsuarioCierre,
                                FechaReproceso,
                                UsuarioReproceso,
                                Observacion
                            FROM dbo.BI_CierreMensualGerencial
                            WHERE Anio = @Anio
                              AND Mes = @Mes
                              AND CategoriaVenta = @CategoriaVenta
                              AND ISNULL(Division, '') = ISNULL(@Division, '')
                            ORDER BY Venta DESC;
                        ";

                var data = await connection.QueryAsync(sql, new
                {
                    Anio = anio,
                    Mes = mes,
                    CategoriaVenta = categoriaVenta,
                    Division = string.IsNullOrWhiteSpace(division) || division == "Todas" ? null : division
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
                    mensaje = "Error al consultar cierre mensual.",
                    detalle = ex.Message
                });
            }
        }













        // ==================== DTOs (salida JSON) ===========================
        public sealed class ClientesNuevosResumenRow
        {
            public string EmpleadoVentas { get; set; } = "";
            public int ClientesNuevos { get; set; }
            public decimal TotalVentas { get; set; }   // ⭐ NUEVO
        }

        public sealed class ClientesNuevosDetalleRow
        {
            public string EmpleadoVentas { get; set; } = "";
            public string Docto { get; set; } = "";
            public int? NFolio { get; set; }
            public DateTime? FechaContabliz { get; set; }
            public string CodigoArticulo { get; set; } = "";
            public string Articulo { get; set; } = "";
            public decimal Cantidad { get; set; }
            public decimal Precio { get; set; }
            public decimal Total { get; set; }
            public string Rut { get; set; } = "";
            public string CodigoCliente { get; set; } = "";
            public string Cliente { get; set; } = "";
            public DateTime? FechaCreacion { get; set; }
        }


        public sealed class ProyeccionCierreTotalesDto
        {
            public decimal Facturas { get; set; }
            public decimal Pedidos { get; set; }
            public decimal Entregas { get; set; }
            public decimal Total { get; set; }
        }

        public sealed class ProyeccionCierreEquipoBaseDto
        {
            public string Equipo { get; set; } = "";
            public decimal Facturas { get; set; }
            public decimal Pedidos { get; set; }
            public decimal Entregas { get; set; }
            public decimal Total { get; set; }
        }

        public sealed class ProyeccionCierreVendedorBaseDto
        {
            public int YEAR { get; set; }
            public string Mes { get; set; } = "";
            public string Equipo { get; set; } = "";
            public string Div { get; set; } = "";
            public string Vendedor { get; set; } = "";
            public decimal Facturas { get; set; }
            public decimal Pedidos { get; set; }
            public decimal Entregas { get; set; }
            public decimal Total { get; set; }
        }

        public sealed class MetaVendedorDto
        {
            public string Equipo { get; set; } = "";
            public string Vendedor { get; set; } = "";
            public decimal Meta { get; set; }
        }







    }



}
