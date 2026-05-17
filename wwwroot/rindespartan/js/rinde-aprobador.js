console.log("Panel aprobador cargado");

let rendiciones = [];
let rendicionSeleccionada = null;

const TOPE_MENSUAL_TEMPORAL = 350000;

document.addEventListener("DOMContentLoaded", async function () {

    await cargarPendientes();

    const buscador = document.getElementById("txtBuscar");

    if (buscador) {
        buscador.addEventListener("keyup", function () {
            const texto = this.value.toLowerCase();

            const filtrado = rendiciones.filter(r =>
                obtener(r, "Proveedor").toLowerCase().includes(texto) ||
                obtener(r, "TipoGasto").toLowerCase().includes(texto) ||
                obtener(r, "TipoDocumento").toLowerCase().includes(texto) ||
                obtener(r, "Justificacion").toLowerCase().includes(texto)
            );

            cargarTabla(filtrado);
        });
    }

    document.getElementById("btnAprobar")?.addEventListener("click", aprobarRendicion);
    document.getElementById("btnRechazar")?.addEventListener("click", rechazarRendicion);
});

async function cargarPendientes() {
    try {
        const response = await fetch("/api/rindespartan/pendientes-aprobacion");
        const result = await response.json();

        if (!response.ok || !result.ok) {
            console.error(result);
            return;
        }

        rendiciones = result.data || [];
        cargarTabla(rendiciones);
        actualizarResumen();

    } catch (error) {
        console.error(error);
    }
}

function cargarTabla(datos) {
    const tbody = document.getElementById("tbodyPendientes");
    tbody.innerHTML = "";

    if (!datos || datos.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="8" class="text-center text-muted py-4">
                    No existen rendiciones pendientes.
                </td>
            </tr>
        `;
        actualizarResumen();
        return;
    }

    datos.forEach(r => {
        const id = obtener(r, "Id");
        const monto = Number(obtener(r, "Monto") || 0);
        const saldoUsuario = TOPE_MENSUAL_TEMPORAL - monto;

        const fila = `
            <tr>
                <td>${formatearFecha(obtener(r, "FechaDocumento"))}</td>
                <td>Usuario ${obtener(r, "UsuarioId")}</td>
                <td>${obtener(r, "TipoDocumento")}</td>
                <td>${obtener(r, "Proveedor")}</td>
                <td>${obtener(r, "TipoGasto")}</td>
                <td>${formatoMoneda(monto)}</td>
                <td>${formatoMoneda(saldoUsuario)}</td>
                <td>
                    <button class="btn btn-sm btn-outline-dark" onclick="abrirDetalle(${id})">
                        Revisar
                    </button>
                </td>
            </tr>
        `;

        tbody.innerHTML += fila;
    });
}

async function abrirDetalle(id) {
    try {
        const response = await fetch(`/api/rindespartan/detalle/${id}`);
        const result = await response.json();

        if (!response.ok || !result.ok) {
            alert(result.mensaje || "No se pudo obtener el detalle.");
            return;
        }

        const r = result.cabecera;
        const adjuntos = result.adjuntos || [];

        rendicionSeleccionada = r;

        const monto = Number(obtener(r, "Monto") || 0);
        const saldoUsuario = TOPE_MENSUAL_TEMPORAL - monto;

        document.getElementById("modalUsuario").innerText =
            "Usuario " + obtener(r, "UsuarioId");

        document.getElementById("modalFecha").innerText =
            formatearFecha(obtener(r, "FechaDocumento"));

        document.getElementById("modalProveedor").innerText =
            obtener(r, "Proveedor");

        document.getElementById("modalMonto").innerText =
            formatoMoneda(monto);

        document.getElementById("modalDocumento").innerText =
            obtener(r, "FolioRinde") || `RS-${obtener(r, "Id")}`;

        document.getElementById("modalTipoGasto").innerText =
            obtener(r, "TipoGasto");

        document.getElementById("modalTope").innerText =
            formatoMoneda(TOPE_MENSUAL_TEMPORAL);

        document.getElementById("modalSaldo").innerText =
            formatoMoneda(saldoUsuario);

        document.getElementById("modalJustificacion").innerText =
            obtener(r, "Justificacion");

        document.getElementById("observacionAprobador").value = "";

        cargarAdjuntosDetalle(adjuntos);

        limpiarMensaje();

        const modal = new bootstrap.Modal(
            document.getElementById("modalDetalleRendicion")
        );

        modal.show();

    } catch (error) {
        console.error(error);
        alert("Error de comunicación al obtener detalle.");
    }
}

function cargarAdjuntosDetalle(adjuntos) {
    const tbody = document.getElementById("tbodyDetalleAdjuntos");

    if (!tbody) return;

    tbody.innerHTML = "";

    if (!adjuntos || adjuntos.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="6" class="text-center text-muted py-3">
                    Sin documentos asociados.
                </td>
            </tr>
        `;
        return;
    }

    adjuntos.forEach(a => {
        const ruta = obtener(a, "RutaArchivo");

        const boton = ruta
            ? `<a class="btn btn-sm btn-outline-dark" href="${ruta}" target="_blank">Ver</a>`
            : `<button class="btn btn-sm btn-outline-secondary" disabled>Sin archivo</button>`;

        const fila = `
            <tr>
                <td>${obtener(a, "TipoDocumento") || "-"}</td>
                <td>${obtener(a, "NumeroDocumento") || "-"}</td>
                <td>${obtener(a, "Proveedor") || "-"}</td>
                <td>${formatoMoneda(obtener(a, "Monto") || 0)}</td>
                <td>${obtener(a, "NombreArchivo") || "-"}</td>
                <td>${boton}</td>
            </tr>
        `;

        tbody.innerHTML += fila;
    });
}







async function aprobarRendicion() {
    if (!rendicionSeleccionada) return;

    const observacion = document.getElementById("observacionAprobador").value.trim();

    await enviarAprobacion("/api/rindespartan/aprobar", {
        solicitudId: Number(obtener(rendicionSeleccionada, "Id")),
        observacion: observacion
    });
}

async function rechazarRendicion() {
    if (!rendicionSeleccionada) return;

    const observacion = document.getElementById("observacionAprobador").value.trim();

    if (!observacion) {
        mostrarMensaje("danger", "Para rechazar debe ingresar una observación.");
        return;
    }

    await enviarAprobacion("/api/rindespartan/rechazar", {
        solicitudId: Number(obtener(rendicionSeleccionada, "Id")),
        observacion: observacion
    });
}

async function enviarAprobacion(url, payload) {
    try {
        mostrarMensaje("info", "Procesando solicitud...");

        const response = await fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });

        const result = await response.json();

        if (!response.ok || !result.ok) {
            mostrarMensaje("danger", result.mensaje || "No se pudo procesar la solicitud.");
            return;
        }

        mostrarMensaje("success", result.mensaje || "Solicitud procesada correctamente.");

        setTimeout(async function () {
            const modalElement = document.getElementById("modalDetalleRendicion");
            const modal = bootstrap.Modal.getInstance(modalElement);

            if (modal) {
                modal.hide();
            }

            await cargarPendientes();

        }, 900);

    } catch (error) {
        console.error(error);
        mostrarMensaje("danger", "Error de comunicación con la API.");
    }
}

function actualizarResumen() {
    const pendientes = rendiciones.length;

    const monto = rendiciones.reduce((a, b) =>
        a + Number(obtener(b, "Monto") || 0), 0);

    document.getElementById("cantidadPendientes").innerText = pendientes;
    document.getElementById("montoPendiente").innerText = formatoMoneda(monto);
    document.getElementById("aprobadasMes").innerText = "0";
}

function mostrarMensaje(tipo, texto) {
    const mensaje = document.getElementById("mensajeAprobacion");
    mensaje.className = `alert alert-${tipo} mt-3`;
    mensaje.innerText = texto;
    mensaje.classList.remove("d-none");
}

function limpiarMensaje() {
    const mensaje = document.getElementById("mensajeAprobacion");
    mensaje.className = "alert mt-3 d-none";
    mensaje.innerText = "";
}

function obtener(obj, campo) {
    return obj[campo]
        ?? obj[campo.charAt(0).toLowerCase() + campo.slice(1)]
        ?? "";
}