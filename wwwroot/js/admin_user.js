
let usuariosCache = [];

document.addEventListener("DOMContentLoaded", async () => {
    if (!requireAuth()) return;

    const info = getCurrentUserInfo();
    document.getElementById("usuarioActual").textContent =
        `${info.nombre || info.usuario || "Usuario"} (${info.rol || "SIN ROL"})`;

    const txtBuscar = document.getElementById("txtBuscarUsuario");
    if (txtBuscar) {
        txtBuscar.addEventListener("input", filtrarUsuarios);
    }

    await cargarRoles();
    await cargarUsuarios();
});


function mostrarMensaje(texto, esError = false) {
    const msg = document.getElementById("msg");
    msg.textContent = texto;
    msg.style.color = esError ? "#b91c1c" : "#15803d";
}

async function manejarRespuesta(resp) {
    if (resp.status === 401 || resp.status === 403) {
        alert("Tu sesión no es válida o no tienes permisos.");
        logout();
        throw new Error("No autorizado");
    }

    const text = await resp.text();
    try {
        return text ? JSON.parse(text) : {};
    } catch {
        return text;
    }
}

async function cargarRoles() {
    try {
        const resp = await fetch("/api/admin/roles", {
            method: "GET",
            headers: getAuthHeaders()
        });

        const data = await manejarRespuesta(resp);
        const sel = document.getElementById("selRol");
        sel.innerHTML = "";

        (data || []).forEach(r => {
            const valor = r.nombre || r.Nombre || r.nombreRol || r.NombreRol || r.rol || r.Rol || "";
            const opt = document.createElement("option");
            opt.value = valor;
            opt.textContent = valor;
            sel.appendChild(opt);
        });

       /* (data || []).forEach(r => {
            const opt = document.createElement("option");
            opt.value = r.nombreRol || r.NombreRol || r.rol || r.Rol || "";
            opt.textContent = r.nombreRol || r.NombreRol || r.rol || r.Rol || "";
            sel.appendChild(opt);
        });*/

        if (!sel.options.length) {
            ["ADMIN", "GERENCIA", "SUPERVISOR", "VENDEDOR"].forEach(x => {
                const opt = document.createElement("option");
                opt.value = x;
                opt.textContent = x;
                sel.appendChild(opt);
            });
        }
    } catch (err) {
        console.error("Error cargando roles:", err);
        mostrarMensaje("No fue posible cargar roles.", true);
    }
}

async function cargarUsuarios() {
    try {
        const resp = await fetch("/api/admin/usuarios", {
            method: "GET",
            headers: getAuthHeaders()
        });

        const data = await manejarRespuesta(resp);

        if (!resp.ok) {
            mostrarMensaje(data?.mensaje || "No fue posible cargar usuarios.", true);
            return;
        }

        usuariosCache = Array.isArray(data) ? data : [];
        renderUsuarios(usuariosCache);
        mostrarMensaje("", false);
    } catch (err) {
        console.error("Error cargando usuarios:", err);
        mostrarMensaje("No fue posible cargar usuarios.", true);
    }
}



async function crearUsuario() {
    const usuario = document.getElementById("txtUsuario").value.trim();
    const nombre = document.getElementById("txtNombre").value.trim();
    const clave = document.getElementById("txtClave").value.trim();
    const rol = document.getElementById("selRol").value;
    const slpCodeTxt = document.getElementById("txtSlpCode").value.trim();

    if (!usuario || !nombre || !clave || !rol) {
        mostrarMensaje("Completa login, nombre, clave y rol.", true);
        return;
    }

    const body = {
        usuario,
        nombre,
        clave,
        rol,
        slpCode: slpCodeTxt ? parseInt(slpCodeTxt) : null
    };

    try {
        const resp = await fetch("/api/admin/usuarios", {
            method: "POST",
            headers: getAuthHeaders(),
            body: JSON.stringify(body)
        });

        const data = await manejarRespuesta(resp);

        if (!resp.ok) {
            mostrarMensaje(data?.mensaje || "No fue posible crear el usuario.", true);
            return;
        }

        mostrarMensaje(data?.mensaje || "Usuario creado correctamente.");
        document.getElementById("txtUsuario").value = "";
        document.getElementById("txtNombre").value = "";
        document.getElementById("txtClave").value = "";
        document.getElementById("txtSlpCode").value = "";

        await cargarUsuarios();
    } catch (err) {
        console.error("Error creando usuario:", err);
        mostrarMensaje("Error al crear usuario.", true);
    }
}

async function cambiarEstado(id, activo) {
    try {
        const resp = await fetch(`/api/admin/usuarios/${id}/estado`, {
            method: "PATCH",
            headers: getAuthHeaders(),
            body: JSON.stringify({ activo })
        });

        const data = await manejarRespuesta(resp);

        if (!resp.ok) {
            mostrarMensaje(data?.mensaje || "No fue posible cambiar estado.", true);
            return;
        }

        mostrarMensaje(data?.mensaje || "Estado actualizado.");
        await cargarUsuarios();
    } catch (err) {
        console.error("Error cambiando estado:", err);
        mostrarMensaje("Error al cambiar estado.", true);
    }
}

async function resetClave(id) {
    const nuevaClave = prompt("Ingrese la nueva clave:");
    if (!nuevaClave) return;

    try {
        const resp = await fetch(`/api/admin/usuarios/${id}/reset-clave`, {
            method: "POST",
            headers: getAuthHeaders(),
            body: JSON.stringify({ nuevaClave })
        });

        const data = await manejarRespuesta(resp);

        if (!resp.ok) {
            mostrarMensaje(data?.mensaje || "No fue posible resetear clave.", true);
            return;
        }

        mostrarMensaje(data?.mensaje || "Clave actualizada.");
    } catch (err) {
        console.error("Error reset clave:", err);
        mostrarMensaje("Error al resetear clave.", true);
    }
}

async function generarLink(id) {
    try {
        const respLink = await fetch(`/api/admin/usuarios/${id}/link`, {
            method: "GET",
            headers: getAuthHeaders()
        });

        if (respLink.ok) {
            const dataLink = await respLink.json();

            const confirmar = confirm(
                "Este usuario ya tiene un link activo.\n\n" +
                "Si generas uno nuevo, el link anterior dejará de funcionar.\n\n" +
                "¿Deseas continuar?"
            );

            if (!confirmar) return;
        }

        const resp = await fetch(`/api/admin/usuarios/${id}/generar-link`, {
            method: "POST",
            headers: getAuthHeaders()
        });

        const data = await manejarRespuesta(resp);

        if (!resp.ok) {
            mostrarMensaje(data?.mensaje || "No fue posible generar el link.", true);
            return;
        }

        const nuevoLink = data?.link || "";
        mostrarMensaje(data?.mensaje || "Link generado correctamente.");

        if (nuevoLink) {
            prompt("Nuevo link generado:", nuevoLink);
        }

        await cargarUsuarios();
    } catch (err) {
        console.error("Error generando link:", err);
        mostrarMensaje("Error al generar link.", true);
    }
}

async function verLink(id) {
    try {
        const resp = await fetch(`/api/admin/usuarios/${id}/link`, {
            method: "GET",
            headers: getAuthHeaders()
        });

        const data = await manejarRespuesta(resp);

        if (resp.status === 404) {
            alert("El usuario no tiene link generado actualmente.");
            mostrarMensaje("", false);
            return;
        }

        if (!resp.ok) {
            mostrarMensaje(data?.mensaje || "No fue posible obtener el link.", true);
            return;
        }

        const link = data?.link || data?.Link || "";
        if (!link) {
            alert("El usuario no tiene link generado actualmente.");
            mostrarMensaje("", false);
            return;
        }

        mostrarMensaje("", false);
        prompt("Link actual del usuario:", link);
    } catch (err) {
        console.error("Error obteniendo link:", err);
        mostrarMensaje("Error al obtener link.", true);
    }
}

function filtrarUsuarios() {
    const txtBuscar = document.getElementById("txtBuscarUsuario");
    const filtro = (txtBuscar?.value || "").trim().toLowerCase();

    if (!filtro) {
        renderUsuarios(usuariosCache);
        return;
    }

    const filtrados = usuariosCache.filter(u => {
        const id = String(u.id ?? u.Id ?? "").toLowerCase();
        const usuario = String(u.usuario ?? u.Usuario ?? u.login ?? u.Login ?? "").toLowerCase();
        const nombre = String(u.nombre ?? u.Nombre ?? "").toLowerCase();
        const rol = String(u.rol ?? u.Rol ?? "").toLowerCase();
        const slpCode = String(u.slpCode ?? u.SlpCode ?? "").toLowerCase();

        return id.includes(filtro)
            || usuario.includes(filtro)
            || nombre.includes(filtro)
            || rol.includes(filtro)
            || slpCode.includes(filtro);
    });

    renderUsuarios(filtrados);
}

function renderUsuarios(data) {
    const tbody = document.getElementById("tblUsuarios");
    tbody.innerHTML = "";

    (data || []).forEach(u => {
        const tr = document.createElement("tr");

        const id = u.id ?? u.Id ?? "";
        const usuario = u.usuario ?? u.Usuario ?? u.login ?? u.Login ?? "";
        const nombre = u.nombre ?? u.Nombre ?? "";
        const rol = u.rol ?? u.Rol ?? "";
        const slpCode = u.slpCode ?? u.SlpCode ?? "";
        const activo = u.activo ?? u.Activo ?? false;
        const link = u.linkAcceso ?? u.LinkAcceso ?? "";

        tr.innerHTML = `
            <td>${id}</td>
            <td>${usuario}</td>
            <td>${nombre}</td>
            <td>${rol}</td>
            <td>${slpCode ?? ""}</td>
            <td class="${activo ? 'estado-activo' : 'estado-inactivo'}">${activo ? "Activo" : "Inactivo"}</td>
            <td>${link ? '<span class="estado-activo">Link activo</span>' : '<span class="muted">Sin link</span>'}</td>
            <td>
                <div class="acciones">
                    <button onclick="cambiarEstado(${id}, ${activo ? "false" : "true"})" class="${activo ? "btn-danger" : "btn-green"}">
                        ${activo ? "Desactivar" : "Activar"}
                    </button>
                    <button onclick="resetClave(${id})" class="btn-sec">Reset clave</button>
                    <button onclick="generarLink(${id})">Generar link</button>
                    <button onclick="verLink(${id})" class="btn-sec">Ver link</button>
                </div>
            </td>
        `;
        tbody.appendChild(tr);
    });
}