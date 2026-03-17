// wwwroot/js/gerencia-nav.js
(function () {
    function getToken() {
        return localStorage.getItem("token") || sessionStorage.getItem("token") || "";
    }

    function getAllowedCCFromStorage() {
        // Opción A: guardas allowedCC en login (recomendado)
        // Ej: localStorage.setItem("allowedCC", "40,50")
        const raw = localStorage.getItem("allowedCC") || "";
        return raw
            .split(",")
            .map(x => parseInt(x.trim(), 10))
            .filter(n => Number.isFinite(n));
    }

    function pickGerenciaBase(allowedCC) {
        // prioridad simple: IND(10) > HC(30) > FOOD/IN(40/50)
        if (allowedCC.includes(10)) return "/uploads/gerencia";
        if (allowedCC.includes(30)) return "/uploads/gerencia";
        if (allowedCC.includes(40) || allowedCC.includes(50)) return "/uploads/gerencia";
        return "/uploads/gerencia";
    }

    function pickGerenciaPage(allowedCC, kind) {
        // kind: "ranking" | "cierre" | "nuevos"
        // decisión por división
        const hasIND = allowedCC.includes(10);
        const hasHC = allowedCC.includes(30);
        const hasFOOD = allowedCC.includes(40) || allowedCC.includes(50);

        // Si tiene varias, puedes decidir una por prioridad o mostrar selector después.
        let suffix = "food";
        if (hasIND) suffix = "ind";
        else if (hasHC) suffix = "hc";
        else if (hasFOOD) suffix = "food";

        const base = pickGerenciaBase(allowedCC);

        if (kind === "ranking") return `${base}/ranking_${suffix}.html`;
        if (kind === "cierre") return `${base}/cierre_${suffix}.html`;
        if (kind === "nuevos") return `${base}/nuevos_${suffix}.html`;

        return `${base}/ranking_${suffix}.html`;
    }

    function applyAutoUrls() {
        const allowedCC = getAllowedCCFromStorage();

        // Si no hay allowedCC, no tocamos nada (evita romper)
        if (!allowedCC.length) return;

        document.querySelectorAll("[data-url='AUTO_RANKING']").forEach(btn => {
            btn.setAttribute("data-url", pickGerenciaPage(allowedCC, "ranking"));
        });

        document.querySelectorAll("[data-url='AUTO_CIERRES']").forEach(btn => {
            btn.setAttribute("data-url", pickGerenciaPage(allowedCC, "cierre"));
        });

        document.querySelectorAll("[data-url='AUTO_CLIENTES']").forEach(btn => {
            btn.setAttribute("data-url", pickGerenciaPage(allowedCC, "nuevos"));
        });

        // Si tu botón Gerencia apunta a un dashboard por división (si existe)
        document.querySelectorAll("[data-url='AUTO_GERENCIA']").forEach(btn => {
            // si no tienes dashboard por división, déjalo fijo:
            btn.setAttribute("data-url", "/dashboard_gerencia.html");
        });
    }

    // Si ya tienes un handler global para .nav-dashboard-btn, esto basta.
    document.addEventListener("DOMContentLoaded", applyAutoUrls);
})();
