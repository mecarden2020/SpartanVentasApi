/* global XLSX, Chart */

(() => {
    // ------------------------------------------------------------
    // CONFIG
    // ------------------------------------------------------------
    const API_BASE = window.API_BASE || window.location.origin;

    let cnChart = null;

    // data cache del último reporte (para excel)
    let cnLastData = { resumen: [], detalle: [] };

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    const $ = (id) => document.getElementById(id);

    function setEstado(msg, type = "muted") {
        const el = $("cnEstado");
        if (!el) return;
        // type: ok | warn | err | muted
        const cls =
            type === "ok" ? "text-success"
                : type === "warn" ? "text-warning"
                    : type === "err" ? "text-danger"
                        : "text-muted";
        el.className = `card p-2 mb-3 ${cls}`;
        el.textContent = msg;
    }

    function monthNameES(m) {
        const names = ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];
        return names[m] || "";
    }

    function fmtCLP(n) {
        const x = Number(n || 0);
        return x.toLocaleString("es-CL");
    }

    function fmtDate(d) {
        if (!d) return "";
        const dt = new Date(d);
        if (isNaN(dt)) return "";
        const dd = String(dt.getDate()).padStart(2, "0");
        const mm = String(dt.getMonth() + 1).padStart(2, "0");
        const yy = dt.getFullYear();
        return `${dd}/${mm}/${yy}`;
    }

    function safeLowerKeys(obj) {
        if (!obj || typeof obj !== "object") return obj;
        const out = {};
        for (const k of Object.keys(obj)) {
            const nk = k.length ? (k[0].toLowerCase() + k.slice(1)) : k; // PascalCase -> camelCase
            out[nk] = obj[k];
        }
        return out;
    }







    function cnSetKpisFromDetalle(rows) {

        const rowsL = (rows || []).map(safeLowerKeys);

        // ===== PARSER NUM CHILENO ROBUSTO =====
        const parseNumCL = (v) => {
            if (v == null) return 0;
            if (typeof v === "number") return isFinite(v) ? v : 0;

            let t = String(v).trim().replace(/[^\d.,-]/g, "");

            const hasComma = t.includes(",");
            const hasDot = t.includes(".");

            if (hasComma && hasDot) {
                t = t.replace(/\./g, "").replace(",", ".");
            } else if (hasDot && !hasComma) {
                const dots = (t.match(/\./g) || []).length;
                if (dots > 1) t = t.replace(/\./g, "");
            } else if (hasComma && !hasDot) {
                t = t.replace(",", ".");
            }

            const n = Number(t);
            return isFinite(n) ? n : 0;
        };

        // ===== KPI REALES =====

        // Clientes únicos
        const getCodCli = (r) => {
            const v =
                r.codigocliente ?? r.codigoCliente ??
                r.cardcode ?? r.cardCode ??
                r.cardcodebase ?? r.cardCodeBase ??
                "";
            return String(v).trim();
        };

        const setClientes = new Set(
            rowsL.map(getCodCli).filter(v => v.length > 0)
        );


        const clientesUnicos = setClientes.size;

        // Filas detalle
        const filas = rowsL.length;

        // Total Neto
        const totalNeto = rowsL.reduce((acc, r) => {
            const val = r.total ?? r.linetotal ?? 0;
            return acc + parseNumCL(val);
        }, 0);

        console.log("KPI DEBUG FIX:", {
            filas,
            clientesUnicos,
            totalNeto
        });

        // ===== RENDER =====
        const elCli = document.getElementById("kpiClientes");
        const elFil = document.getElementById("kpiFilas");
        const elTot = document.getElementById("kpiTotal");

        if (elCli) elCli.textContent = clientesUnicos.toLocaleString("es-CL");
        if (elFil) elFil.textContent = filas.toLocaleString("es-CL");
        if (elTot) elTot.textContent = fmtCLP(totalNeto);
    }















    // ------------------------------------------------------------
    // Fetch Report (API)
    // ------------------------------------------------------------
    async function cnFetchReport(desdeIso, hastaIso) {
        const url = `/api/Ventas/clientes-nuevos-quimicos?desde=${encodeURIComponent(desdeIso)}&hasta=${encodeURIComponent(hastaIso)}`;
        console.log("GET =>", url);

        const r = await SpartanAuth.fetchAuth(url);

        // Caso 1: fetchAuth devuelve un Response (fetch nativo)
        if (r && typeof r.json === "function") {
            if (!r.ok) {
                const txt = await r.text().catch(() => "");
                throw new Error(`HTTP ${r.status} :: ${txt}`);
            }
            return await r.json();
        }

        // Caso 2: fetchAuth devuelve directamente el JSON (objeto)
        // (en este caso asumimos que si viene con error, trae status/message)
        if (r && (r.resumen || r.detalle)) return r;

        // Caso 3: fetchAuth devuelve wrapper { ok, status, data, message }
        if (r && Object.prototype.hasOwnProperty.call(r, "ok")) {
            if (!r.ok) throw new Error(`HTTP ${r.status ?? ""} :: ${r.message ?? "Error consultando"}`);
            return r.data ?? r;
        }

        // Caso final
        throw new Error("Respuesta inesperada de SpartanAuth.fetchAuth()");
    }




    // ------------------------------------------------------------
    // KPIs (Clientes únicos / filas / total neto)
    // ------------------------------------------------------------
    function cnCalcKPIs(detalle) {
        const rows = (detalle || []);
        const clientesUnicos = new Set(rows.map(r => (r.codigocliente ?? r.codigoCliente ?? "").toString())).size;
        const filas = rows.length;
        const totalNeto = rows.reduce((acc, r) => acc + Number(r.total || 0), 0);
        return { clientesUnicos, filas, totalNeto };
    }


    function cnRenderKPIs(kpi) {
        const a = $("cnKpiClientes");
        const b = $("cnKpiFilas");
        const c = $("cnKpiTotal");

        if (a) a.textContent = String(kpi.clientesUnicos || 0);
        if (b) b.textContent = String(kpi.filasDetalle || 0);
        if (c) c.textContent = fmtCLP(kpi.totalNeto || 0);
    }

    // ------------------------------------------------------------
    // Chart
    // ------------------------------------------------------------
    function cnRenderChart(resumenRaw) {
        const resumen = (resumenRaw || []);

        const labels = resumen.map(x => x.empleadoVentas ?? x.EmpleadoVentas ?? "");
        const data = resumen.map(x => Number(x.clientesNuevos ?? x.ClientesNuevos ?? 0));

        const ctx = $("cnChart").getContext("2d");

        if (cnChart) { cnChart.destroy(); cnChart = null; }
        const existing = Chart.getChart($("cnChart"));
        if (existing) existing.destroy();

        cnChart = new Chart(ctx, {
            type: "bar",
            data: { labels, datasets: [{ label: "Clientes Nuevos", data }] },
            options: {
                responsive: true,
                plugins: { legend: { display: true } },
                scales: { y: { beginAtZero: true, ticks: { precision: 0 } } }
            }
        });
    }




    function pick(obj, ...keys) {
        for (const k of keys) {
            const v = obj && obj[k];
            if (v !== undefined && v !== null && v !== "") return v;
        }
        return "";
    }

    // ------------------------------------------------------------
    // Tabla detalle
    // ------------------------------------------------------------
    function cnRenderDetalle(rows) {
        const tbody = document.querySelector("#cnTabla tbody");
        if (!tbody) return;

        // 1) Normaliza keys a minúsculas SIEMPRE
        const rowsL = (rows || []).map(safeLowerKeys);

        // 2) Parser CLP robusto (soporta number, "57.180", "57,180", "$ 57.180", etc.)
        const parseNumCL = (v) => {
            if (v == null) return 0;
            if (typeof v === "number") return isFinite(v) ? v : 0;

            const s = String(v).trim();
            if (!s) return 0;

            // deja solo dígitos, punto, coma y signo
            let t = s.replace(/[^\d.,-]/g, "");

            // caso típico CL: miles '.' y decimales ','
            // ej: "1.234.567" -> 1234567
            // ej: "1.234,56"  -> 1234.56
            const hasComma = t.includes(",");
            const hasDot = t.includes(".");

            if (hasComma && hasDot) {
                // asume '.' miles y ',' decimal
                t = t.replace(/\./g, "").replace(",", ".");
            } else if (hasDot && !hasComma) {
                // puede ser miles con '.' o decimal '.'
                // si tiene más de 1 punto -> miles
                const dots = (t.match(/\./g) || []).length;
                if (dots > 1) t = t.replace(/\./g, "");
            } else if (hasComma && !hasDot) {
                // si solo coma, asume decimal
                t = t.replace(",", ".");
            }

            const n = Number(t);
            return isFinite(n) ? n : 0;
        };

        // ✅ Parche anti-sucursales (front)
        const isMatriz = (code) => {
            const s = String(code ?? "").trim();
            return /^C\d{7,8}-[0-9K]$/i.test(s); // C#######-DV o C########-DV
        };

        // 3) Filtra data
        const data = rowsL.filter(r => isMatriz(r.codigocliente ?? r.cardcode));

        // 4) Total del detalle (usa varios nombres posibles por si cambió el backend)
        const totalDetalle = data.reduce((acc, r) => {
            const val = r.total ?? r.totalnetolineas ?? r.totalventas ?? r.linetotal ?? 0;
            return acc + parseNumCL(val);
        }, 0);

        // 5) Muestra en el header (badge/label)
        const elTot = document.getElementById("cnTotales");
        if (elTot) elTot.textContent = `Total: ${fmtCLP(totalDetalle)}`;

        // 6) Render filas    }



        tbody.innerHTML = data.map(r => {
            const empleado = r.empleadoVentas ?? "";
            const docto = r.docto ?? "";
            const folio = r.nFolio ?? r.nfolio ?? "";
            const fCont = fmtDate(r.fechaContabliz ?? r.docDate ?? r.docdate);
            const fCre = fmtDate(r.fechaCreacion ?? r.createDate ?? r.createdate);

            const rut = r.rut ?? r.licTradNum ?? r.lictradnum ?? "";
            const codCli = r.codigoCliente ?? r.cardCode ?? r.cardcode ?? "";
            const cliente = r.cliente ?? r.cardName ?? r.cardname ?? "";

            const codArt = r.codigoArticulo ?? r.itemCode ?? r.itemcode ?? "";
            const articulo = r.articulo ?? r.dscription ?? r.dscription ?? r.descripcion ?? "";

            const cantidad = fmtNum(r.cantidad);
            const precio = fmtNum(r.precio);
            const total = fmtNum(r.total);

            return `
        <tr>
          <td>${empleado}</td>
          <td>${docto}</td>
          <td>${folio}</td>
          <td>${fCont}</td>
          <td>${fCre}</td>
          <td>${rut}</td>
          <td>${codCli}</td>
          <td class="col-cliente">${cliente}</td>
          <td>${codArt}</td>
          <td class="col-articulo">${articulo}</td>
          <td class="num">${cantidad}</td>
          <td class="num">${precio}</td>
          <td class="num">${total}</td>
        </tr>`;
        }).join("");

        cnSetKpisFromDetalle(rows);

    }


    // ------------------------------------------------------------
    // Export Excel
    // ------------------------------------------------------------
    function ddmmyyyy_to_iso(s) {
        // "01-02-2026" => "2026-02-01"
        if (!s) return "";
        const m = String(s).match(/^(\d{2})-(\d{2})-(\d{4})$/);
        if (!m) return s; // si ya viene ISO, lo deja
        return `${m[3]}-${m[2]}-${m[1]}`;
    }



    function cnExportExcel(desdeISO) {
        if (!cnLastData || (!cnLastData.resumen?.length && !cnLastData.detalle?.length)) {
            alert("No hay datos para exportar.");
            return;
        }

        const iso = ddmmyyyy_to_iso(desdeISO);
        const d = new Date(iso + "T00:00:00"); // evita desfases por timezone
        const mes = isNaN(d) ? "MES" : monthNameES(d.getMonth());
        const anio = isNaN(d) ? "ANIO" : d.getFullYear();

        const titulo = `Informe Clientes Nuevos ${mes} ${anio}`.trim();
        const filename = `Informe_Clientes_Nuevos_${mes}_${anio}.xlsx`;

        const wb = XLSX.utils.book_new();

        const resumen = (cnLastData.resumen || []).map(safeLowerKeys);

        const detalle = (cnLastData.detalle || []).map(safeLowerKeys);
        // ===============================
        // TOTAL DETALLE
        // ===============================
        const parseNumCL = (v) => {
            if (v == null) return 0;
            if (typeof v === "number") return v;
            let t = String(v).replace(/[^\d.,-]/g, "");
            if (t.includes(",") && t.includes(".")) t = t.replace(/\./g, "").replace(",", ".");
            else if (t.includes(",")) t = t.replace(",", ".");
            const n = Number(t);
            return isFinite(n) ? n : 0;
        };

        const totalDetalle = detalle.reduce((acc, r) => {
            return acc + parseNumCL(r.total ?? r.linetotal ?? 0);
        }, 0);

         // ------- Resumen -------

        const wsResumenAOA = [
            [titulo],
            [],
            ["Empleado Ventas", "Clientes Nuevos", "Total Ventas"],
            ...resumen.map(x => ([
                x.empleadoVentas ?? "",
                Number(x.clientesNuevos ?? 0),
                Number(x.totalVentas ?? 0)
            ]))
        ];

        const wsResumen = XLSX.utils.aoa_to_sheet(wsResumenAOA);
        wsResumen["!merges"] = [{ s: { r: 0, c: 0 }, e: { r: 0, c: 2 } }];
        XLSX.utils.book_append_sheet(wb, wsResumen, "Resumen");

        // ------- Detalle (orden como tu imagen) -------
        const headerDetalle = [
            "Empleado Ventas", "Docto", "Nº Folio", "Fecha Contabliz", "Fecha Creación",
            "Rut", "Código Cliente", "Cliente", "Código Artículo", "Artículo",
            "Cantidad", "Precio", "Total"
        ];

        const wsDetalleAOA = [
            [titulo],
            [],
            headerDetalle,
            ...detalle.map(r => ([
                r.empleadoventas ?? r.empleadoVentas ?? "",
                r.docto ?? "",
                r.nfolio ?? r.nFolio ?? "",
                fmtDate(r.fechacontabliz ?? r.fechaContabliz ?? r.docdate),
                fmtDate(r.fechacreacion ?? r.fechaCreacion ?? r.createdate),
                r.rut ?? "",
                r.codigocliente ?? r.codigoCliente ?? r.cardcode ?? "",
                r.cliente ?? r.cardname ?? "",
                r.codigoarticulo ?? r.codigoArticulo ?? r.itemcode ?? "",
                r.articulo ?? r.dscription ?? "",
                Number(r.cantidad ?? 0),
                Number(r.precio ?? 0),
                Number(r.total ?? 0)
            ]))

        ];

        // ✅ TOTAL GENERAL ABAJO
        wsDetalleAOA.push([]);
        wsDetalleAOA.push([
            "TOTAL GENERAL",
            "", "", "", "", "", "", "", "", "", "", "",
            totalDetalle
        ]);

        // 👉 RECIÉN AQUÍ CREAS EL SHEET
        const wsDetalle = XLSX.utils.aoa_to_sheet(wsDetalleAOA);
        XLSX.utils.book_append_sheet(wb, wsDetalle, "Detalle");

        

        XLSX.writeFile(wb, filename);
    }









    function toIsoDate(v) {
        if (!v) return "";

        v = v.trim();

        // Caso 1: ya viene en ISO YYYY-MM-DD
        if (/^\d{4}-\d{2}-\d{2}$/.test(v)) return v;

        // Caso 2: viene DD-MM-YYYY
        let m = v.match(/^(\d{2})-(\d{2})-(\d{4})$/);
        if (m) {
            const [, dd, mm, yyyy] = m;
            return `${yyyy}-${mm}-${dd}`;
        }

        // Caso 3: viene DD/MM/YYYY
        m = v.match(/^(\d{2})\/(\d{2})\/(\d{4})$/);
        if (m) {
            const [, dd, mm, yyyy] = m;
            return `${yyyy}-${mm}-${dd}`;
        }

        // Caso 4: fallback Date JS
        const d = new Date(v);
        if (!isNaN(d)) return d.toISOString().slice(0, 10);

        return "";
    }



    // ------------------------------------------------------------
    // Run
    // ------------------------------------------------------------
    async function cnRun() {
        console.count("cnRun called");

        const desde = $("cnDesde")?.value;
        const hasta = $("cnHasta")?.value;

        if (!desde || !hasta) {
            setEstado("Debes ingresar Desde y Hasta.", "warn");
            return;
        }
        if (hasta < desde) {
            setEstado("'Hasta' no puede ser menor que 'Desde'.", "warn");
            return;
        }

        $("cnBuscar").disabled = true;
        $("cnExportar").disabled = true;

        try {
            setEstado("Consultando...", "muted");

            // ISO
            const desdeIso = toIsoDate(desde);
            const hastaIso = toIsoDate(hasta);

            if (!desdeIso || !hastaIso) {
                setEstado("Formato de fecha inválido. Usa DD-MM-YYYY.", "warn");
                return;
            }

            // =========================
            // Fetch + Normalización única
            // =========================
            const data = await cnFetchReport(desdeIso, hastaIso);

            const resumenN = (data?.resumen || []).map(safeLowerKeys);
            const detalleN = (data?.detalle || []).map(safeLowerKeys);

            // guarda para export (USA ESTO EN cnExportExcel)
            cnLastData = { resumen: resumenN, detalle: detalleN };

            console.log("Data recibida:", cnLastData);

            // =========================
            // Render Resumen + Detalle
            // =========================
            if (typeof cnRenderResumen === "function") cnRenderResumen(resumenN);
            if (typeof cnRenderDetalle === "function") cnRenderDetalle(detalleN);

            // =========================
            // KPIs + Total Detalle
            // =========================
            if (typeof cnSetKpisFromDetalle === "function") cnSetKpisFromDetalle(detalleN);
            if (typeof cnSetTotalDetalleHeader === "function") cnSetTotalDetalleHeader(detalleN);

            // =========================
            // Chart (resumen)
            // =========================
            try {
                if (typeof cnChart !== "undefined" && cnChart) { cnChart.destroy(); cnChart = null; }
                const canvas = $("cnChart");
                if (canvas && window.Chart) {
                    const existing = Chart.getChart(canvas);
                    if (existing) existing.destroy();
                }
            } catch (err) {
                console.warn("No se pudo destruir el chart anterior:", err);
            }

            if (typeof cnRenderChart === "function") {
                cnRenderChart(resumenN);
            }

            // =========================
            // Export + Estado
            // =========================
            const hayData = (resumenN.length > 0) || (detalleN.length > 0);
            $("cnExportar").disabled = !hayData;

            if (hayData) {
                setEstado("Listo. Reporte cargado correctamente.", "ok");
            } else {
                setEstado("Sin datos para el período indicado.", "warn");
            }
        } catch (e) {
            console.error("Error cnRun:", e);
            setEstado(e?.message || "Error consultando Clientes Nuevos.", "err");
            alert(e?.message || "Error consultando Clientes Nuevos");
        } finally {
            $("cnBuscar").disabled = false;
        }
    }



    function cnRenderDetalle(detalleRaw) {
        const detalle = (detalleRaw || []).map(safeLowerKeys);

        const tabla = $("cnTabla");
        if (!tabla) {
            console.warn("No existe la tabla #cnTabla");
            return;
        }

        // Asegurar tbody
        let tbody = tabla.querySelector("tbody");
        if (!tbody) {
            tbody = document.createElement("tbody");
            tabla.appendChild(tbody);
        }
        tbody.innerHTML = "";

        // helpers num
        const fmtNum = (v) => {
            const n = Number(v);
            if (!isFinite(n)) return "";
            return n.toLocaleString("es-CL", { maximumFractionDigits: 2 });
        };

        for (const r of detalle) {
            const tr = document.createElement("tr");

            // soporta nombres alternativos por si llega algo distinto
            const empleado = r.empleadoventas ?? r.empleadoventa ?? r.empleadoventas ?? r.empleadoVentas ?? r.empleado_ventas ?? r.empleado ?? "";
            const docto = r.docto ?? "";
            const nFolio = r.nfolio ?? r.nFolio ?? "";
            const fCont = r.fechacontabliz ?? r.fechaContabliz ?? r.docdate ?? r.docDate ?? null;
            const fCrea = r.fechacreacion ?? r.fechaCreacion ?? r.createdate ?? r.createDate ?? null;
            const rut = r.rut ?? r.lictradnum ?? r.licTradNum ?? "";
            const codCli = r.codigocliente ?? r.codigoCliente ?? r.cardcode ?? r.cardCode ?? "";
            const cli = r.cliente ?? r.cardname ?? r.cardName ?? "";
            const codArt = r.codigoarticulo ?? r.codigoArticulo ?? r.itemcode ?? r.itemCode ?? "";
            const art = r.articulo ?? r.dscription ?? r.dscription ?? r.dscription ?? r.descrip ?? "";
            const cant = r.cantidad ?? 0;
            const precio = r.precio ?? 0;
            const total = r.total ?? 0;

            tr.innerHTML = `
            <td class="cn-emp">${empleado}</td>
            <td class="cn-mini">${docto}</td>
            <td class="text-end cn-mini">${nFolio}</td>
            <td class="cn-date">${fmtDate(fCont)}</td>
            <td class="cn-date">${fmtDate(fCrea)}</td>
            <td class="cn-mini">${rut}</td>
            <td class="cn-mini">${codCli}</td>
            <td class="cn-cli">${cli}</td>
            <td class="cn-mini">${codArt}</td>
            <td class="cn-art">${art}</td>
            <td class="text-end cn-num">${fmtNum(cant)}</td>
            <td class="text-end cn-num">${fmtCLP(precio)}</td>
            <td class="text-end cn-num fw-semibold">${fmtCLP(total)}</td>
        `;

            tbody.appendChild(tr);
        }
    }


    function cnBuildFilename(desdeIso, hastaIso) {
        // desdeIso/hastaIso = "YYYY-MM-DD"
        const [y, m] = (desdeIso || "").split("-"); // y=2026, m=01

        const meses = ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];
        const mesTxt = (m ? meses[Number(m) - 1] : "MES");

        // Si el rango está dentro de un mismo mes, usa "MES_AÑO"
        // Si cruza meses, usa "YYYYMMDD_YYYYMMDD"
        const sameMonth = desdeIso?.slice(0, 7) === hastaIso?.slice(0, 7);

        if (sameMonth) {
            return `Informe_Clientes_Nuevos_${mesTxt}_${y}.xlsx`;
        }
        return `Informe_Clientes_Nuevos_${desdeIso.replaceAll("-", "")}_${hastaIso.replaceAll("-", "")}.xlsx`;
    }


    // ------------------------------------------------------------
    // Init
    // ------------------------------------------------------------
    function initDefaults() {
        // por defecto: mes actual
        const now = new Date();
        const y = now.getFullYear();
        const m = now.getMonth();
        const first = new Date(y, m, 1);
        const last = new Date(y, m + 1, 0);

        const toISO = (d) => {
            const dd = String(d.getDate()).padStart(2, "0");
            const mm = String(d.getMonth() + 1).padStart(2, "0");
            return `${d.getFullYear()}-${mm}-${dd}`;
        };

        if ($("cnDesde") && !$("cnDesde").value) $("cnDesde").value = toISO(first);
        if ($("cnHasta") && !$("cnHasta").value) $("cnHasta").value = toISO(last);

        // export deshabilitado hasta tener data
        if ($("cnExportar")) $("cnExportar").disabled = true;
    }

    document.addEventListener("DOMContentLoaded", () => {
        initDefaults();

        $("cnBuscar")?.addEventListener("click", cnRun);
        $("cnExportar")?.addEventListener("click", () => cnExportExcel($("cnDesde")?.value || ""));


        /*$("cnExportar")?.addEventListener("click", () => {
            const desdeISO = $("cnDesde")?.value || "";
            cnExportExcel(desdeISO);
        });*/ 

        console.log("nuevos_gerencia.js cargado OK");
    });
})();
