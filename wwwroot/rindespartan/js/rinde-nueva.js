console.log("RindeSpartan - Formato tradicional cargado");

let documentos = [];
let guardandoRendicion = false;
let leyendoComprobante = false;
let secuenciaLectura = 0;
let comprobanteRevisado = false;
let archivoLeido = null;

const CATEGORIAS = [
    "ALOJAMIENTO",
    "COMIDAS",
    "TRANSPORTE",
    "COMBUSTIBLE",
    "OTROS"
];

document.addEventListener("DOMContentLoaded", () => {

    const hoy = fechaLocalISO();

    document.getElementById("periodoGasto").value = hoy.slice(0, 7);
    document.getElementById("fechaRendicion").value = hoy;

    document.getElementById("docFechaDocumento").value = "";

    const nombre = document.getElementById("nombreUsuario")
        ?.textContent?.trim();

    document.getElementById("empleado").value =
        nombre && nombre !== "Usuario" ? nombre : "";

    document.getElementById("btnAgregarDocumento")
        .addEventListener("click", agregarDocumento);

    document.getElementById("formNuevaRendicion")
        .addEventListener("submit", guardarRendicion);

    document.getElementById("provisionAnticipo")
        .addEventListener("input", actualizarTotales);

    configurarLecturaComprobante();
    configurarConfirmacionDocumento();
    renderDocumentos();
});


// Vista del archivo original y confirmación de los datos declarados.
let urlVistaPrevia = null;
function configurarConfirmacionDocumento() {
    const campos = ["docTipoDocumento", "docFechaDocumento", "docNumeroDocumento", "docProveedor", "docMonto"];
    for (const id of campos) {
        document.getElementById(id)?.addEventListener("input", () => {
            document.getElementById("docDatosConfirmados").checked = false;
        });
    }
    document.getElementById("docArchivo")?.addEventListener("change", () => {
        document.getElementById("docDatosConfirmados").checked = false;
        mostrarVistaPreviaComprobante();
    });
    mostrarVistaPreviaComprobante();
}
function mostrarVistaPreviaComprobante() {
    const archivo = document.getElementById("docArchivo").files[0];
    const contenedor = document.getElementById("vistaPreviaComprobante");
    if (urlVistaPrevia) {
        URL.revokeObjectURL(urlVistaPrevia);
        urlVistaPrevia = null;
    }
    contenedor.replaceChildren();
    if (!archivo) {
        contenedor.classList.add("d-none");
        return;
    }
    contenedor.classList.remove("d-none");
    urlVistaPrevia = URL.createObjectURL(archivo);
    const enlace = document.createElement("a");
    enlace.href = urlVistaPrevia;
    enlace.target = "_blank";
    enlace.rel = "noopener noreferrer";
    enlace.className = "d-inline-block mb-2";
    enlace.textContent = "Abrir comprobante original en otra pestaña";
    contenedor.appendChild(enlace);
    if (archivo.type.startsWith("image/")) {
        const imagen = document.createElement("img");
        imagen.src = urlVistaPrevia;
        imagen.alt = "Vista previa del comprobante adjunto";
        imagen.className = "img-fluid d-block border rounded";
        imagen.style.maxHeight = "440px";
        contenedor.appendChild(imagen);
    } else if (archivo.type === "application/pdf" || archivo.name.toLowerCase().endsWith(".pdf")) {
        const iframe = document.createElement("iframe");
        iframe.src = urlVistaPrevia;
        iframe.title = "Vista previa del comprobante PDF";
        iframe.className = "w-100 border rounded";
        iframe.style.height = "360px";
        contenedor.appendChild(iframe);
    }
}

// LECTURA AUTOMÁTICA DE COMPROBANTES PDF E IMÁGENES
function configurarLecturaComprobante() {
    const input = document.getElementById("docArchivo");
    const aviso = document.createElement("div");
    aviso.id = "estadoLecturaComprobante";
    aviso.className = "small mt-2";
    aviso.setAttribute("role", "status");
    aviso.setAttribute("aria-live", "polite");
    input.insertAdjacentElement("afterend", aviso);

    input.addEventListener("change", leerComprobanteSeleccionado);
}

function estadoLectura(mensaje, clase = "text-muted") {
    const aviso = document.getElementById("estadoLecturaComprobante");
    aviso.className = `small mt-2 ${clase}`;
    aviso.textContent = mensaje;
}

function limpiarCamposNuevoComprobante() {
    const campos = [
        "docTipoDocumento",
        "docFechaDocumento",
        "docNumeroDocumento",
        "docProveedor",
        "docMonto"
    ];

    for (const id of campos) {
        const elemento = document.getElementById(id);

        if (elemento) {
            elemento.value = "";
        }
    }

    // El trabajador debe confirmar cada comprobante nuevo.
    const confirmacion =
        document.getElementById("docDatosConfirmados");

    if (confirmacion) {
        confirmacion.checked = false;
    }

    comprobanteRevisado = false;
    archivoLeido = null;
}





async function leerComprobanteSeleccionado() {

    // Invalida cualquier respuesta OCR de un archivo anterior.
    const secuencia = ++secuenciaLectura;

    // Limpia todos los datos asociados al comprobante anterior.
    limpiarCamposNuevoComprobante();

    const archivo =
        document.getElementById("docArchivo").files[0];

    if (!archivo) {
        estadoLectura("");
        leyendoComprobante = false;

        document.getElementById(
            "btnAgregarDocumento"
        ).disabled = false;

        return;
    }


    
    const validacion = validarArchivo(archivo);
    if (!validacion.ok) {
        estadoLectura(validacion.mensaje, "text-danger");
        return;
    }

    const esPdf = archivo.name.toLowerCase().endsWith(".pdf");

    const ids = [
        "docTipoDocumento",
        "docFechaDocumento",
        "docNumeroDocumento",
        "docProveedor",
        "docMonto"
    ];

    const valoresIniciales = Object.fromEntries(
        ids.map(id => [id, document.getElementById(id).value])
    );

    const botonAgregar = document.getElementById("btnAgregarDocumento");
    leyendoComprobante = true;
    botonAgregar.disabled = true;

    estadoLectura(
        esPdf
            ? "Analizando comprobante PDF..."
            : "Analizando imagen y reconociendo los datos del comprobante...",
        "text-primary"
    );

    try {
        const datos = new FormData();
        datos.append("archivo", archivo);


        const token = localStorage.getItem("jwtToken");

        if (!token) {
            estadoLectura(
                "Debe iniciar sesión en SpartanCloud para leer comprobantes.",
                "text-danger"
            );
            comprobanteRevisado = false;
            return;
        }

        const respuesta = await fetch("/api/rindespartan/leer-comprobante", {
            method: "POST",
            headers: {
                Authorization: `Bearer ${token}`
            },
            credentials: "same-origin",
            body: datos
        });


        if (secuencia !== secuenciaLectura) return;

        if (respuesta.status === 401) {
            estadoLectura(
                "Su sesión no está autenticada. Ingrese nuevamente a SpartanCloud antes de leer el comprobante.",
                "text-danger"
            );
            comprobanteRevisado = true;
            return;
        }

        if (respuesta.status === 403) {
            estadoLectura(
                "Su usuario no tiene autorización para utilizar el lector.",
                "text-danger"
            );
            comprobanteRevisado = true;
            return;
        }

        if (!respuesta.ok) {
            estadoLectura(
                `No se pudo leer el comprobante (HTTP ${respuesta.status}). Complete los datos manualmente y revise el archivo.`,
                "text-warning"
            );
            comprobanteRevisado = true;
            return;
        }

        const resultado = await respuesta.json();

        if (secuencia !== secuenciaLectura) return;

        archivoLeido = archivo;

        if (resultado.metodoLectura === "REQUIERE_OCR") {
            estadoLectura(
                "El PDF es una imagen escaneada y requiere OCR. Complete los datos manualmente.",
                "text-warning"
            );
            comprobanteRevisado = true;
            return;
        }

        const fecha = resultado.fechaDocumento
            ? String(resultado.fechaDocumento).slice(0, 10)
            : "";

        const campos = {
            docTipoDocumento: resultado.tipoDocumento || "",
            docFechaDocumento: fecha,
            docNumeroDocumento: resultado.numeroDocumento || "",
            docProveedor: resultado.proveedor || "",
            docMonto: resultado.montoTotal != null
                ? String(resultado.montoTotal)
                : ""
        };

        let actualizados = 0;
        let conflictos = 0;

        for (const [id, valor] of Object.entries(campos)) {
            if (!valor) continue;

            const elemento = document.getElementById(id);

            if (elemento.value !== valoresIniciales[id]) {
                conflictos++;
                continue;
            }

            if (
                id === "docTipoDocumento" &&
                !Array.from(elemento.options).some(
                    op => op.value === valor
                )
            ) {
                conflictos++;
                continue;
            }

            elemento.value = valor;
            actualizados++;
        }

        comprobanteRevisado = true;
        document.getElementById("docDatosConfirmados").checked = false;

        const advertencias = Array.isArray(resultado.advertencias)
            ? resultado.advertencias
            : [];

        const nota = advertencias.length
            ? ` Advertencias: ${advertencias.join(" ")}`
            : "";

        const conflicto = conflictos
            ? ` ${conflictos} campo(s) modificado(s) por usted no se sobrescribieron.`
            : "";

        if (
            resultado.lecturaExitosa &&
            !advertencias.length &&
            !conflictos
        ) {
            estadoLectura(
                `Comprobante leído: ${actualizados} campos completados. Revise y confirme los datos antes de agregar el gasto.`,
                "text-success"
            );
        } else {
            estadoLectura(
                `Lectura parcial: ${actualizados} campos completados. Revise los datos manualmente.${conflicto}${nota}`,
                "text-warning"
            );
        }

    } catch (error) {
        if (secuencia !== secuenciaLectura) return;

        console.error("Lectura de comprobante:", error);

        estadoLectura(
            "No fue posible comunicarse con el lector de comprobantes. Revise e ingrese los datos manualmente.",
            "text-warning"
        );

        comprobanteRevisado = true;

    } finally {
        if (secuencia === secuenciaLectura) {
            leyendoComprobante = false;
            botonAgregar.disabled = false;
        }
    }
}

function fechaLocalISO() {
    const fecha = new Date();
    const local = new Date(
        fecha.getTime() - fecha.getTimezoneOffset() * 60000
    );
    return local.toISOString().slice(0, 10);
}

function agregarDocumento() {
    limpiarMensaje();

    if (leyendoComprobante)
        return mostrarError(
            "Espere a que finalice la lectura del comprobante."
        );

    if (!comprobanteRevisado)
        return mostrarError(
            "Seleccione un comprobante válido y espere su revisión."
        );

    const tipoDocumento =
        document.getElementById("docTipoDocumento").value;

    const fechaDocumento =
        document.getElementById("docFechaDocumento").value;

    const numeroDocumento =
        document.getElementById("docNumeroDocumento").value.trim();

    const proveedor =
        document.getElementById("docProveedor").value.trim();

    const descripcion =
        document.getElementById("docDescripcionGasto").value.trim();

    const categoria =
        document.getElementById("docCategoriaGasto").value;

    const monto =
        Number(document.getElementById("docMonto").value);

    const archivo =
        document.getElementById("docArchivo").files[0];

    if (!tipoDocumento)
        return mostrarError("Seleccione el tipo de documento.");

    if (!fechaDocumento)
        return mostrarError("Indique la fecha del gasto.");

    if (!numeroDocumento)
        return mostrarError("Ingrese el número de boleta u operación; si no existe, escriba SIN FOLIO.");

    if (!proveedor)
        return mostrarError("Ingrese el acreedor o proveedor.");

    if (!descripcion)
        return mostrarError("Ingrese la descripción del gasto.");

    if (!CATEGORIAS.includes(categoria))
        return mostrarError("Seleccione una categoría de gasto.");

    if (!Number.isSafeInteger(monto) || monto <= 0)
        return mostrarError("Ingrese un monto válido en pesos.");

    if (!archivo)
        return mostrarError("Debe adjuntar el comprobante.");

    if (!document.getElementById("docDatosConfirmados").checked)
        return mostrarError("Revise la imagen y confirme los datos del gasto antes de agregarlo.");

    const validacion = validarArchivo(archivo);

    if (!validacion.ok)
        return mostrarError(validacion.mensaje);

    documentos.push({
        tipoDocumento,
        fechaDocumento,
        numeroDocumento,
        proveedor,
        descripcion,
        categoria,
        monto,
        archivo
    });

    limpiarDocumentoForm();
    renderDocumentos();
}

async function guardarRendicion(evento) {
    evento.preventDefault();

    if (guardandoRendicion) return;

    limpiarMensaje();

    const objetivo =
        document.getElementById("objetivo").value.trim();

    const justificacion =
        document.getElementById("justificacion").value.trim();

    // Se mantiene el contrato actual de la API: fechas completas.

    const fechaInforme = document.getElementById("fechaRendicion").value;

    if (!fechaInforme)
        return mostrarError(
            "Debe indicar la fecha de la rendición."
        );

    const periodoGasto = document.getElementById("periodoGasto").value;

    if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(periodoGasto))
        return mostrarError(
            "Seleccione un mes y año válidos para el período de gasto."
        );

    const [anioPeriodo, mesPeriodo] =
        periodoGasto.split("-").map(Number);

    const ultimoDia =
        new Date(anioPeriodo, mesPeriodo, 0).getDate();

    const periodoDesde = `${periodoGasto}-01`;

    const periodoHasta =
        `${periodoGasto}-${String(ultimoDia).padStart(2, "0")}`;

    const anticipo =
        Number(document.getElementById("provisionAnticipo").value || 0);

    const supervisorValor =
        document.getElementById("supervisorUsuarioId").value;

    if (!objetivo)
        return mostrarError("Ingrese el objetivo de la rendición.");

    if (!justificacion)
        return mostrarError("Ingrese la justificación general.");

    if (!fechaInforme || !periodoDesde || !periodoHasta)
        return mostrarError("Complete las fechas del informe.");

    if (periodoDesde > periodoHasta)
        return mostrarError(
            "El período desde no puede superar al período hasta."
        );

    if (!Number.isSafeInteger(anticipo) || anticipo < 0)
        return mostrarError(
            "Ingrese una provisión válida en pesos."
        );

    if (documentos.length === 0)
        return mostrarError("Debe agregar al menos un gasto.");

    const fueraPeriodo = documentos.some(doc =>
        doc.fechaDocumento < periodoDesde ||
        doc.fechaDocumento > periodoHasta
    );

    

    const formData = new FormData();

    // Compatibilidad con el backend existente
    formData.append("TipoGasto", "MULTIPLE");
    formData.append("Justificacion", justificacion);

    // Nueva cabecera
    formData.append("Objetivo", objetivo);
    formData.append("FechaInforme", fechaInforme);
    formData.append("PeriodoDesde", periodoDesde);
    formData.append("PeriodoHasta", periodoHasta);
    formData.append("ProvisionAnticipo", anticipo.toString());

    // El supervisor se resolverá desde configuración.
    // No inventar ni enviar un identificador.
    if (supervisorValor) {
        formData.append("SupervisorUsuarioId", supervisorValor);
    }

    documentos.forEach((doc, index) => {
        const prefijo = `Documentos[${index}]`;

        formData.append(
            `${prefijo}.FechaDocumento`,
            doc.fechaDocumento
        );

        formData.append(
            `${prefijo}.TipoDocumento`,
            doc.tipoDocumento
        );

        formData.append(
            `${prefijo}.NumeroDocumento`,
            doc.numeroDocumento
        );

        formData.append(
            `${prefijo}.Proveedor`,
            doc.proveedor
        );

        formData.append(
            `${prefijo}.Monto`,
            doc.monto.toString()
        );

        formData.append(
            `${prefijo}.ArchivoDocumento`,
            doc.archivo
        );

        formData.append(
            `${prefijo}.FechaGasto`,
            doc.fechaDocumento
        );

        formData.append(
            `${prefijo}.DescripcionGasto`,
            doc.descripcion
        );

        formData.append(
            `${prefijo}.CategoriaGasto`,
            doc.categoria
        );
    });

    const boton =
        document.getElementById("btnGuardarRendicion");

    try {
        guardandoRendicion = true;
        boton.disabled = true;

        mostrarInfo("Guardando rendición y comprobantes...");

        const respuesta = await fetch("/api/rindespartan/nueva", {
            method: "POST",
            body: formData
        });

        const resultado = await respuesta.json();

        if (!respuesta.ok || !resultado.ok) {
            mostrarError(
                resultado.mensaje ||
                "No fue posible registrar la rendición."
            );
            return;
        }

        mostrarExito(
            `Rendición ${resultado.folioRinde || ""} registrada correctamente.`
        );

        documentos.forEach(doc => {
            if (doc.urlVista)
                URL.revokeObjectURL(doc.urlVista);
        });

        documentos = [];
        renderDocumentos();

        setTimeout(() => {
            location.href = "mis_rendiciones.html";
        }, 1500);

    } catch (error) {
        console.error("Error al guardar rendición:", error);
        mostrarError("Error de comunicación con la API.");

    } finally {
        guardandoRendicion = false;
        boton.disabled = false;
    }
}

function renderDocumentos() {
    const tbody = document.getElementById("tbodyDocumentos");
    tbody.replaceChildren();

    if (documentos.length === 0) {
        const fila = document.createElement("tr");
        const celda = document.createElement("td");

        celda.colSpan = 11;
        celda.className = "text-center text-muted py-3";
        celda.textContent = "No hay gastos agregados.";

        fila.appendChild(celda);
        tbody.appendChild(fila);

        actualizarTotales();
        return;
    }

    documentos.forEach((doc, index) => {
        const fila = document.createElement("tr");

        const valores = [
            formatearFechaLocal(doc.fechaDocumento),
            `${doc.tipoDocumento} ${doc.numeroDocumento || ""}`.trim(),
            doc.proveedor,
            doc.descripcion,
            doc.categoria === "ALOJAMIENTO"
                ? formatoMoneda(doc.monto) : "-",
            doc.categoria === "COMIDAS"
                ? formatoMoneda(doc.monto) : "-",
            doc.categoria === "TRANSPORTE"
                ? formatoMoneda(doc.monto) : "-",
            doc.categoria === "COMBUSTIBLE"
                ? formatoMoneda(doc.monto) : "-",
            doc.categoria === "OTROS"
                ? formatoMoneda(doc.monto) : "-"
        ];

        valores.forEach((valor, posicion) => {
            const celda = document.createElement("td");
            celda.textContent = valor;

            if (posicion >= 4)
                celda.classList.add("monto");

            fila.appendChild(celda);
        });

        const celdaAdjunto = document.createElement("td");

        const enlace = document.createElement("a");

        enlace.href = doc.urlVista ||
            (doc.urlVista = URL.createObjectURL(doc.archivo));

        enlace.target = "_blank";
        enlace.rel = "noopener noreferrer";
        enlace.textContent = "Ver";

        const nombre = document.createElement("small");
        nombre.className = "adjunto-nombre text-muted";
        nombre.textContent = doc.archivo.name;

        celdaAdjunto.append(enlace, nombre);
        fila.appendChild(celdaAdjunto);

        const celdaAccion = document.createElement("td");
        const quitar = document.createElement("button");

        quitar.type = "button";
        quitar.className = "btn btn-sm btn-outline-danger";
        quitar.textContent = "Quitar";

        quitar.addEventListener(
            "click",
            () => quitarDocumento(index)
        );

        celdaAccion.appendChild(quitar);
        fila.appendChild(celdaAccion);

        tbody.appendChild(fila);
    });

    actualizarTotales();
}

function quitarDocumento(index) {
    const [eliminado] = documentos.splice(index, 1);

    if (eliminado?.urlVista)
        URL.revokeObjectURL(eliminado.urlVista);

    renderDocumentos();
}

function actualizarTotales() {
    const totales = Object.fromEntries(
        CATEGORIAS.map(categoria => [categoria, 0])
    );

    documentos.forEach(doc => {
        totales[doc.categoria] += doc.monto;
    });

    document.getElementById("totalAlojamiento").textContent =
        formatoMoneda(totales.ALOJAMIENTO);

    document.getElementById("totalComidas").textContent =
        formatoMoneda(totales.COMIDAS);

    document.getElementById("totalTransporte").textContent =
        formatoMoneda(totales.TRANSPORTE);

    document.getElementById("totalCombustible").textContent =
        formatoMoneda(totales.COMBUSTIBLE);

    document.getElementById("totalOtros").textContent =
        formatoMoneda(totales.OTROS);

    const total = documentos.reduce(
        (acumulado, doc) => acumulado + doc.monto,
        0
    );

    const anticipo = Number(
        document.getElementById("provisionAnticipo").value || 0
    );

    document.getElementById("totalRendicion").textContent =
        formatoMoneda(total);

    document.getElementById("resumenAnticipo").textContent =
        formatoMoneda(anticipo);

    document.getElementById("resumenRendido").textContent =
        formatoMoneda(total);

    document.getElementById("resumenSaldo").textContent =
        formatoMoneda(anticipo - total);
}

function limpiarDocumentoForm() {
    document.getElementById("docTipoDocumento").value = "";
    document.getElementById("docFechaDocumento").value = "";
    document.getElementById("docNumeroDocumento").value = "";
    document.getElementById("docProveedor").value = "";
    document.getElementById("docDescripcionGasto").value = "";
    document.getElementById("docCategoriaGasto").value = "";
    document.getElementById("docMonto").value = "";
    document.getElementById("docArchivo").value = "";
    document.getElementById("docDatosConfirmados").checked = false;
    mostrarVistaPreviaComprobante();

    secuenciaLectura++;
    archivoLeido = null;
    comprobanteRevisado = false;

    estadoLectura("");
}

function validarArchivo(archivo) {
    const extension =
        archivo.name.split(".").pop().toLowerCase();

    if (!["pdf", "jpg", "jpeg", "png"].includes(extension)) {
        return {
            ok: false,
            mensaje: "Solo se permiten archivos PDF, JPG, JPEG o PNG."
        };
    }

    if (archivo.size > 5 * 1024 * 1024) {
        return {
            ok: false,
            mensaje: "El archivo supera el máximo permitido de 5 MB."
        };
    }

    if (archivo.size === 0) {
        return {
            ok: false,
            mensaje: "El archivo está vacío."
        };
    }

    return { ok: true };
}

function formatearFechaLocal(fechaISO) {
    if (!fechaISO) return "-";

    const [anio, mes, dia] = fechaISO.split("-");
    return `${dia}-${mes}-${anio}`;
}

function formatoMoneda(valor) {
    return new Intl.NumberFormat("es-CL", {
        style: "currency",
        currency: "CLP",
        maximumFractionDigits: 0
    }).format(valor || 0);
}

function mostrarMensaje(texto, tipo) {
    const elemento =
        document.getElementById("mensajeValidacion");

    elemento.className = `alert alert-${tipo} mt-4`;
    elemento.textContent = texto;
}

function mostrarError(texto) {
    mostrarMensaje(texto, "danger");
}

function mostrarExito(texto) {
    mostrarMensaje(texto, "success");
}

function mostrarInfo(texto) {
    mostrarMensaje(texto, "info");
}

function limpiarMensaje() {
    const elemento =
        document.getElementById("mensajeValidacion");

    elemento.className = "alert mt-4 d-none";
    elemento.textContent = "";
}

