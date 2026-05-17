console.log("Mis Rendiciones cargado");

let rendiciones = [];

document.addEventListener("DOMContentLoaded", async function () {
    await cargarMisRendiciones();

    document.getElementById("txtBuscar").addEventListener("keyup", function () {
        const texto = this.value.toLowerCase();

        const filtrado = rendiciones.filter(r =>
            obtener(r, "Proveedor").toLowerCase().includes(texto) ||
            obtener(r, "TipoGasto").toLowerCase().includes(texto) ||
            obtener(r, "TipoDocumento").toLowerCase().includes(texto) ||
            obtener(r, "Estado").toLowerCase().includes(texto)
        );

        cargarTabla(filtrado);
    });
});

async function cargarMisRendiciones() {
    try {
        const response = await fetch("/api/rindespartan/mis-rendiciones");
        const result = await response.json();

        if (!response.ok || !result.ok) {
            mostrarTablaVacia("No se pudieron cargar las rendiciones.");
            return;
        }

        rendiciones = result.data || [];

        cargarTabla(rendiciones);
        actualizarResumen();

    } catch (error) {
        console.error(error);
        mostrarTablaVacia("Error de comunicación con la API.");
    }
}

function cargarTabla(datos) {
    const tbody = document.getElementById("tbodyRendiciones");
    tbody.innerHTML = "";

    if (!datos || datos.length === 0) {
        mostrarTablaVacia("No existen rendiciones registradas.");
        return;
    }

    datos.forEach(r => {
        const folioRinde = obtener(r, "FolioRinde") || `RS-${obtener(r, "Id")}`;
        const fechaDocumento = obtener(r, "FechaDocumento");
        const tipoDocumento = obtener(r, "TipoDocumento");
        const proveedor = obtener(r, "Proveedor");
        const tipoGasto = obtener(r, "TipoGasto");
        const estado = obtener(r, "Estado").toUpperCase();
        const rutaArchivo = obtener(r, "RutaArchivo");
        const monto = Number(obtener(r, "Monto") || 0);

        let badgeEstado = "";

        switch (estado) {
            case "PENDIENTE":
                badgeEstado = `<span class="badge bg-warning text-dark">Pendiente</span>`;
                break;
            case "APROBADO":
                badgeEstado = `<span class="badge bg-success">Aprobado</span>`;
                break;
            case "RECHAZADO":
                badgeEstado = `<span class="badge bg-danger">Rechazado</span>`;
                break;
            default:
                badgeEstado = `<span class="badge bg-secondary">${estado || "-"}</span>`;
                break;
        }

        const observacion =
            estado === "RECHAZADO"
                ? obtener(r, "ObservacionRechazo")
                : obtener(r, "ObservacionAprobador");

        const botonVer = rutaArchivo
            ? `<a class="btn btn-sm btn-outline-dark" href="${rutaArchivo}" target="_blank">Ver</a>`
            : `<button class="btn btn-sm btn-outline-secondary" disabled>Sin archivo</button>`;

        const fila = `
            <tr>
               <td>${folioRinde}</td>
                <td>${formatearFecha(fechaDocumento)}</td>
                <td>${tipoDocumento || "-"}</td>
                <td>${proveedor || "-"}</td>
                <td>${tipoGasto || "-"}</td>
                <td>${formatoMoneda(monto)}</td>
                <td>${badgeEstado}</td>
                <td>${observacion || "-"}</td>
                <td>${botonVer}</td>
            </tr>
        `;

        tbody.innerHTML += fila;
    });
}

function actualizarResumen() {
    const total = rendiciones.reduce((a, b) => a + Number(obtener(b, "Monto") || 0), 0);

    const pendiente = rendiciones
        .filter(x => obtener(x, "Estado").toUpperCase() === "PENDIENTE")
        .reduce((a, b) => a + Number(obtener(b, "Monto") || 0), 0);

    const aprobado = rendiciones
        .filter(x => obtener(x, "Estado").toUpperCase() === "APROBADO")
        .reduce((a, b) => a + Number(obtener(b, "Monto") || 0), 0);

    document.getElementById("totalRendido").innerText = formatoMoneda(total);
    document.getElementById("pendienteAprobacion").innerText = formatoMoneda(pendiente);
    document.getElementById("totalAprobado").innerText = formatoMoneda(aprobado);
}

function obtener(obj, campo) {
    return obj[campo] ?? obj[campo.charAt(0).toLowerCase() + campo.slice(1)] ?? "";
}

function mostrarTablaVacia(mensaje) {
    document.getElementById("tbodyRendiciones").innerHTML = `
        <tr>
            <td colspan="9" class="text-center text-muted py-4">
                ${mensaje}
            </td>
        </tr>
    `;

    document.getElementById("totalRendido").innerText = formatoMoneda(0);
    document.getElementById("pendienteAprobacion").innerText = formatoMoneda(0);
    document.getElementById("totalAprobado").innerText = formatoMoneda(0);
}

function formatoMoneda(valor) {
    return Number(valor).toLocaleString("es-CL", {
        style: "currency",
        currency: "CLP"
    });
}

function formatearFecha(fecha) {
    if (!fecha) return "-";

    const d = new Date(fecha);
    if (isNaN(d.getTime())) return fecha;

    return d.toLocaleDateString("es-CL");
}