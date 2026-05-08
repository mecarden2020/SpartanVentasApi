(function () {
    const filtroAnio = document.getElementById("filtroAnio");
    const filtroMes = document.getElementById("filtroMes");
    const filtroCategoria = document.getElementById("filtroCategoria");
    const filtroDivision = document.getElementById("filtroDivision");
    const btnActualizar = document.getElementById("btnActualizar");

    const kpiVentaTotal = document.getElementById("kpiVentaTotal");
    const kpiMetaTotal = document.getElementById("kpiMetaTotal");
    const kpiCumplimiento = document.getElementById("kpiCumplimiento");
    const kpiBrecha = document.getElementById("kpiBrecha");

    const tbodyResumenEquipos = document.getElementById("tbodyResumenEquipos");
    const loadingBox = document.getElementById("loadingBox");
    const errorBox = document.getElementById("errorBox");

    const chartVentaMetaCanvas = document.getElementById("chartVentaMeta");
    const chartCumplimientoCanvas = document.getElementById("chartCumplimiento");
    const chartTopProductosCanvas = document.getElementById("chartTopProductos");

    let chartVentaMeta = null;
    let chartCumplimiento = null;
    let chartTopProductos = null;

    let dataEquipos = [];
    let dataVendedores = [];
    let dataProductos = [];
    let dataClientesTop = [];
    let proyeccionCierreEquipos = [];
    let proyeccionCierreVendedores = [];

    function getEstadoColor(valor) {
        return valor >= 0 ? 'success' : 'danger';
    }

    function getEstadoTexto(valor) {
        return valor >= 0 ? 'Cumplido' : 'Oportunidad';
    }

    function formatCurrency(value) {
        return new Intl.NumberFormat("es-CL", {
            style: "currency",
            currency: "CLP",
            maximumFractionDigits: 0
        }).format(value || 0);
    }

    function formatNumber(value) {
        return new Intl.NumberFormat("es-CL", {
            maximumFractionDigits: 2
        }).format(value || 0);
    }



    function getAuthHeaders() {
        const token = localStorage.getItem("jwtToken") || localStorage.getItem("token") || "";

        return token
            ? { "Authorization": `Bearer ${token}` }
            : {};
    }

    async function fetchJsonAuth(url) {
        const response = await fetch(url, {
            method: "GET",
            headers: getAuthHeaders()
        });

        if (!response.ok) {
            throw new Error(`Error HTTP ${response.status}`);
        }

        return await response.json();
    }





    function getEstadoBadge(cumplimientoPct) {
        if (cumplimientoPct >= 100) {
            return `<span class="badge badge-ok">Cumplido</span>`;
        }

        if (cumplimientoPct >= 90) {
            return `<span class="badge badge-mid">En línea</span>`;
        }

        return `<span class="badge badge-low">Oportunidad</span>`;
    }

    function setLoading(isLoading) {
        if (loadingBox) {
            loadingBox.style.display = isLoading ? "block" : "none";
        }

        if (btnActualizar) {
            btnActualizar.disabled = isLoading;
        }
    }

    function showError(message) {
        if (!errorBox) return;
        errorBox.textContent = message;
        errorBox.style.display = "block";
    }

    function hideError() {
        if (!errorBox) return;
        errorBox.style.display = "none";
        errorBox.textContent = "";
    }

    function renderKpis(kpis) {
        const ventaTotal = kpis?.ventaTotal ?? 0;
        const metaTotal = kpis?.metaTotal ?? 0;
        const cumplimientoPct = kpis?.cumplimientoPct ?? 0;
        const brecha = kpis?.brecha ?? 0;

        if (kpiVentaTotal) kpiVentaTotal.textContent = formatCurrency(ventaTotal);
        if (kpiMetaTotal) kpiMetaTotal.textContent = formatCurrency(metaTotal);
        if (kpiCumplimiento) kpiCumplimiento.textContent = `${formatNumber(cumplimientoPct)}%`;
        if (kpiBrecha) kpiBrecha.textContent = formatCurrency(brecha);

        if (kpiCumplimiento) {
            kpiCumplimiento.style.color =
                cumplimientoPct >= 100 ? "#7fb36a" :
                    cumplimientoPct >= 90 ? "#c49a1f" :
                        "#d36b6b";
        }

        if (kpiBrecha) {
            kpiBrecha.style.color = brecha >= 0 ? "#7fb36a" : "#d36b6b";
        }
    }


    async function cargarProyeccionCierre() {
        try {
            const anio = document.getElementById("filtroAnio")?.value;
            const mes = document.getElementById("filtroMes")?.value;
            /*const categoria = document.getElementById("filtroCategoria")?.value || "Ventas Quimicos";*/
            let categoria = document.getElementById("filtroCategoria")?.value || "Ventas Quimicos";

            categoria = categoria
                .replace("Químicos", "Quimicos")
                .replace("Máquinas", "Maquinas")
                .replace("Técnico", "Tecnico");

            const division = document.getElementById("filtroDivision")?.value || "Todas";

            const url = `/api/gerencia/proyeccion-cierre?anio=${anio}&mes=${mes}&categoria=${encodeURIComponent(categoria)}&division=${encodeURIComponent(division)}`;

            const response = await fetch(url, {
                headers: {
                    "Authorization": `Bearer ${localStorage.getItem("jwtToken") || localStorage.getItem("token") || ""}`
                }
            });

            if (!response.ok) {
                throw new Error("Error al cargar proyección de cierre");
            }

            const result = await fetchJsonAuth(url);
            const data = result.data || {};   // 👈 ESTE ES EL FIX
            // 🔍 DEBUG (AQUÍ VA)
            console.log("DATA PROYECCION:", data);

            proyeccionCierreEquipos = data.equipos || [];
            proyeccionCierreVendedores = data.vendedores || [];

            // 🔍 DEBUG (AQUÍ TAMBIÉN)
            console.log("EQUIPOS PROYECCION:", proyeccionCierreEquipos);
            console.log("VENDEDORES PROYECCION:", proyeccionCierreVendedores);

            renderResumenProyeccionCierre();
            renderDetalleProyeccionCierre();


        } catch (error) {
            console.error("Error cargarProyeccionCierre:", error);
        }
    }


    function cargarSelectEquipoProyeccionCierre() {
        const select = document.getElementById("selectEquipoProyeccionCierre");
        if (!select) return;

        const equipoActual = select.value;

        const equipos = [...new Set(proyeccionCierreVendedores.map(x => x.equipo))]
            .filter(x => x)
            .sort();

        select.innerHTML = "";

        equipos.forEach(equipo => {
            select.innerHTML += `<option value="${equipo}">${equipo}</option>`;
        });

        if (equipoActual && equipos.includes(equipoActual)) {
            select.value = equipoActual;
        }

        select.onchange = renderDetalleProyeccionCierre;
    }

    function renderResumenProyeccionCierre() {
        const tbody = document.getElementById("tbodyResumenProyeccionCierre");
        if (!tbody) return;

        tbody.innerHTML = "";

        proyeccionCierreEquipos.forEach(item => {
            const proyeccion = Number(item.proyeccionCierre ?? item.total ?? 0);
            const meta = Number(item.metaTotalEquipo ?? item.meta ?? 0);
            const diferencia = proyeccion - meta;

            const estadoTexto = diferencia >= 0 ? "Cumplido" : "Oportunidad";
            const estadoClass = diferencia >= 0 ? "bg-success" : "bg-danger";

            tbody.innerHTML += `
            <tr>
                <td>${item.equipo || "Sin Equipo"}</td>
                <td class="text-end">${formatCurrency(proyeccion)}</td>
                <td class="text-end">${formatCurrency(meta)}</td>
                <td class="text-end">${formatCurrency(diferencia)}</td>
                <td class="text-center">
                    <span class="badge ${estadoClass}">${estadoTexto}</span>
                </td>
            </tr>
        `;
        });

        cargarSelectEquipoProyeccionCierre();
    }

    function renderDetalleProyeccionCierre() {
        const tbody = document.getElementById("tbodyDetalleProyeccionCierre");
        const select = document.getElementById("selectEquipoProyeccionCierre");

        const kpiTotalEquipo = document.getElementById("kpiTotalProyectadoEquipo");
        const kpiMetaEquipo = document.getElementById("kpiMetaProyectadaEquipo");

        if (!tbody || !select) return;

        const equipoSeleccionado = select.value;

        const vendedoresFiltrados = proyeccionCierreVendedores.filter(x =>
            !equipoSeleccionado || x.equipo === equipoSeleccionado
        );

        const resumenEquipo = proyeccionCierreEquipos.find(x => x.equipo === equipoSeleccionado);

        if (kpiTotalEquipo) {
            kpiTotalEquipo.textContent = formatCurrency(resumenEquipo?.proyeccionCierre ?? 0);
        }

        if (kpiMetaEquipo) {
            kpiMetaEquipo.textContent = formatCurrency(resumenEquipo?.metaTotalEquipo ?? 0);
        }

        tbody.innerHTML = "";

        vendedoresFiltrados.forEach(item => {
            const facturas = Number(item.facturas ?? 0);
            const pedidos = Number(item.pedidos ?? 0);
            const entregas = Number(item.entregas ?? 0);
            const total = Number(item.proyeccionCierre ?? item.total ?? 0);
            const meta = Number(item.metaTotalVendedor ?? item.meta ?? 0);
            const diferencia = Number(item.diferenciaProyectada ?? (total - meta));

            const estadoTexto = diferencia >= 0 ? "Cumplido" : "Oportunidad";
            const estadoClass = diferencia >= 0 ? "badge-ok" : "badge-low";

            tbody.innerHTML += `
            <tr>
                <td>${item.vendedor || ""}</td>
                <td class="text-end">${formatCurrency(facturas)}</td>
                <td class="text-end">${formatCurrency(pedidos)}</td>
                <td class="text-end">${formatCurrency(entregas)}</td>
                <td class="text-end">${formatCurrency(total)}</td>
                <td class="text-end">${formatCurrency(meta)}</td>
                <td class="text-end">${formatCurrency(diferencia)}</td>
                <td class="text-center">
                    <span class="badge ${estadoClass}">${estadoTexto}</span>
                </td>
            </tr>
        `;
        });
    }






    function formatMoney(value) {
        return Number(value || 0).toLocaleString("es-CL", {
            style: "currency",
            currency: "CLP",
            maximumFractionDigits: 0
        });
    }

    function renderResumenEquipos(equipos) {
        if (!tbodyResumenEquipos) return;

        if (!equipos || equipos.length === 0) {
            tbodyResumenEquipos.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center text-muted py-4">
                        No hay información para los filtros seleccionados.
                    </td>
                </tr>
            `;
            return;
        }

        tbodyResumenEquipos.innerHTML = equipos.map(item => `
            <tr>
                <td>${item.equipo ?? ""}</td>
                <td class="text-end">${formatCurrency(item.venta ?? 0)}</td>
                <td class="text-end">${formatCurrency(item.meta ?? 0)}</td>
                <td class="text-end">${formatNumber(item.cumplimientoPct ?? 0)}%</td>
                <td class="text-end">${formatCurrency(item.brecha ?? 0)}</td>
                <td class="text-center">${getEstadoBadge(item.cumplimientoPct ?? 0)}</td>
            </tr>
        `).join("");
    }

    function destroyCharts() {
        if (chartVentaMeta) {
            chartVentaMeta.destroy();
            chartVentaMeta = null;
        }

        if (chartCumplimiento) {
            chartCumplimiento.destroy();
            chartCumplimiento = null;
        }

        if (chartTopProductos) {
            chartTopProductos.destroy();
            chartTopProductos = null;
        }
    }

    function renderCharts(equipos) {
        if (chartVentaMeta) {
            chartVentaMeta.destroy();
            chartVentaMeta = null;
        }

        if (chartCumplimiento) {
            chartCumplimiento.destroy();
            chartCumplimiento = null;
        }

        if (!equipos || equipos.length === 0 || !chartVentaMetaCanvas || !chartCumplimientoCanvas) {
            return;
        }

        const equiposOrdenados = [...equipos].sort((a, b) => (b.venta || 0) - (a.venta || 0));

        const labels = equiposOrdenados.map(x => x.equipo);
        const ventas = equiposOrdenados.map(x => x.venta || 0);
        const metas = equiposOrdenados.map(x => x.meta || 0);
        const cumplimientos = equiposOrdenados.map(x => Number((x.cumplimientoPct || 0).toFixed(2)));

        chartVentaMeta = new Chart(chartVentaMetaCanvas, {
            type: "bar",
            data: {
                labels,
                datasets: [
                    {
                        label: "Venta",
                        data: ventas,
                        backgroundColor: "#5c6b7a",
                        borderColor: "#44515d",
                        borderWidth: 1,
                        borderRadius: 4
                    },
                    {
                        label: "Meta",
                        data: metas,
                        backgroundColor: "#f0c64b",
                        borderColor: "#c7a63a",
                        borderWidth: 1,
                        borderRadius: 4
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 900 },
                plugins: {
                    legend: { position: "top" },
                    tooltip: {
                        callbacks: {
                            label(context) {
                                const label = context.dataset.label || "";
                                const value = context.parsed.y || 0;
                                return `${label}: ${formatCurrency(value)}`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        ticks: {
                            color: "#1f2a33",
                            maxRotation: 0,
                            minRotation: 0
                        },
                        grid: { display: false }
                    },
                    y: {
                        ticks: {
                            color: "#1f2a33",
                            callback(value) {
                                return new Intl.NumberFormat("es-CL", {
                                    notation: "compact",
                                    maximumFractionDigits: 1
                                }).format(value);
                            }
                        },
                        grid: { color: "#dde3e8" }
                    }
                }
            }
        });

        chartCumplimiento = new Chart(chartCumplimientoCanvas, {
            type: "bar",
            data: {
                labels,
                datasets: [
                    {
                        label: "Cumplimiento %",
                        data: cumplimientos,
                        backgroundColor: cumplimientos.map(v =>
                            v >= 100 ? "#7fb36a" :
                                v >= 90 ? "#f7d774" :
                                    "#d36b6b"
                        ),
                        borderColor: cumplimientos.map(v =>
                            v >= 100 ? "#6aa058" :
                                v >= 90 ? "#d5b24d" :
                                    "#ba5656"
                        ),
                        borderWidth: 1,
                        borderRadius: 4
                    }
                ]
            },
            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 900 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label(context) {
                                return `Cumplimiento: ${formatNumber(context.parsed.x)}%`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        beginAtZero: true,
                        ticks: {
                            color: "#1f2a33",
                            callback(value) {
                                return `${value}%`;
                            }
                        },
                        grid: { color: "#dde3e8" }
                    },
                    y: {
                        ticks: { color: "#1f2a33" },
                        grid: { display: false }
                    }
                }
            }
        });
    }

    async function cargarReporte() {
        try {
            hideError();

            const anio = filtroAnio?.value;
            const mes = filtroMes?.value;

            let categoriaVenta = filtroCategoria?.value || "Ventas Quimicos";
            categoriaVenta = categoriaVenta
                .replace("Químicos", "Quimicos")
                .replace("Máquinas", "Maquinas")
                .replace("Técnico", "Tecnico");

            const division = filtroDivision?.value;

            const params = new URLSearchParams({
                anio,
                mes,
                categoriaVenta
            });

            if (division) {
                params.append("division", division);
            }

            const response = await fetchJsonAuth(`/api/ventas/reporte-gerencial?${params.toString()}`);

            console.log("RESPUESTA REPORTE:", response);

            if (!response || response.ok === false) {
                throw new Error(response?.mensaje || "No fue posible obtener el reporte.");
            }

            const data = response.data ?? response ?? {};

            console.log("DATA NORMALIZADA:", data);
            console.log("KPIS:", data.kpis);
            console.log("EQUIPOS:", data.equipos);

            const kpis = data.kpis || {};
            const equipos = Array.isArray(data.equipos) ? data.equipos : [];
            const vendedores = Array.isArray(data.vendedores) ? data.vendedores : [];
            const productos = Array.isArray(data.productos) ? data.productos : [];
            const clientesTop = Array.isArray(data.clientesTop) ? data.clientesTop : [];

            // =====================================
            // Guardar datasets globales / locales
            // =====================================
            dataEquipos = equipos;
            dataVendedores = vendedores;
            dataProductos = productos;
            dataClientesTop = clientesTop;

            window.vendedoresGerencial = vendedores;
            window.productosGerencial = productos;
            window.clientesTopGerencial = clientesTop;

            // =====================================
            // Render principal
            // =====================================
            renderKpis(kpis);
            renderResumenEquipos(equipos);
            renderCharts(equipos);
            renderFiltrosEquipo();

            // =====================================
            // Render detalle con filtro de equipo
            // =====================================
            renderDetalleVendedores(vendedores);
            renderDetalleProductos(productos);
            renderClientesTop(clientesTop);

        } catch (error) {
            console.error("Error al cargar reporte:", error);
            showError(error.message || "Error inesperado al cargar el reporte.");

            dataEquipos = [];
            dataVendedores = [];
            dataProductos = [];
            dataClientesTop = [];

            window.vendedoresGerencial = [];
            window.productosGerencial = [];
            window.clientesTopGerencial = [];

            renderKpis({
                ventaTotal: 0,
                metaTotal: 0,
                cumplimientoPct: 0,
                brecha: 0
            });

            renderResumenEquipos([]);
            renderDetalleVendedores([]);
            renderDetalleProductos([]);
            renderClientesTop([]);
            destroyCharts();
        }
    }





    function renderFiltrosEquipo() {
        const filtroVend = document.getElementById("filtroEquipoVendedor");
        const filtroProd = document.getElementById("filtroEquipoProducto");

        if (!filtroVend || !filtroProd) return;

        const equiposUnicos = [...new Set(dataEquipos.map(x => x.equipo).filter(Boolean))].sort();

        const options = equiposUnicos.map(eq => `<option value="${eq}">${eq}</option>`).join("");

        filtroVend.innerHTML = options;
        filtroProd.innerHTML = options;

        if (equiposUnicos.length > 0) {
            filtroVend.value = equiposUnicos[0];
            filtroProd.value = equiposUnicos[0];
        } else {
            filtroVend.innerHTML = "";
            filtroProd.innerHTML = "";
        }

        actualizarVistaVendedores();
        actualizarVistaProductos();
    }

    function getEstadoVendedor(cumplimiento) {
        if (cumplimiento >= 100) {
            return `<span class="badge badge-ok">Cumplido</span>`;
        }

        if (cumplimiento >= 90) {
            return `<span class="badge badge-mid">En línea</span>`;
        }

        return `<span class="badge badge-low">Oportunidad</span>`;
    }

    // ======================================================================


    function normalizarTexto(valor) {
        return (valor || "").toString().trim().toUpperCase();
    }

    function getEquipoSeleccionadoVendedor() {
        const select = document.getElementById("filtroEquipoVendedor");
        return select ? select.value : "";
    }

    function getEquipoSeleccionadoProducto() {
        const select = document.getElementById("filtroEquipoProducto");
        return select ? select.value : "";
    }


    function renderDetalleVendedores(data) {
        const tbody = document.getElementById("tbodyDetalleVendedor");
        if (!tbody) return;

        const equipoSeleccionado = document.getElementById("filtroEquipoVendedor")?.value?.trim() || "";

        const filtrados = (data || []).filter(x => {
            const equipoFila = (x.equipo ?? x.Equipo ?? "").trim();

            if (!equipoSeleccionado || equipoSeleccionado === "Todos") return true;

            return equipoFila === equipoSeleccionado;
        });

        console.log("Equipo seleccionado vendedor:", equipoSeleccionado);
        console.log("Total vendedores:", data?.length || 0);
        console.log("Filtrados vendedor:", filtrados.length);

        if (filtrados.length === 0) {
            tbody.innerHTML = `
            <tr>
                <td colspan="7" class="text-center text-muted py-3">
                    No hay detalle por vendedor para el equipo seleccionado.
                </td>
            </tr>
        `;
            return;
        }

        tbody.innerHTML = filtrados.map(x => `
        <tr>
            <td>${x.equipo ?? x.Equipo ?? ""}</td>
            <td>${x.empleadoVentas ?? x.EmpleadoVentas ?? ""}</td>
            <td class="text-end">${formatCurrency(x.venta ?? x.Venta ?? 0)}</td>
            <td class="text-end">${formatCurrency(x.meta ?? x.Meta ?? 0)}</td>
            <td class="text-end">${formatNumber(x.cumplimientoPct ?? x.CumplimientoPct ?? 0)}%</td>
            <td class="text-end">${formatCurrency(x.brecha ?? x.Brecha ?? 0)}</td>
            <td class="text-center">${getEstadoVendedor(x.cumplimientoPct ?? x.CumplimientoPct ?? 0)}</td>
        </tr>
    `).join("");
    }




    function renderDetalleProductos(data) {
        const tbody = document.getElementById("tbodyDetalleProducto");
        if (!tbody) return;

        const equipoSeleccionado = document.getElementById("filtroEquipoProducto")?.value?.trim() || "";

        const filtrados = (data || []).filter(x => {
            const equipoFila = (x.equipo ?? x.Equipo ?? "").trim();

            if (!equipoSeleccionado || equipoSeleccionado === "Todos") return true;

            return equipoFila === equipoSeleccionado;
        });

        console.log("Equipo seleccionado producto:", equipoSeleccionado);
        console.log("Total productos:", data?.length || 0);
        console.log("Filtrados producto:", filtrados.length);

        if (filtrados.length === 0) {
            tbody.innerHTML = `
            <tr>
                <td colspan="5" class="text-center text-muted py-3">
                    No hay detalle por producto para el equipo seleccionado.
                </td>
            </tr>
        `;
            return;
        }

        tbody.innerHTML = filtrados.map(x => `
        <tr>
            <td>${x.empleadoVentas ?? x.EmpleadoVentas ?? ""}</td>
            <td>${x.codigo ?? x.Codigo ?? ""}</td>
            <td>${x.producto ?? x.Producto ?? ""}</td>
            <td class="text-end">${formatNumber(x.cantidad ?? x.Cantidad ?? 0)}</td>
            <td class="text-end">${formatCurrency(x.venta ?? x.Venta ?? 0)}</td>
        </tr>
    `).join("");
    }



    function renderAnalisisProducto(productos, equipoSeleccionado) {
        console.log("PRODUCTOS:", productos);
        console.log("EQUIPO SELECCIONADO:", equipoSeleccionado);

        const equipo = equipoSeleccionado || getEquipoSeleccionadoProducto();

        const filtrados = (productos || [])
            .filter(p => {
                if (!equipo) return true;

                return normalizarTexto(p.equipo || p.Equipo) === normalizarTexto(equipo);
            })
            .sort((a, b) => ((b.venta ?? b.Venta ?? 0) - (a.venta ?? a.Venta ?? 0)));

        const tbody = document.querySelector("#tblProductos tbody") || document.getElementById("tbodyDetalleProducto");
        if (!tbody) return;

        if (!filtrados.length) {
            tbody.innerHTML = `
            <tr>
                <td colspan="5" class="text-center text-muted py-3">
                    No hay detalle por producto para el equipo seleccionado.
                </td>
            </tr>`;

            if (typeof actualizarGraficoTopProductos === "function") {
                actualizarGraficoTopProductos([], []);
            }

            return;
        }

        tbody.innerHTML = filtrados.map(p => `
        <tr>
            <td>${p.empleadoVentas ?? p.EmpleadoVentas ?? ""}</td>
            <td>${p.codigo ?? p.Codigo ?? ""}</td>
            <td>${p.producto ?? p.Producto ?? ""}</td>
            <td class="text-end">${formatNumber(p.cantidad ?? p.Cantidad ?? 0)}</td>
            <td class="text-end">${formatCurrency(p.venta ?? p.Venta ?? 0)}</td>
        </tr>
    `).join("");

        const top5 = filtrados.slice(0, 5);

        if (typeof actualizarGraficoTopProductos === "function") {
            actualizarGraficoTopProductos(
                top5.map(x => x.producto ?? x.Producto ?? ""),
                top5.map(x => x.venta ?? x.Venta ?? 0)
            );
        }
    }

    // ==================================================================



    function renderClientesTop(data) {
        const tbody = document.getElementById("tbodyTopClientesEquipo");
        if (!tbody) return;

        const equipoSeleccionado =
            document.getElementById("filtroEquipoVendedor")?.value?.trim() || "";

        const filtrados = (data || [])
            .filter(x => {
                const equipoFila = (x.equipo ?? x.Equipo ?? "").trim();

                if (!equipoSeleccionado || equipoSeleccionado === "Todos") {
                    return true;
                }

                return equipoFila === equipoSeleccionado;
            })
            .sort((a, b) => (b.venta || 0) - (a.venta || 0))
            .slice(0, 5);

        console.log("CLIENTES TOP FILTRADOS:", filtrados);

        if (filtrados.length === 0) {
            tbody.innerHTML = `
            <tr>
                <td colspan="5" class="text-center text-muted py-3">
                    No hay clientes para el equipo seleccionado.
                </td>
            </tr>
        `;
            return;
        }

        tbody.innerHTML = filtrados.map(x => `
        <tr>
            <td>${x.cardCode ?? ""}</td>
            <td>${x.cardName ?? ""}</td>
            <td>${x.empleadoVentas ?? ""}</td>
            <td class="text-end">${formatCurrency(x.venta ?? 0)}</td>
            <td class="text-end">${formatNumber(x.aportePct ?? 0)}%</td>
        </tr>
    `).join("");
    }



    function actualizarTopClientesEquipo() {
        const filtro = document.getElementById("filtroEquipoVendedor");
        if (!filtro) return;

        const equipo = (filtro.value || "").trim().toUpperCase();

        const top5 = dataClientesTop
            .filter(x => ((x.equipo || "").trim().toUpperCase() === equipo))
            .sort((a, b) => (b.venta || 0) - (a.venta || 0))
            .slice(0, 5);

        renderClientesTop(top5);
    }

    function actualizarVistaVendedores() {
        const filtro = document.getElementById("filtroEquipoVendedor");
        const kpiVenta = document.getElementById("kpiVentaEquipoSel");
        const kpiMeta = document.getElementById("kpiMetaEquipoSel");

        if (!filtro || !kpiVenta || !kpiMeta) return;

        const equipo = filtro.value;

        const detalle = dataVendedores.filter(x => x.equipo === equipo);
        const resumenEquipo = dataEquipos.find(x => x.equipo === equipo);

        kpiVenta.textContent = formatCurrency(resumenEquipo?.venta ?? 0);
        kpiMeta.textContent = formatCurrency(resumenEquipo?.meta ?? 0);

        renderDetalleVendedores(detalle);
        actualizarTopClientesEquipo();
    }

    function actualizarVistaProductos() {
        const filtro = document.getElementById("filtroEquipoProducto");
        if (!filtro) return;

        const equipo = (filtro.value || "").trim().toUpperCase();

        const detalle = dataProductos.filter(x =>
            ((x.equipo || "").trim().toUpperCase() === equipo)
        );

        renderDetalleProductos(detalle);
        renderTopProductos(detalle);
    }

    function renderTopProductos(data) {
        if (!chartTopProductosCanvas) return;

        if (chartTopProductos) {
            chartTopProductos.destroy();
            chartTopProductos = null;
        }

        const top5 = [...data]
            .sort((a, b) => (b.venta || 0) - (a.venta || 0))
            .slice(0, 5);

        chartTopProductos = new Chart(chartTopProductosCanvas, {
            type: "bar",
            data: {
                labels: top5.map(x => x.producto),
                datasets: [{
                    label: "Venta",
                    data: top5.map(x => x.venta || 0),
                    backgroundColor: "#f0c64b",
                    borderColor: "#c7a63a",
                    borderWidth: 1,
                    borderRadius: 4
                }]
            },
            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false }
                },
                scales: {
                    x: {
                        ticks: {
                            callback(value) {
                                return new Intl.NumberFormat("es-CL", {
                                    notation: "compact",
                                    maximumFractionDigits: 1
                                }).format(value);
                            }
                        }
                    }
                }
            }
        });
    }

   

    function exportarExcelGerencial() {

        const wb = XLSX.utils.book_new();

        const resumenEquipos = dataEquipos.map(x => ({
            Equipo: x.equipo,
            Venta: x.venta,
            Meta: x.meta,
            "Cumplimiento %": x.cumplimientoPct,
            Brecha: x.brecha
        }));

        const wsEquipos = XLSX.utils.json_to_sheet(resumenEquipos);
        XLSX.utils.book_append_sheet(wb, wsEquipos, "Resumen Equipos");

        const vendedores = dataVendedores.map(x => ({
            Equipo: x.equipo,
            "Empleado Ventas": x.empleadoVentas,
            Venta: x.venta,
            Meta: x.meta,
            "Cumplimiento %": x.cumplimientoPct,
            Brecha: x.brecha
        }));

        const wsVendedores = XLSX.utils.json_to_sheet(vendedores);
        XLSX.utils.book_append_sheet(wb, wsVendedores, "Vendedores");

        const clientes = dataClientesTop.map(x => ({
            Equipo: x.equipo,
            Cliente: x.cardCode,
            "Nombre Cliente": x.cardName,
            Vendedor: x.empleadoVentas,
            Venta: x.venta,
            "% Aporte": x.aportePct
        }));

        const wsClientes = XLSX.utils.json_to_sheet(clientes);
        XLSX.utils.book_append_sheet(wb, wsClientes, "Top Clientes");

        const productos = dataProductos.map(x => ({
            Equipo: x.equipo,
            "Empleado Ventas": x.empleadoVentas,
            Código: x.codigo,
            Producto: x.producto,
            Cantidad: x.cantidad,
            Venta: x.venta
        }));

        const wsProductos = XLSX.utils.json_to_sheet(productos);
        XLSX.utils.book_append_sheet(wb, wsProductos, "Productos");

        const anio = document.getElementById("filtroAnio")?.value;
        const mes = document.getElementById("filtroMes")?.value;

        const nombreArchivo = `Reporte_Gerencial_${anio}_${mes}.xlsx`;

        XLSX.writeFile(wb, nombreArchivo);
    }

    /*
    async function actualizarDashboard() {
        await cargarProyeccionCierre();
    }*/

    async function actualizarDashboard() {
        await Promise.allSettled([
            cargarReporte(),
            cargarProyeccionCierre(),
            cargarEstadoCierreMes()
        ]);
    }



    if (btnActualizar) {
        btnActualizar.addEventListener("click", actualizarDashboard);
    }



    document.addEventListener("DOMContentLoaded", async () => {

        const btnExportar = document.getElementById("btnExportarExcel");
        const btnCerrarMes = document.getElementById("btnCerrarMes");
        const btnCerrarSesion = document.getElementById("btnCerrarSesion");

        if (btnExportar) {
            btnExportar.addEventListener("click", exportarExcelGerencial);
        }

        if (btnCerrarMes) {
            btnCerrarMes.addEventListener("click", cerrarOReprocesarMes);
        }

        if (btnCerrarSesion) {
            btnCerrarSesion.addEventListener("click", () => {
                localStorage.clear();
                sessionStorage.clear();
                window.location.href = "/login.html";
            });
        }

        mostrarSesionActual();

        await actualizarDashboard();
    });




    document.addEventListener("change", (e) => {

        if (e.target && e.target.id === "filtroEquipoVendedor") {
            console.log("Cambio equipo vendedor:", e.target.value);
            renderDetalleVendedores(dataVendedores || []);
            renderClientesTop(dataClientesTop || []);
        }

        if (e.target && e.target.id === "filtroEquipoProducto") {
            console.log("Cambio equipo producto:", e.target.value);
            renderDetalleProductos(dataProductos || []);
        }

        if (e.target && e.target.id === "selectEquipoProyeccionCierre") {
            renderDetalleProyeccionCierre();
        }
    });


    function getSesionActual() {
        const usuario =
            localStorage.getItem("currentLogin") ||
            localStorage.getItem("login") ||
            localStorage.getItem("nombre") ||
            "Sin usuario";

        let role =
            localStorage.getItem("role") ||
            localStorage.getItem("rol") ||
            localStorage.getItem("permiso") ||
            localStorage.getItem("currentRole") ||
            "";

        if (!role && usuario.toLowerCase().includes("admin")) {
            role = "ADMIN";
        }

        if (!role && usuario.toLowerCase().includes("administrador")) {
            role = "ADMIN";
        }

        return { usuario, role };
    }

    function getUserRole() {
        return (getSesionActual().role || "").toUpperCase().trim();
    }

    function esAdmin() {
        const role = getUserRole();
        console.log("ROL DETECTADO:", role);
        return role === "ADMIN";
    }

    function mostrarSesionActual() {
        const info = document.getElementById("infoSesion");
        if (!info) return;

        const sesion = getSesionActual();
        info.textContent = `Usuario: ${sesion.usuario} | Rol: ${sesion.role || "Sin rol"}`;
    }

    async function cargarEstadoCierreMes() {
        console.log("Ejecutando cargarEstadoCierreMes");
        console.log("Es admin:", esAdmin());

        const panel = document.getElementById("panelCierreMes");
        const badge = document.getElementById("badgeEstadoCierre");
        const boton = document.getElementById("btnCerrarMes");

        if (!panel || !badge || !boton) {
            console.warn("No se encontró panel, badge o botón cierre mes");
            return;
        }

        if (!esAdmin()) {
            panel.style.display = "none";
            panel.style.visibility = "hidden";
            panel.style.opacity = "0";

            boton.style.display = "none";
            boton.style.visibility = "hidden";
            boton.style.opacity = "0";

            return;
        }

        panel.style.display = "block";
        panel.style.visibility = "visible";
        panel.style.opacity = "1";

        boton.style.display = "inline-block";
        boton.style.visibility = "visible";
        boton.style.opacity = "1";

        const anio = document.getElementById("filtroAnio")?.value;
        const mes = document.getElementById("filtroMes")?.value;
        const categoriaVenta = document.getElementById("filtroCategoria")?.value || "Ventas Quimicos";
        const division = document.getElementById("filtroDivision")?.value || "Todas";

        const url = `/api/gerencia/cierre-mensual?anio=${anio}&mes=${mes}&categoriaVenta=${encodeURIComponent(categoriaVenta)}&division=${encodeURIComponent(division)}`;

        try {
            const response = await fetch(url, {
                headers: getAuthHeaders()
            });

            const result = await response.json();

            if (!result.ok || !result.data || result.data.length === 0) {
                badge.className = "badge bg-secondary";
                badge.textContent = "Sin cierre";
                return;
            }

            const estado = result.data[0].estado || "CERRADO";

            if (estado === "REPROCESADO") {
                badge.className = "badge bg-warning text-dark";
                badge.textContent = "MES REPROCESADO";
            } else {
                badge.className = "badge bg-success";
                badge.textContent = "MES CERRADO";
            }

        } catch (error) {
            console.error("Error cierre mensual:", error);
            badge.className = "badge bg-danger";
            badge.textContent = "Error cierre";
        }
    }

    async function cerrarOReprocesarMes() {
        const anio = document.getElementById("filtroAnio")?.value;
        const mes = document.getElementById("filtroMes")?.value;
        const categoriaVenta = document.getElementById("filtroCategoria")?.value || "Ventas Quimicos";
        const division = document.getElementById("filtroDivision")?.value || "Todas";

        const observacion = prompt(
            "Ingrese observación del cierre/reproceso:",
            "Cierre mensual validado contra SAP"
        );

        if (observacion === null) return;

        const confirmar = confirm(
            `¿Confirmas cerrar/reprocesar el mes ${mes}/${anio}?\n\n` +
            `Esta acción actualizará el cierre histórico y quedará auditada.`
        );

        if (!confirmar) return;

        const url = `/api/gerencia/cerrar-mes?anio=${anio}&mes=${mes}&categoriaVenta=${encodeURIComponent(categoriaVenta)}&division=${encodeURIComponent(division)}&forzarReproceso=true&observacion=${encodeURIComponent(observacion)}`;

        const response = await fetch(url, {
            method: "POST",
            headers: getAuthHeaders()
        });

        const result = await response.json();

        if (!result.ok) {
            alert(result.mensaje || "Error al cerrar/reprocesar mes.");
            return;
        }

        alert(result.mensaje);
        await actualizarDashboard();
    }

    document.addEventListener("DOMContentLoaded", async () => {
        const btnExportar = document.getElementById("btnExportarExcel");
        const btnCerrarMes = document.getElementById("btnCerrarMes");
        const btnCerrarSesion = document.getElementById("btnCerrarSesion");

        if (btnExportar) {
            btnExportar.addEventListener("click", exportarExcelGerencial);
        }

        if (btnCerrarMes) {
            btnCerrarMes.addEventListener("click", cerrarOReprocesarMes);
        }

        if (btnCerrarSesion) {
            btnCerrarSesion.addEventListener("click", () => {
                localStorage.clear();
                sessionStorage.clear();
                window.location.href = "/login.html";
            });
        }

        mostrarSesionActual();
        await actualizarDashboard();
    });

    document.addEventListener("change", (e) => {
        if (e.target && e.target.id === "filtroEquipoVendedor") {
            renderDetalleVendedores(dataVendedores || []);
            renderClientesTop(dataClientesTop || []);
        }

        if (e.target && e.target.id === "filtroEquipoProducto") {
            renderDetalleProductos(dataProductos || []);
        }

        if (e.target && e.target.id === "selectEquipoProyeccionCierre") {
            renderDetalleProyeccionCierre();
        }
    });
    



})();


