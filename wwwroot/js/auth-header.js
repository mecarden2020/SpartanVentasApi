// wwwroot/js/auth-header.js
(() => {
    "use strict";

    const LOGIN_PAGE = "login.html";
    const TOKEN_KEYS = ["jwtToken", "token"];

    const $ = (id) => document.getElementById(id);

    function getToken() {
        for (const key of TOKEN_KEYS) {
            const value = localStorage.getItem(key);
            if (value && String(value).trim().length > 0) {
                return value.trim();
            }
        }
        return null;
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

    function getAuthHeaders(extraHeaders = {}) {
        const token = getToken();

        const headers = {
            "Content-Type": "application/json",
            "Accept": "application/json",
            ...extraHeaders
        };

        if (token) {
            headers["Authorization"] = `Bearer ${token}`;
        }

        return headers;
    }

    function clearSession() {
        [
            "token",
            "jwtToken",
            "currentLogin",
            "currentPermisos",
            "currentAlias",
            "currentRole",
            "currentSlpCode",
            "photoUrl",
            "currentPhotoUrl"
        ].forEach((key) => localStorage.removeItem(key));
    }

    function goLogin() {
        window.location.href = LOGIN_PAGE;
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

    function resolvePhotoUrl(url) {
        if (!url) return "/img/avatar-default.png";
        if (/^https?:\/\//i.test(url)) return url;
        if (url.startsWith("/")) return url;
        return `/${url.replace(/^\/+/, "")}`;
    }

    function bustCache(url) {
        if (!url) return url;
        const separator = url.includes("?") ? "&" : "?";
        return `${url}${separator}v=${Date.now()}`;
    }

    function getApiBase() {
        return window.location.origin;
    }

    async function paintNavbarUser() {
        const img = $("navAvatar");
        if (!img) return;

        const token = getToken();

        if (!token) {
            img.src = "/img/avatar-default.png";
            return;
        }

        const cached = localStorage.getItem("photoUrl") || localStorage.getItem("currentPhotoUrl");
        if (cached) {
            img.src = bustCache(resolvePhotoUrl(cached));
        } else {
            img.src = "/img/avatar-default.png";
        }

        try {
            const resp = await fetch(`${getApiBase()}/api/users/me/profile`, {
                method: "GET",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Accept": "application/json"
                }
            });

            if (!resp.ok) return;

            const data = await resp.json();
            if (data?.photoUrl) {
                localStorage.setItem("photoUrl", data.photoUrl);
                img.src = bustCache(resolvePhotoUrl(data.photoUrl));
            }
        } catch {
            // mantener avatar actual sin interrumpir la navegación
        }
    }

    async function fetchAuth(url, options = {}) {
        const token = getToken();
        if (!token) {
            clearSession();
            goLogin();
            return null;
        }

        const headers = new Headers(options.headers || {});
        headers.set("Authorization", `Bearer ${token}`);
        headers.set("Accept", "application/json");

        // No forzar Content-Type cuando se envía FormData
        if (!(options.body instanceof FormData) && !headers.has("Content-Type")) {
            headers.set("Content-Type", "application/json");
        }

        const resp = await fetch(url, { ...options, headers });

        if (resp.status === 401 || resp.status === 403) {
            clearSession();
            goLogin();
            return null;
        }

        if (!resp.ok) {
            const text = await resp.text().catch(() => "");
            throw new Error(text || `HTTP ${resp.status}`);
        }

        const text = await resp.text().catch(() => "");
        if (!text) return {};

        try {
            return JSON.parse(text);
        } catch {
            return {};
        }
    }

    async function fetchAuthRaw(url, options = {}) {
        const token = getToken();
        if (!token) {
            clearSession();
            goLogin();
            return null;
        }

        const headers = new Headers(options.headers || {});
        headers.set("Authorization", `Bearer ${token}`);

        const resp = await fetch(url, { ...options, headers });

        if (resp.status === 401 || resp.status === 403) {
            clearSession();
            goLogin();
            return null;
        }

        if (!resp.ok) {
            const text = await resp.text().catch(() => "");
            throw new Error(text || `HTTP ${resp.status}`);
        }

        return resp;
    }

    document.addEventListener("DOMContentLoaded", () => {
        renderUser();
        wireLogout();
        paintNavbarUser();
    });

    window.SpartanAuth = {
        getToken,
        getAuthHeaders,
        clearSession,
        goLogin,
        fetchAuth,
        fetchAuthRaw
    };
})();