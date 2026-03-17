// wwwroot/js/auth-header.js
(() => {
    "use strict";

    const LOGIN_PAGE = "login.html";
    const TOKEN_KEYS = ["jwtToken", "token"];

    const $ = (id) => document.getElementById(id);

    function getToken() {
        for (const k of TOKEN_KEYS) {
            const v = localStorage.getItem(k);
            if (v && String(v).trim().length > 0) return v.trim();
        }
        return null;
    }

    function routeByRole(roleRaw) {
        const role = (roleRaw || "").toString().trim().toUpperCase();

        if (role === "GERENCIA") return "gerencia_presupuesto_ventas.html";
        if (role === "ADMIN") return "gerencia_presupuesto_ventas.html"; // o admin.html si tienes
        if (role === "SUPERVISOR") return "ranking_ventas.html";            // ajusta si tienes dashboard supervisor

        return "ingreso.html"; // VENDEDOR por defecto
    }

    function getUserLabel() {
        const login = localStorage.getItem("currentLogin") || "";
        const alias = localStorage.getItem("currentAlias") || "";
        const role = localStorage.getItem("currentRole") || "";
        const who = (alias || login).trim();
        if (!who && !role) return "";
        if (who && role) return `${who} • ${role}`;
        return who || role;
    }

    function clearSession() {
        [
            "token", "jwtToken",
            "currentLogin", "currentPermisos", "currentAlias", "currentRole", "currentSlpCode"
        ].forEach(k => localStorage.removeItem(k));
    }

    function goLogin() {
        window.location.href = LOGIN_PAGE;
    }

    async function fetchAuth(url, options = {}) {
        const token = getToken();
        if (!token) throw new Error("Sesión no encontrada (token vacío).");

        const headers = new Headers(options.headers || {});
        headers.set("Authorization", `Bearer ${token}`);

        const resp = await fetch(url, { ...options, headers });

        if (resp.status === 401 || resp.status === 403) {
            clearSession();
            throw new Error("Sesión expirada o sin permisos (401/403).");
        }
        if (!resp.ok) {
            const txt = await resp.text();
            throw new Error(txt || `HTTP ${resp.status}`);
        }
        return resp;
    }

    function wireLogout() {
        const btn = $("btnLogout");
        if (!btn) return;
        btn.addEventListener("click", () => {
            clearSession();
            goLogin();
        });
    }

    function renderUser() {
        const lbl = $("lblUsuario");
        if (!lbl) return;
        lbl.textContent = getUserLabel();
    }


    async function paintNavbarUser() {
        // ... tu lógica existente (nombre, rol, etc.)

        const token = localStorage.getItem("jwtToken") || localStorage.getItem("token");
        const img = document.getElementById("navAvatar");
        if (!img) return;

        // si no hay token, default
        if (!token) {
            img.src = "/img/avatar-default.png";
            return;
        }

        // si ya está en localStorage, úsalo
        const cached = localStorage.getItem("photoUrl") || localStorage.getItem("currentPhotoUrl");
        if (cached) {
            img.src = bustCache(resolvePhotoUrl(cached));
        }

        // refrescar desde API (fuente de verdad)
        try {
            const res = await fetch(`${getApiBase()}/api/users/me/profile`, {
                headers: { "Authorization": `Bearer ${token}` }
            });
            if (!res.ok) return;

            const data = await res.json();
            if (data?.photoUrl) {
                localStorage.setItem("photoUrl", data.photoUrl);
                img.src = bustCache(resolvePhotoUrl(data.photoUrl));
            } else {
                img.src = "/img/avatar-default.png";
            }
        } catch { }
    }





    document.addEventListener("DOMContentLoaded", () => {
        renderUser();
        wireLogout();
    });

    document.getElementById("btnLogout")?.addEventListener("click", () => {
        window.SpartanAuth?.clearSession?.();
        window.location.href = "login.html";
    });


    window.SpartanAuth = {
        getToken,
        clearSession,
        goLogin: () => window.location.href = "login.html",

        // JSON helper (devuelve objeto o null)
        fetchAuth: async (url, options = {}) => {
            const token = getToken();
            if (!token) return null;

            const headers = new Headers(options.headers || {});
            headers.set("Authorization", `Bearer ${token}`);
            headers.set("Accept", "application/json");

            const resp = await fetch(url, { ...options, headers });

            if (resp.status === 401 || resp.status === 403) {
                clearSession();
                window.location.href = "login.html";
                return null;
            }

            if (!resp.ok) {
                const t = await resp.text().catch(() => "");
                throw new Error(t || `HTTP ${resp.status}`);
            }

            // ✅ importante: permitir respuestas vacías sin romper
            const txt = await resp.text();
            if (!txt) return {};
            try { return JSON.parse(txt); } catch { return {}; }
        },

        // ✅ NUEVO: helper para FormData (subir imagen) y endpoints sin JSON
        fetchAuthRaw: async (url, options = {}) => {
            const token = getToken();
            if (!token) return null;

            const headers = new Headers(options.headers || {});
            headers.set("Authorization", `Bearer ${token}`);

            const resp = await fetch(url, { ...options, headers });

            if (resp.status === 401 || resp.status === 403) {
                clearSession();
                window.location.href = "login.html";
                return null;
            }

            if (!resp.ok) {
                const t = await resp.text().catch(() => "");
                throw new Error(t || `HTTP ${resp.status}`);
            }

            return resp; // devuelve Response
        }
    };



})();
