console.log("Nueva Rendición múltiple cargada");

let documentos = [];

document.addEventListener("DOMContentLoaded", function () {

    const form = document.getElementById("formNuevaRendicion");
    const btnAgregar = document.getElementById("btnAgregarDocumento");
    const fechaDoc = document.getElementById("docFechaDocumento");

    fechaDoc.valueAsDate = new Date();

    btnAgregar.addEventListener("click", agregarDocumento);

    form.addEventListener("submit", guardarRendicion);
});

function agregarDocumento() {
    limpiarMensaje();

    const tipoDocumento = document.getElementById("docTipoDocumento").value;
    const fechaDocumento = document.getElementById("docFechaDocumento").value;
    const numeroDocumento = document.getElementById("docNumeroDocumento").value.trim();
    const proveedor = document.getElementById("docProveedor").value.trim();
    const monto = Number(document.getElementById("docMonto").value);
    const archivo = document.getElementById("docArchivo").files[0];

    if (!tipoDocumento) return mostrarError("Debe seleccionar el tipo de documento.");
    if (!fechaDocumento) return mostrarError("Debe indicar la fecha del documento.");
    if (!proveedor) return mostrarError("Debe ingresar proveedor.");
    if (!monto || monto <= 0) return mostrarError("Debe ingresar un monto válido.");
    if (!archivo) return mostrarError("Debe adjuntar un archivo.");

    const validacion = validarArchivo(archivo);
    if (!validacion.ok) return mostrarError(validacion.mensaje);

    documentos.push({
        tipoDocumento,
        fechaDocumento,
        numeroDocumento,
        proveedor,
        monto,
        archivo
    });

    limpiarDocumentoForm();
    renderDocumentos();
}

async function guardarRendicion(e) {
    e.preventDefault();
    limpiarMensaje();

    const tipoGasto = document.getElementById("tipoGasto").value;
    const justificacion = document.getElementById("justificacion").value.trim();

    if (!tipoGasto) return mostrarError("Debe seleccionar el tipo de gasto.");
    if (!justificacion) return mostrarError("Debe ingresar una justificación general.");
    if (documentos.length === 0) return mostrarError("Debe agregar al menos un documento.");

    const formData = new FormData();

    formData.append("TipoGasto", tipoGasto);
    formData.append("Justificacion", justificacion);

    documentos.forEach((doc, index) => {
        formData.append(`Documentos[${index}].FechaDocumento`, doc.fechaDocumento);
        formData.append(`Documentos[${index}].TipoDocumento`, doc.tipoDocumento);
        formData.append(`Documentos[${index}].NumeroDocumento`, doc.numeroDocumento);
        formData.append(`Documentos[${index}].Proveedor`, doc.proveedor);
        formData.append(`Documentos[${index}].Monto`, doc.monto);
        formData.append(`Documentos[${index}].ArchivoDocumento`, doc.archivo);
    });

    try {
        mostrarInfo("Guardando rendición, por favor espere...");

        const response = await fetch("/api/rindespartan/nueva", {
            method: "POST",
            body: formData
        });

        const result = await response.json();

        if (!response.ok || !result.ok) {
            mostrarError(result.mensaje || "No se pudo guardar la rendición.");
            return;
        }

        mostrarExito(result.mensaje || "Rendición registrada correctamente.");

        documentos = [];
        renderDocumentos();

        setTimeout(function () {
            location.href = "mis_rendiciones.html";
        }, 1200);

    } catch (error) {
        console.error(error);
        mostrarError("Error de comunicación con la API.");
    }
}

function renderDocumentos() {
    const tbody = document.getElementById("tbodyDocumentos");
    tbody.innerHTML = "";

    if (documentos.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="7" class="text-center text-muted py-3">
                    No hay documentos agregados.
                </td>
            </tr>
        `;
        actualizarTotal();
        return;
    }

    documentos.forEach((doc, index) => {
        const fila = `
            <tr>
                <td>${doc.tipoDocumento}</td>
                <td>${formatearFecha(doc.fechaDocumento)}</td>
                <td>${doc.numeroDocumento || "-"}</td>
                <td>${doc.proveedor}</td>
                <td>${formatoMoneda(doc.monto)}</td>
                <td>${doc.archivo.name}</td>
                <td>
                    <button type="button" class="btn btn-sm btn-outline-danger"
                            onclick="quitarDocumento(${index})">
                        Quitar
                    </button>
                </td>
            </tr>
        `;

        tbody.innerHTML += fila;
    });

    actualizarTotal();
}

function quitarDocumento(index) {
    documentos.splice(index, 1);
    renderDocumentos();
}

function actualizarTotal() {
    const total = documentos.reduce((a, b) => a + Number(b.monto || 0), 0);
    document.getElementById("totalRendicion").innerText = formatoMoneda(total);
}

function limpiarDocumentoForm() {
    document.getElementById("docTipoDocumento").value = "";
    document.getElementById("docFechaDocumento").valueAsDate = new Date();
    document.getElementById("docNumeroDocumento").value = "";
    document.getElementById("docProveedor").value = "";
    document.getElementById("docMonto").value = "";
    document.getElementById("docArchivo").value = "";
}

function validarArchivo(archivo) {
    const extensionesPermitidas = ["pdf", "jpg", "jpeg", "png"];
    const maxMB = 5;
    const extension = archivo.name.split(".").pop().toLowerCase();
    const pesoMB = archivo.size / (1024 * 1024);

    if (!extensionesPermitidas.includes(extension)) {
        return {
            ok: false,
            mensaje: "Formato no permitido. Solo se aceptan PDF, JPG, JPEG o PNG."
        };
    }

    if (pesoMB > maxMB) {
        return {
            ok: false,
            mensaje: "El archivo supera el tamaño máximo permitido de 5 MB."
        };
    }

    return { ok: true };
}

function mostrarError(texto) {
    const mensaje = document.getElementById("mensajeValidacion");
    mensaje.className = "alert alert-danger mt-4";
    mensaje.innerText = texto;
    mensaje.classList.remove("d-none");
}

function mostrarExito(texto) {
    const mensaje = document.getElementById("mensajeValidacion");
    mensaje.className = "alert alert-success mt-4";
    mensaje.innerText = texto;
    mensaje.classList.remove("d-none");
}

function mostrarInfo(texto) {
    const mensaje = document.getElementById("mensajeValidacion");
    mensaje.className = "alert alert-info mt-4";
    mensaje.innerText = texto;
    mensaje.classList.remove("d-none");
}

function limpiarMensaje() {
    const mensaje = document.getElementById("mensajeValidacion");
    mensaje.className = "alert mt-4 d-none";
    mensaje.innerText = "";
}