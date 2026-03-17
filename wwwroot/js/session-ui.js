// js/session-ui.js

const TOKEN_KEYS = ["jwtToken", "token"];

// ------------------ TOKEN ------------------
function getToken() {
    for (const k of TOKEN_KEYS) {
        const v = localStorage.getItem(k);
        if (v && v.trim().length > 0) return v.trim();
    }
    return null;
}

function goHomeDashboard(force = false) {
    window.location.href = "ingreso.html";
}

function initSapNav(pageName) {

    const bc = document.getElementById("sapBreadcrumbPage");
    if (bc) bc.textContent = pageName;

    const btn = document.getElementById("btnGoDashboard");

    if (btn) {
        if (!location.pathname.toLowerCase().includes("ingreso.html")) {
            btn.classList.remove("d-none");
        } else {
            btn.classList.add("d-none");
        }
    }
}



// ------------------ USER LABEL ------------------
function getUserLabel() {
    const login = (localStorage.getItem("currentLogin") || "").trim();
    const alias = (localStorage.getItem("currentAlias") || "").trim();
    const role = (localStorage.getItem("currentRole") || "").trim();

    const who = alias || login;
    if (!who && !role) return "";
    if (who && role) return `${who} · ${role}`;
    return who || role;
}

// ------------------ PHOTO CACHE (POR USUARIO) ------------------
function getCurrentLogin() {
    return (localStorage.getItem("currentLogin") || localStorage.getItem("login") || "").trim().toLowerCase();
}

function photoKey(login) {
    return `photoUrl:${(login || "").trim().toLowerCase()}`;
}

function setCachedPhoto(login, url) {
    const k = photoKey(login);
    if (!k) return;
    localStorage.setItem(k, (url || "").trim());
}

function getCachedPhoto(login) {
    const k = photoKey(login);
    if (!k) return "";
    return (localStorage.getItem(k) || "").trim();
}

function resolvePhotoUrl(url) {
    if (!url) return "";
    if (url.startsWith("http")) return url;
    return url.startsWith("/") ? url : `/${url}`;
}

function bustCache(url) {
    if (!url) return url;
    const u = new URL(url, window.location.origin);
    u.searchParams.set("v", Date.now().toString());
    return u.toString();
}

// ------------------ NAVBAR PAINT ------------------
function paintNavbarUser() {
    const img = document.getElementById("navAvatar");
    const spn = document.getElementById("navUserName");

    if (spn) spn.textContent = getUserLabel();

    const login = getCurrentLogin();
    const cached = getCachedPhoto(login);

    const finalUrl = cached
        ? bustCache(resolvePhotoUrl(cached))
        : "/img/avatar-default.png";

    if (img) img.src = finalUrl;
}

// ------------------ SYNC PHOTO FOR CURRENT USER ------------------
// 1) setea currentPhotoUrl desde cache por login (evita foto pegada)
// 2) intenta refrescar desde API y actualiza cache
async function syncPhotoForCurrentUser() {
    const login = getCurrentLogin();
    if (!login) return;

    // 1) rápido desde cache por usuario
    const cached = getCachedPhoto(login);
    if (cached) {
        localStorage.setItem("currentPhotoUrl", cached);
    } else {
        localStorage.removeItem("currentPhotoUrl");
    }

    // 2) refresco desde API
    const token = getToken();
    if (!token) return;

    try {
        const res = await fetch("/api/users/me/profile", {
            headers: { "Authorization": `Bearer ${token}` }
        });
        if (!res.ok) return;

        const data = await res.json().catch(() => ({}));
        const url = (data.photoUrl || "").trim();

        if (url) {
            setCachedPhoto(login, url);
            localStorage.setItem("currentPhotoUrl", url);
        } else {
            // si no trae foto, no forzamos borrar cache; opcional:
            // setCachedPhoto(login, "");
            // localStorage.removeItem("currentPhotoUrl");
        }
    } catch {
        // silencioso
    }
}

// ------------------ LOGOUT ------------------
function clearSession() {
    [
        "token",
        "jwtToken",
        "currentLogin",
        "currentAlias",
        "currentRole",
        "currentSlpCode",
        "currentPhotoUrl"
    ].forEach(k => localStorage.removeItem(k));
}
