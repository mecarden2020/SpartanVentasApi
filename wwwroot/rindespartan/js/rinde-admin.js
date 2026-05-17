console.log("Admin RindeSpartan cargado");

const usuariosRinde = [
    {
        id: 1,
        usuario: "Melchor Cardenas",
        tipo: "ADMINISTRATIVO",
        puedeRendir: true,
        topeMensual: 350000,
        centroCosto: "ADM",
        aprobador: "Gerencia General",
        activo: true,
        observacion: "Usuario administrativo con rendición habilitada."
    },
    {
        id: 2,
        usuario: "Juan Pérez",
        tipo: "VENDEDOR",
        puedeRendir: true,
        topeMensual: 350000,
        centroCosto: "10",
        aprobador: "Carlos Aprobador",
        activo: true,
        observacion: ""
    },
    {
        id: 3,
        usuario: "María González",
        tipo: "VENDEDOR",
        puedeRendir: true,
        topeMensual: 500000,
        centroCosto: "40",
        aprobador: "María Supervisora",
        activo: true,
        observacion: ""
    },
    {
        id: 4,
        usuario: "Usuario Administrativo",
        tipo: "ADMINISTRATIVO",
        puedeRendir: false,
        topeMensual: 0,
        centroCosto: "ADM",
        aprobador: "Contabilidad",
        activo: false,
        observacion: "Pendiente de habilitación."
    }
];

let usuarioSeleccionado = null;

document.addEventListener("DOMContentLoaded", function () {

    cargarUsuarios(usuariosRinde);
    actualizarResumenAdmin();

    document.getElementById("txtBuscarAdmin").addEventListener("keyup", function () {
        const texto = this.value.toLowerCase();

        const filtrado = usuariosRinde.filter(x =>
            x.usuario.toLowerCase().includes(texto) ||
            x.tipo.toLowerCase().includes(texto) ||
            x.centroCosto.toLowerCase().includes(texto) ||
            x.aprobador.toLowerCase().includes(texto)
        );

        cargarUsuarios(filtrado);
    });

    document.getElementById("btnGuardarConfig").addEventListener("click", guardarConfiguracion);

});

function cargarUsuarios(datos) {
    const tbody = document.getElementById("tbodyUsuariosRinde");
    tbody.innerHTML = "";

    datos.forEach(u => {

        const badgeRinde = u.puedeRendir
            ? `<span class="badge bg-success">Sí</span>`
            : `<span class="badge bg-secondary">No</span>`;

        const badgeEstado = u.activo
            ? `<span class="badge bg-success">Activo</span>`
            : `<span class="badge bg-danger">Inactivo</span>`;

        const fila = `
            <tr>
                <td>${u.usuario}</td>
                <td>${u.tipo}</td>
                <td>${badgeRinde}</td>
                <td>${formatoMoneda(u.topeMensual)}</td>
                <td>${u.centroCosto || "-"}</td>
                <td>${u.aprobador || "-"}</td>
                <td>${badgeEstado}</td>
                <td>
                    <button class="btn btn-sm btn-outline-dark" onclick="abrirConfigUsuario(${u.id})">
                        Configurar
                    </button>
                </td>
            </tr>
        `;

        tbody.innerHTML += fila;
    });
}

function abrirConfigUsuario(id) {
    usuarioSeleccionado = usuariosRinde.find(x => x.id === id);

    if (!usuarioSeleccionado) return;

    document.getElementById("configUsuarioId").value = usuarioSeleccionado.id;
    document.getElementById("configNombreUsuario").value = usuarioSeleccionado.usuario;
    document.getElementById("configTipoUsuario").value = usuarioSeleccionado.tipo;
    document.getElementById("configPuedeRendir").value = usuarioSeleccionado.puedeRendir ? "1" : "0";
    document.getElementById("configTopeMensual").value = usuarioSeleccionado.topeMensual;
    document.getElementById("configCentroCosto").value = usuarioSeleccionado.centroCosto;
    document.getElementById("configActivo").value = usuarioSeleccionado.activo ? "1" : "0";
    document.getElementById("configObservacion").value = usuarioSeleccionado.observacion || "";

    setAprobadorSelect(usuarioSeleccionado.aprobador);

    limpiarMensajeAdmin();

    const modal = new bootstrap.Modal(document.getElementById("modalConfigUsuario"));
    modal.show();
}

function guardarConfiguracion() {
    if (!usuarioSeleccionado) return;

    const tope = Number(document.getElementById("configTopeMensual").value);
    const puedeRendir = document.getElementById("configPuedeRendir").value === "1";
    const aprobador = document.getElementById("configAprobador");
    const aprobadorTexto = aprobador.options[aprobador.selectedIndex].text;

    if (tope < 0) {
        mostrarMensajeAdmin("danger", "El tope mensual no puede ser negativo.");
        return;
    }

    if (puedeRendir && tope <= 0) {
        mostrarMensajeAdmin("danger", "Si el usuario puede rendir, debe tener un tope mensual mayor a cero.");
        return;
    }

    usuarioSeleccionado.tipo = document.getElementById("configTipoUsuario").value;
    usuarioSeleccionado.puedeRendir = puedeRendir;
    usuarioSeleccionado.topeMensual = tope;
    usuarioSeleccionado.centroCosto = document.getElementById("configCentroCosto").value;
    usuarioSeleccionado.aprobador = document.getElementById("configAprobador").value ? aprobadorTexto : "";
    usuarioSeleccionado.activo = document.getElementById("configActivo").value === "1";
    usuarioSeleccionado.observacion = document.getElementById("configObservacion").value.trim();

    cargarUsuarios(usuariosRinde);
    actualizarResumenAdmin();

    mostrarMensajeAdmin("success", "Configuración guardada visualmente. Luego conectaremos esta acción con la API.");
}

function actualizarResumenAdmin() {
    const total = usuariosRinde.length;
    const activos = usuariosRinde.filter(x => x.puedeRendir && x.activo).length;
    const topeTotal = usuariosRinde
        .filter(x => x.puedeRendir && x.activo)
        .reduce((a, b) => a + b.topeMensual, 0);

    document.getElementById("totalUsuarios").innerText = total;
    document.getElementById("totalActivos").innerText = activos;
    document.getElementById("topeTotal").innerText = formatoMoneda(topeTotal);
}

function setAprobadorSelect(nombreAprobador) {
    const select = document.getElementById("configAprobador");

    for (let i = 0; i < select.options.length; i++) {
        if (select.options[i].text === nombreAprobador) {
            select.selectedIndex = i;
            return;
        }
    }

    select.value = "";
}

function mostrarMensajeAdmin(tipo, texto) {
    const mensaje = document.getElementById("mensajeAdmin");
    mensaje.className = `alert alert-${tipo} mt-3`;
    mensaje.innerText = texto;
    mensaje.classList.remove("d-none");
}

function limpiarMensajeAdmin() {
    const mensaje = document.getElementById("mensajeAdmin");
    mensaje.className = "alert mt-3 d-none";
    mensaje.innerText = "";
}

function formatoMoneda(valor) {
    return valor.toLocaleString("es-CL", {
        style: "currency",
        currency: "CLP"
    });
}