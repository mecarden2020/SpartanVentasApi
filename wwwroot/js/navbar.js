document.addEventListener("DOMContentLoaded", () => {

    const role = localStorage.getItem("role"); // GERENCIA, etc
    const username = localStorage.getItem("username");

    // 🔥 IMPORTANTE: definir división
    // puedes guardar esto en login después
    let division = localStorage.getItem("division");

    // fallback manual (mientras pruebas)
    if (!division) {
        if (username === "cborquez") {
            division = "FOOD"; // Food + Institucional
        }
        else if (username === "proco") {
            division = "IND";
        }
        else {
            division = "GENERAL";
        }
    }

    console.log("Usuario:", username, "División:", division);

    // =============================
    // DEFINIR RUTAS
    // =============================
    let basePath = "/uploads/gerencia/";

    let urls = {
        ranking: "",
        gerencia: "",
        cierres: "",
        nuevos: ""
    };

    if (division === "FOOD") {
        urls.ranking = basePath + "ranking_food.html";
        urls.gerencia = basePath + "dashboard_gerencia.html";
        urls.cierres = basePath + "cierre_food.html";
        urls.nuevos = basePath + "nuevos_food.html";
    }
    else if (division === "HC") {
        urls.ranking = basePath + "ranking_hc.html";
        urls.gerencia = basePath + "dashboard_gerencia.html";
        urls.cierres = basePath + "cierre_hc.html";
        urls.nuevos = basePath + "nuevos_hc.html";
    }
    else if (division === "IND") {
        urls.ranking = basePath + "ranking_ind.html";
        urls.gerencia = basePath + "dashboard_gerencia.html";
        urls.cierres = basePath + "cierre_ind.html";
        urls.nuevos = basePath + "nuevos_ind.html";
    }
    else {
        // gerente general
        urls.ranking = "/ranking_gerencia.html";
        urls.gerencia = "/dashboard_gerencia.html";
        urls.cierres = "/cierres_gerencia.html";
        urls.nuevos = "/nuevos_gerencia.html";
    }

    // =============================
    // ASIGNAR BOTONES
    // =============================
    document.getElementById("btnRanking")?.addEventListener("click", () => {
        window.location.href = urls.ranking;
    });

    document.getElementById("btnGerencia")?.addEventListener("click", () => {
        window.location.href = urls.gerencia;
    });

    document.getElementById("btnCierres")?.addEventListener("click", () => {
        window.location.href = urls.cierres;
    });

    document.getElementById("btnClientes")?.addEventListener("click", () => {
        window.location.href = urls.nuevos;
    });

});
