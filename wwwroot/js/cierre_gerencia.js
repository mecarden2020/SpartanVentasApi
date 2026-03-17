// wwwroot/js/cierre_gerencia.js
(() => {
    "use strict";
    // ==================  inicializando el analizador de typescript ==============================
    const COLORS_RANKING = {
        facturas: "#0d6efd", // Azul
        pedidos: "#dc3545",  // Rojo
        entregas: "#fd7e14", // Naranja
        total: "#f2b705"     // Dorado (ajustado)
    };

    // Helper sin $ para evitar choques
    const el = (id) => document.getElementById(id);

    // ================== SCOPE (ALL / FOOD / HC) ==================
    // Se define en el HTML: window.GERENCIA_SCOPE = "FOOD" | "HC" | "ALL"
    const GERENCIA_SCOPE = (window.GERENCIA_SCOPE || "ALL").toString().toUpperCase();

    // FOOD = FB + IN
    function getDivisionList(scope) {
        if (scope === "FOOD") return "FB,IN";
        if (scope === "HC") return "HC";
        return "ALL";
    }


    const fmt = new Intl.NumberFormat("es-CL", {
        style: "currency",
        currency: "CLP",
        maximumFractionDigits: 0
    });

    let chartMain = null;     // gráfico del bloque "CHART" (mensual)
    let chartRanking = null;  // gráfico del ranking semanal
    let lastExport = { headers: [], rows: [], filename: "export.csv" };

    function showRanking(show) {
        const wrap = el("rankingWrap");
        if (wrap) wrap.style.display = show ? "" : "none";
    }

    function showKpi7(show) {
        const wrap = el("kpi7Wrap");
        if (wrap) wrap.style.display = show ? "" : "none";
    }

    

    function limpiarUI() {
        // Limpia cards
        document.querySelectorAll(".kpi-value").forEach(x => x.textContent = "—");
        // Limpia tabla
        document.getElementById("tbodyCierre").innerHTML = "";
        // Limpia mensajes
        document.getElementById("msgError").textContent = "";
    }




    // ✅ llena las 7 cards
    function setKPIs7(t = {}) {
        const setMoney = (id, val) => {
            const node = el(id);
            if (node) node.textContent = fmt.format(Number(val || 0));
        };

        setMoney("kpiQ", t.quimicos);
        setMoney("kpiA", t.accesorios);
        setMoney("kpiM", t.maquinas);
        setMoney("kpiRM", t.repuestosMaquinas);
        setMoney("kpiST", t.servicioTecnico);
        setMoney("kpiOV", t.otrasVentas);
        setMoney("kpiTG", t.totalGeneral);
    }





    function fmtCLP(n) {
        return (n ?? 0).toLocaleString("es-CL", { style: "currency", currency: "CLP", maximumFractionDigits: 0 });
    }

    function renderTablaMensual(rows) {
        const tb = document.getElementById("tbodyCierre");
        tb.innerHTML = "";

        for (const r of rows) {
            const tr = document.createElement("tr");
            tr.innerHTML = `
      <td>${esc(r.vendedor)}</td>
      <td class="text-end">${fmtCLP(r.quimicos)}</td>
      <td class="text-end">${fmtCLP(r.accesorios)}</td>
      <td class="text-end">${fmtCLP(r.maquinas)}</td>
      <td class="text-end">${fmtCLP(r.repuestosMaquinas)}</td>
      <td class="text-end">${fmtCLP(r.servicioTecnico)}</td>
      <td class="text-end">${fmtCLP(r.otrasVentas)}</td>
      <td class="text-end fw-semibold">${fmtCLP(r.totalGeneral)}</td>
    `;
            tb.appendChild(tr);
        }
    }

    function esc(s) { return (s ?? "").toString().replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;"); }

    function habilitarExportExcelMensual(rows) {
        const btn = document.getElementById("btnExportExcel");
        btn.onclick = () => exportCSV(rows);
    }

    function exportCSV(rows) {
        const header = ["Vendedor", "Quimicos", "Accesorios", "Maquinas", "Repuestos Maquinas", "Servicio Tecnico", "Otras Ventas", "Total general"];
        const lines = [header.join(";")];

        for (const r of rows) {
            lines.push([
                r.vendedor,
                r.quimicos, r.accesorios, r.maquinas, r.repuestosMaquinas, r.servicioTecnico, r.otrasVentas, r.totalGeneral
            ].join(";"));
        }

        const blob = new Blob([lines.join("\n")], { type: "text/csv;charset=utf-8" });
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = "CierreMensual.csv";
        a.click();
        URL.revokeObjectURL(a.href); 
    }




    function setEstado(txt) {
        const e = el("estado");
        if (e) e.textContent = txt || "";
    }

    function setChips(modo, anio, mes) {
        const cm = el("chipModo");
        const cp = el("chipPeriodo");
        if (cm) cm.textContent = modo === "semanal" ? "Semanal" : "Mensual";
        if (cp) cp.textContent = `${anio}-${String(mes).padStart(2, "0")}`;
    }

    function setKPIs(o) {
        const set = (id, v) => {
            const x = el(id);
            if (x) x.textContent = v ?? "—";
        };
        set("kpi1Title", o.k1t); set("kpi1Val", o.k1v); set("kpi1Sub", o.k1s);
        set("kpi2Title", o.k2t); set("kpi2Val", o.k2v); set("kpi2Sub", o.k2s);
        set("kpi3Title", o.k3t); set("kpi3Val", o.k3v); set("kpi3Sub", o.k3s);
    }

    function showKpis7(show) {
        const wrap = el("kpis7Wrap");
        if (wrap) wrap.style.display = show ? "" : "none";
    }

   

    function destroyCharts() {
        if (chartMain) chartMain.destroy();
        chartMain = null;

        if (chartRanking) chartRanking.destroy();
        chartRanking = null;
    }
    //===============  suma linea por vendedor ==================================
    function normalizarFilaMensual(r = {}) {
        const out = {
            Vendedor: r.Vendedor ?? r.vendedor ?? r["Vendedor"] ?? "—",
            Quimicos: r.Quimicos ?? r.quimicos ?? r["Químicos"] ?? 0,
            Accesorios: r.Accesorios ?? r.accesorios ?? 0,
            Maquinas: r.Maquinas ?? r.maquinas ?? 0,
            RepuestosMaquinas: r.RepuestosMaquinas ?? r.repuestosMaquinas ?? r["Repuestos Maquinas"] ?? r["Repuestos Máquinas"] ?? 0,
            ServicioTecnico: r.ServicioTecnico ?? r.servicioTecnico ?? r["Servicio Tecnico"] ?? r["Servicio Técnico"] ?? 0,
            OtrasVentas: r.OtrasVentas ?? r.otrasVentas ?? r["Otras Ventas"] ?? 0,
            TotalGeneral: r.TotalGeneral ?? r.totalGeneral ?? r["Total general"] ?? r["Total General"] ?? 0,
        };

        // ✅ si no viene TotalGeneral (o viene 0), lo calculamos desde las categorías
        const tg = Number(out.TotalGeneral || 0);
        if (!tg) {
            out.TotalGeneral =
                Number(out.Quimicos || 0) +
                Number(out.Accesorios || 0) +
                Number(out.Maquinas || 0) +
                Number(out.RepuestosMaquinas || 0) +
                Number(out.ServicioTecnico || 0) +
                Number(out.OtrasVentas || 0);
        }

        return out;
    }



    // ==============================================================
    

    function renderTable(headers, rows) {
        const thead = el("thead");
        const table = el("tabla");
        const tbody = table ? table.querySelector("tbody") : null;
        if (!thead || !tbody) return;

        thead.innerHTML = `<tr>${headers
            .map((h) => `<th class="${h.align || ""}">${h.title}</th>`)
            .join("")}</tr>`;

        tbody.innerHTML = "";

        for (const r of rows) {
            const tr = document.createElement("tr");
            tr.innerHTML = headers
                .map((h) => {
                    const val = r[h.key];
                    if (h.fmt === "money")
                        return `<td class="text-end">${fmt.format(Number(val || 0))}</td>`;
                    if (h.fmt === "date")
                        return `<td>${val ? new Date(val).toISOString().slice(0, 10) : ""}</td>`;
                    return `<td class="${h.align || ""}">${val ?? ""}</td>`;
                })
                .join("");
            tbody.appendChild(tr);
        }
    }

    function downloadXLSX(filename, rows, headers) {
        if (typeof XLSX === "undefined") {
            alert("No se cargó la librería XLSX. Revisa el <script> en el HTML.");
            return;
        }

        // Convertir rows + headers a array de objetos con títulos “bonitos”
        const data = rows.map(r => {
            const o = {};
            for (const h of headers) o[h.title] = r[h.key];
            return o;
        });

        const ws = XLSX.utils.json_to_sheet(data);

        // Auto ancho de columnas (simple)
        ws["!cols"] = headers.map(h => ({ wch: Math.max(12, String(h.title).length + 2) }));

        const wb = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(wb, ws, "Reporte");

        // Descargar
        const safeName = filename.toLowerCase().endsWith(".xlsx") ? filename : `${filename}.xlsx`;
        XLSX.writeFile(wb, safeName);
    }



    function downloadCSV(filename, rows, headers) {
        const esc = (v) => `"${String(v ?? "").replace(/"/g, '""')}"`;
        const csv = [
            headers.map((h) => esc(h.title)).join(","),
            ...rows.map((r) => headers.map((h) => esc(r[h.key])).join(",")),
        ].join("\n");

        const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    // ===== Leyenda tipo chips para ranking =====
    function renderRankingLegend(containerId, colors) {
        const node = el(containerId);
        if (!node) return;

        node.innerHTML = `
      <div class="d-flex gap-3 align-items-center" style="font-size:0.9rem;">
        <div><span class="legend-box" style="background:${colors.fact}"></span> Facturas</div>
        <div><span class="legend-box" style="background:${colors.ped}"></span> Pedidos</div>
        <div><span class="legend-box" style="background:${colors.ent}"></span> Entregas</div>
        <div><span class="legend-box" style="background:${colors.tot}"></span> Total</div>
      </div>
    `;
    }

    // ===== Ranking horizontal: SOLO barra Total visible + tooltip con 4 líneas coloreadas =====
    function buildRankingHorizontal(canvas, labels, datasets) {
        // 1) Buscar datasets originales
        const dsFact = datasets.find(d => (d.label || "").toLowerCase() === "facturas");
        const dsPed = datasets.find(d => (d.label || "").toLowerCase() === "pedidos");
        const dsEnt = datasets.find(d => (d.label || "").toLowerCase() === "entregas");
        const dsTot = datasets.find(d => (d.label || "").toLowerCase() === "total")
            || datasets[datasets.length - 1];

        // 2) Extraer data como número
        const fact = (dsFact?.data || []).map(Number);
        const ped = (dsPed?.data || []).map(Number);
        const ent = (dsEnt?.data || []).map(Number);
        const tot = (dsTot?.data || []).map(Number);

        // 3) Colores
        const COLORS = {
            fact: dsFact?.backgroundColor || COLORS_RANKING.facturas,
            ped: dsPed?.backgroundColor || COLORS_RANKING.pedidos,
            ent: dsEnt?.backgroundColor || COLORS_RANKING.entregas,
            tot: dsTot?.backgroundColor || COLORS_RANKING.total
        };

        // 4) Chips/leyenda externa
        renderRankingLegend("rankingLegend", COLORS);

        // 5) SOLO Total visible en el gráfico
        const onlyTotal = [{
            label: "Total",
            data: tot,
            backgroundColor: COLORS.tot,
            borderRadius: 6,
            barThickness: 12
        }];

        // 6) Chart
        return new Chart(canvas, {
            type: "bar",
            data: { labels, datasets: onlyTotal },

            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                animation: false,      //accelerar el tooltip
                hover: { animationDuration: 0 },   //accelerar el tooltip
                interaction: { mode: "nearest", intersect: false },

                plugins: {
                    legend: { display: false },
                 //=============== Plugin tooltip ======================
                    tooltip: {
                        enabled: false,
                        external: (context) => {
                            try {
                                const { chart, tooltip } = context;

                                // crear contenedor una vez
                                let tooltipEl = chart.canvas.parentNode.querySelector(".tt-external");
                                if (!tooltipEl) {
                                    tooltipEl = document.createElement("div");
                                    tooltipEl.className = "tt-external";
                                    tooltipEl.style.cssText = `
                                                              position:absolute; pointer-events:none; z-index:9999;
                                                              background: rgba(0,0,0,0.85);
                                                              color:#fff; border:1px solid rgba(255,255,255,0.15);
                                                              border-radius:8px; padding:10px;
                                                              font-size:12px; line-height:1.35;
                                                              box-shadow:0 8px 24px rgba(0,0,0,0.35);
                                                              transform: translate(-50%, -110%);
                                                              white-space:nowrap;
                                                              opacity:0;
                                                            `;
                                    chart.canvas.parentNode.style.position = "relative";
                                    chart.canvas.parentNode.appendChild(tooltipEl);
                                }

                                // ocultar
                                if (!tooltip || tooltip.opacity === 0) {
                                    tooltipEl.style.opacity = 0;
                                    return;
                                }

                                // dataIndex
                                const dp = tooltip.dataPoints?.[0];
                                const i = dp?.dataIndex ?? 0;

                                const title = labels?.[i] || "";

                                const row = (color, text) => `
                                                            <div style="display:flex; gap:8px; align-items:center; margin-top:2px;">
                                                              <span style="width:10px;height:10px;border-radius:2px;background:${color};display:inline-block;"></span>
                                                              <span>${text}</span>
                                                            </div>
                                                          `;

                                tooltipEl.innerHTML = `
                                                        <div style="font-weight:700; font-size:13px; margin-bottom:6px;">${title}</div>
                                                        ${row(COLORS.fact, `Facturas: ${fmt.format(fact[i] || 0)}`)}
                                                        ${row(COLORS.ped, `Pedidos: ${fmt.format(ped[i] || 0)}`)}
                                                        ${row(COLORS.ent, `Entregas: ${fmt.format(ent[i] || 0)}`)}
                                                        ${row(COLORS.tot, `Total: ${fmt.format(tot[i] || 0)}`)}
                                                      `;

                                // posición
                                const { offsetLeft: posX, offsetTop: posY } = chart.canvas;
                                tooltipEl.style.left = (posX + tooltip.caretX) + "px";
                                tooltipEl.style.top = (posY + tooltip.caretY) + "px";
                                tooltipEl.style.opacity = 1;
                                tooltipEl.style.transition = "opacity 0.05s linear";


                            } catch (e) {
                                // si algo falla, NO romper el tablero
                                console.warn("Tooltip externo error:", e);
                            }
                        }
                    }


                },

                scales: {
                    x: { ticks: { callback: (v) => fmt.format(v) } },
                    y: { ticks: { autoSkip: false } }
                }
            }
        });
    }




    function buildBar(canvas, labels, label, values) {
        return new Chart(canvas, {
            type: "bar",
            data: { labels, datasets: [{ label, data: values }] },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: (ctx) => `${ctx.dataset.label}: ${fmt.format(ctx.parsed.y)}`,
                        },
                    },
                },
                scales: { y: { ticks: { callback: (v) => fmt.format(v) } } },
            },
        });
    }

    // Fetch con JWT
    async function fetchJson(url) {
        const token = localStorage.getItem("jwtToken") || localStorage.getItem("token") || "";
        if (!token) {
            setEstado("Inicia sesión para cargar los cierres.");
            return null;
        }

        const resp = await fetch(url, {
            headers: { Authorization: `Bearer ${token}`, Accept: "application/json" },
        });

        if (resp.status === 401 || resp.status === 403) {
            setEstado("Sesión expirada o sin permisos (401/403). Vuelve a iniciar sesión.");
            return null;
        }

        if (!resp.ok) {
            const txt = await resp.text();
            throw new Error(txt || `HTTP ${resp.status}`);
        }

        return await resp.json();
    }

    function initDefaults() {
        const d = new Date();
        if (el("anio")) el("anio").value = String(d.getFullYear());
        if (el("mes")) el("mes").value = String(d.getMonth() + 1);
        if (el("modo")) el("modo").value = "semanal";
    }

    function showChart(show) {
        const wrap = el("chartWrap");
        if (wrap) wrap.style.display = show ? "" : "none";
    }

    async function cargar() {
        destroyCharts();
        setEstado("Cargando...");

        const anio = Number(el("anio")?.value || 0);
        const mes = Number(el("mes")?.value || 0);
        const modo = el("modo")?.value || "semanal";

        if (!anio || !mes) {
            setEstado("Selecciona Año y Mes.");
            return;
        }

        setChips(modo, anio, mes);

        try {
            // =====================================================================================
            // A) CIERRE SEMANAL (NO TOCAR: tu módulo ya validado)
            // =====================================================================================
            if (modo === "semanal") {
                showRanking(true);
                showChart(false);
                showKpi7(false);   // si existía, lo escondemos en semanal

                const qs = new URLSearchParams({
                    anio: String(anio),
                    mes: String(mes),
                    division: getDivisionList(GERENCIA_SCOPE)
                });

                const json = await fetchJson(`/api/gerencia/cierre-semanal-mtd?${qs.toString()}`);
                if (!json) return;

                const rows = json.detalle || [];
                const t = json.totales ?? {};

                setKPIs({
                    k1t: "Facturado Neto (Químicos)",
                    k1v: fmt.format(Number(t.facturas || 0)),
                    k1s: `Vendedores: ${json.vendedoresConMovimiento ?? 0} / ${json.vendedoresConsiderados ?? 0}`,
                    k2t: "Pedidos Neto (abiertos)",
                    k2v: fmt.format(Number(t.pedidos || 0)),
                    k2s: `Corte: ${String(json.hasta || "").slice(0, 10)}`,
                    k3t: "Entregas Neto (abiertas)",
                    k3v: fmt.format(Number(t.entregas || 0)),
                    k3s: `Total: ${fmt.format(Number(t.total || 0))}`,
                });

                if (el("tablaTitle")) el("tablaTitle").textContent = "Informe Ventas MTD (acumulado del mes a la fecha)";
                if (el("tablaHint")) {
                    el("tablaHint").textContent =
                        `Químicos – Facturas/Pedidos/Entregas/Total (desde ${json.desde?.slice?.(0, 10) || ""} hasta ${json.hasta?.slice?.(0, 10) || ""})`;
                }

                const headers = [
                    { title: "YEAR", key: "YEAR" },
                    { title: "Mes", key: "Mes" },
                    { title: "Zona Chile", key: "ZonaChile" },
                    { title: "Vendedor", key: "Vendedor" },
                    { title: "Facturas", key: "Facturas", fmt: "money" },
                    { title: "Pedidos", key: "Pedidos", fmt: "money" },
                    { title: "Entregas", key: "Entregas", fmt: "money" },
                    { title: "Total", key: "Total", fmt: "money" },
                ];
                renderTable(headers, rows);

                lastExport = {
                    headers,
                    rows,
                    filename: `CierreSemanal_${anio}-${String(mes).padStart(2, "0")}.xlsx`
                };
                const btnExp = el("btnExportCSV");
                if (btnExp) {
                    btnExp.style.display = "";
                    btnExp.textContent = "Exportar a Excel (XLSX)";
                    btnExp.onclick = () => downloadXLSX(lastExport.filename, lastExport.rows, lastExport.headers);
                }

                // Ranking Top 25 por TOTAL (MTD)
                const agg = new Map();
                for (const r of rows) {
                    const key = r.Vendedor || "—";
                    const fac = Number(r.Facturas || 0);
                    const ped = Number(r.Pedidos || 0);
                    const ent = Number(r.Entregas || 0);
                    const tot = Number(r.Total || 0);

                    if (!agg.has(key)) agg.set(key, { fac: 0, ped: 0, ent: 0, tot: 0 });
                    const o = agg.get(key);
                    o.fac += fac; o.ped += ped; o.ent += ent; o.tot += tot;
                }

                const arr = [...agg.entries()]
                    .map(([v, o]) => ({ v, ...o }))
                    .sort((a, b) => b.tot - a.tot)
                    .slice(0, 25);

                const rLabels = arr.map(x => x.v);
                const ds = [
                    { label: "Facturas", data: arr.map(x => x.fac), backgroundColor: COLORS_RANKING.facturas },
                    { label: "Pedidos", data: arr.map(x => x.ped), backgroundColor: COLORS_RANKING.pedidos },
                    { label: "Entregas", data: arr.map(x => x.ent), backgroundColor: COLORS_RANKING.entregas },
                    { label: "Total", data: arr.map(x => x.tot), backgroundColor: COLORS_RANKING.total }
                ];

                if (chartRanking) { chartRanking.destroy(); chartRanking = null; }
                const rankingCanvas = el("chartRankingVendedores");
                if (rankingCanvas) {
                    chartRanking = buildRankingHorizontal(rankingCanvas, rLabels, ds);
                    setTimeout(() => chartRanking?.resize(), 0);
                }

                if (chartMain) { chartMain.destroy(); chartMain = null; }

                setEstado(`OK • filas: ${rows.length} • vendedores: ${json.vendedoresConsiderados ?? 0}`);
                return;
            }

            // =====================================================================================
            // B) CIERRE MENSUAL (OPCIÓN A) -> /cierre-mensual-gerencia
            // SOLO: 7 CARDS + TABLA + EXCEL
            // =====================================================================================
            showRanking(false);
            showChart(false);
            showKpi7(true);

            if (chartRanking) { chartRanking.destroy(); chartRanking = null; }
            if (chartMain) { chartMain.destroy(); chartMain = null; }

            const url = `/api/gerencia/cierre-mensual-gerencia?anio=${anio}&mes=${mes}&division=${encodeURIComponent(getDivisionList(GERENCIA_SCOPE))}`;

            const json = await fetchJson(url);
            if (!json) return;

            const k = json.kpis || {};                 // ✅ NO comentar
            const rawRows = json.rows || [];
            const rows = rawRows.map(normalizarFilaMensual);

            setKPIs7(k);                              // ✅ Cards por categoría

            if (el("tablaTitle")) el("tablaTitle").textContent = "Informe Ventas Mensual";
            if (el("tablaHint")) {
                const d = String(json.desde || "").slice(0, 10);
                const h = String(json.hasta || "").slice(0, 10);
                el("tablaHint").textContent =
                    (d && h) ? `(desde ${d} hasta ${h})` : `Periodo: ${anio}-${String(mes).padStart(2, "0")}`;
            }

            const headers = [
                { title: "Vendedor", key: "Vendedor" },
                { title: "Quimicos", key: "Quimicos", fmt: "money" },
                { title: "Accesorios", key: "Accesorios", fmt: "money" },
                { title: "Maquinas", key: "Maquinas", fmt: "money" },
                { title: "Repuestos Maquinas", key: "RepuestosMaquinas", fmt: "money" },
                { title: "Servicio Tecnico", key: "ServicioTecnico", fmt: "money" },
                { title: "Otras Ventas", key: "OtrasVentas", fmt: "money" },
                { title: "Total General", key: "TotalGeneral", fmt: "money" },  // ✅ aquí está la clave
            ];

            renderTable(headers, rows);

            // Excel
            lastExport = { headers, rows, filename: `CierreMensual_${anio}-${String(mes).padStart(2, "0")}.xlsx` };
            const btnExp = el("btnExportCSV");
            if (btnExp) {
                btnExp.style.display = "";
                btnExp.textContent = "Exportar a Excel (XLSX)";
                btnExp.onclick = () => downloadXLSX(lastExport.filename, lastExport.rows, lastExport.headers);
            }

            setEstado(`OK • filas: ${rows.length}`);





        } catch (err) {
            console.error(err);
            setEstado(`Error: ${err?.message || err}`);
        }
    }

    // =====================================================
    // BOOTSTRAP / EVENTOS INICIALES
    // =====================================================
    function boot() {
        const b1 = el("btnCargar");
        const b2 = el("btnReload");

        if (b1) b1.addEventListener("click", cargar);

        // Si btnReload es para volver a ejecutar cargar:
        // if (b2) b2.addEventListener("click", cargar);

        // Si btnReload es para recargar la página:
        if (b2) b2.addEventListener("click", () => location.reload());

        initDefaults();
        setEstado("Listo.");
    }

    document.addEventListener("DOMContentLoaded", boot);


})();
