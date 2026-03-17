// wwwroot/js/feedback.js
(() => {
    "use strict";

    const API = window.API_BASE || ""; // si no existe, queda relativo
    const $ = (id) => document.getElementById(id);

    // IDs esperados en feedback.html:
    // tipo (select), modulo (select), titulo (input), mensaje (textarea)
    // btnPublicar (button), btnActualizar (button opcional), msg (div opcional), feed (div opcional)

    const elTipo = $("tipo");
    const elModulo = $("modulo");
    const elTitulo = $("titulo");
    const elMensaje = $("mensaje");

    const btnPublicar = $("btnPublicar");
    const btnActualizar = $("btnActualizar");
    const elMsg = $("msg");
    const elFeed = $("feed");

    function setMsg(t) {
        if (elMsg) elMsg.textContent = t || "";
    }

    function esc(s) {
        return (s ?? "").toString()
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function resolvePhoto(url) {
        if (!url) return "/img/avatar-default.png";
        // si viene relativo desde API
        if (url.startsWith("/")) return url;
        return url;
    }

    function render(items = []) {
        if (!elFeed) return;

        elFeed.innerHTML = items.map(p => {
            const who = esc(p.login || `SlpCode ${p.slpCode ?? ""}`);
            const tipo = esc(p.tipo || "");
            const modulo = esc(p.modulo || "");
            const titulo = esc(p.titulo || "");
            const mensaje = esc(p.mensaje || "");
            const foto = resolvePhoto(p.photoUrl);

            return `
<div class="card mb-3">
  <div class="card-body">
    <div class="d-flex align-items-start justify-content-between">
      <div class="d-flex gap-2 align-items-center">
        <img src="${esc(foto)}" style="width:34px;height:34px;border-radius:50%;object-fit:cover;" />
        <div>
          <div class="small text-muted">${who} • ${new Date(p.createdAt).toLocaleString()}</div>
          <div class="mt-1">
            <span class="badge text-bg-primary">${tipo}</span>
            <span class="badge text-bg-secondary ms-1">${modulo}</span>
          </div>
        </div>
      </div>
    </div>

    <h6 class="mt-2 mb-1">${titulo}</h6>
    <div style="white-space:pre-wrap;">${mensaje}</div>
  </div>
</div>`;
        }).join("");
    }

    async function cargar() {
        try {
            setMsg("Cargando...");
            const data = await window.SpartanAuth.fetchAuth(`${API}/api/feedback/posts?skip=0&take=30`, { method: "GET" });
            if (!data) return; // sesión expirada/redirigida
            // GetPosts retorna array directamente
            render(Array.isArray(data) ? data : (data.items || []));
            setMsg("");
        } catch (e) {
            console.error(e);
            setMsg("");
            alert(e.message || "Error al cargar");
        }
    }

    async function publicar() {
        try {
            const tipo = (elTipo?.value || "MEJORA").trim();
            const modulo = (elModulo?.value || "General").trim();
            const titulo = (elTitulo?.value || "").trim();
            const mensaje = (elMensaje?.value || "").trim();

            if (titulo.length < 3) return alert("Título inválido (mínimo 3 caracteres).");
            if (mensaje.length < 3) return alert("Mensaje inválido (mínimo 3 caracteres).");

            setMsg("Publicando...");

            const payload = { tipo, modulo, titulo, mensaje };

            const created = await window.SpartanAuth.fetchAuth(`${API}/api/feedback/posts`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            if (!created) return;

            // limpiar
            if (elTitulo) elTitulo.value = "";
            if (elMensaje) elMensaje.value = "";

            setMsg("Enviado ✅");
            await cargar();
            setTimeout(() => setMsg(""), 1200);
        } catch (e) {
            console.error(e);
            // Si tu SpartanAuth.fetchAuth lanza HTTP 401/403, te redirige.
            // Aquí mostramos lo demás:
            setMsg("");
            alert(e.message || "Error al publicar");
        }
    }

    function wire() {
        btnPublicar?.addEventListener("click", publicar);
        btnActualizar?.addEventListener("click", cargar);
    }

    document.addEventListener("DOMContentLoaded", async () => {
        if (!window.SpartanAuth?.fetchAuth) {
            console.error("SpartanAuth.fetchAuth no está disponible. Revisa el orden de scripts.");
            return;
        }
        wire();
        await cargar();
    });

    // debug opcional
    window.feedbackReload = cargar;
})();
