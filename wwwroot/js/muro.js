// wwwroot/js/muro.js
(() => {
    "use strict";

    const API = window.API_BASE || ""; // si no existe, queda relativo
    const $ = (id) => document.getElementById(id);

    const feed = $("feed");
    const msg = $("msg");

    const btnPublicar = $("btnPublicar");
    const btnPrev = $("btnPrev");
    const btnNext = $("btnNext");
    const pageInfo = $("pageInfo");

    let page = 1;
    const pageSize = 10;

    function setMsg(t) { if (msg) msg.textContent = t || ""; }

    function esc(s) {
        return (s ?? "").toString()
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function render(items = []) {
        if (!feed) return;

        feed.innerHTML = items.map(p => {
            const who = esc(p.createdByAlias || p.createdByLogin || "");
            const pinned = p.isPinned ? `<span class="badge text-bg-warning ms-2">Fijado</span>` : "";
            const img = p.imageUrl ? `
        <div class="mt-2">
          <img src="${esc(p.imageUrl)}" class="img-fluid rounded border" style="max-height:360px; object-fit:contain;">
        </div>` : "";

            return `
<div class="card mb-3" data-post-id="${p.id}">
  <div class="card-body">
    <div class="d-flex justify-content-between align-items-start">
      <div>
        <span class="badge text-bg-secondary">${esc(p.category)}</span>
        ${pinned}
        <div class="small text-muted mt-1">${who} • ${new Date(p.createdAt).toLocaleString()}</div>
      </div>
    </div>

    <div class="mt-2" style="white-space:pre-wrap;">${esc(p.text)}</div>
    ${img}

    <div class="d-flex gap-2 align-items-center mt-3">
      <button type="button" class="btn btn-sm btn-outline-primary" data-action="like" data-id="${p.id}">
        👍 <span>${p.likeCount ?? 0}</span>
      </button>

      <button type="button" class="btn btn-sm btn-outline-dark" data-action="pin" data-id="${p.id}">
        📌
      </button>

      <button type="button" class="btn btn-sm btn-danger ms-auto" data-action="del" data-id="${p.id}">
        🗑 Eliminar
      </button>
    </div>
  </div>
</div>`;
        }).join("");
    }

    async function cargarFeed() {
        setMsg("Cargando...");

        const data = await window.SpartanAuth.fetchAuth(`${API}/api/wall/posts?page=${page}&pageSize=${pageSize}`);
        if (!data) return; // sesión expirada

        render(data.items || []);

        const total = data.total ?? 0;
        const totalPages = Math.max(1, Math.ceil(total / pageSize));
        if (pageInfo) pageInfo.textContent = `Página ${data.page} de ${totalPages} • Total: ${total}`;

        if (btnPrev) btnPrev.disabled = page <= 1;
        if (btnNext) btnNext.disabled = page >= totalPages;

        setMsg("");
    }

    async function publicar() {
        const category = ($("cat")?.value || "").trim();
        const text = ($("txt")?.value || "").trim();
        const file = $("postImage")?.files?.[0] || null;

        if (!category) return alert("Selecciona categoría.");
        if (!text || text.length < 2) return alert("Texto demasiado corto.");

        setMsg("Publicando...");

        // 1) crear post (JSON)
        const created = await window.SpartanAuth.fetchAuth(`${API}/api/wall/posts`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ category, text })
        });
        if (!created) return;

        const id = created.id;
        if (!id) throw new Error("No se recibió id del post.");

        // 2) subir imagen (multipart) si hay
        if (file) {
            const fd = new FormData();
            fd.append("file", file);

            await window.SpartanAuth.fetchAuthRaw(`${API}/api/wall/posts/${id}/image`, {
                method: "POST",
                body: fd
            });
        }

        $("txt").value = "";
        if ($("postImage")) $("postImage").value = "";

        page = 1;
        await cargarFeed();
        setMsg("Publicado ✅");
        setTimeout(() => setMsg(""), 1000);
    }

    async function like(id) {
        await window.SpartanAuth.fetchAuth(`${API}/api/wall/posts/${id}/like`, { method: "POST" });
        await cargarFeed();
    }

    async function pin(id) {
        await window.SpartanAuth.fetchAuth(`${API}/api/wall/posts/${id}/pin`, { method: "POST" });
        await cargarFeed();
    }

    async function del(id) {
        if (!confirm("¿Eliminar este post?")) return;
        await window.SpartanAuth.fetchAuth(`${API}/api/wall/posts/${id}`, { method: "DELETE" });

        await cargarFeed();
    }

    function wire() {
        btnPublicar?.addEventListener("click", async () => {
            try { await publicar(); }
            catch (e) { console.error(e); alert(e.message || "Error al publicar"); setMsg(""); }
        });

        btnPrev?.addEventListener("click", async () => { page = Math.max(1, page - 1); await cargarFeed(); });
        btnNext?.addEventListener("click", async () => { page = page + 1; await cargarFeed(); });

        // Delegación para botones del feed
        feed?.addEventListener("click", async (ev) => {
            const btn = ev.target.closest("button[data-action]");
            if (!btn) return;

            const action = btn.getAttribute("data-action");
            const id = parseInt(btn.getAttribute("data-id"), 10);
            if (!id) return;

            try {
                setMsg("Procesando...");
                if (action === "like") await like(id);
                else if (action === "pin") await pin(id);
                else if (action === "del") await del(id);
                setMsg("");
            } catch (e) {
                console.error(e);
                alert(e.message || "Error");
                setMsg("");
            }
        });
    }

    document.addEventListener("DOMContentLoaded", async () => {
        wire();
        await cargarFeed();
    });

    // debug opcional
    window.cargarFeed = cargarFeed;
})();
