document.addEventListener("click", (e) => {

    const btn = e.target.closest(".sp-nav-btn");
    if (!btn) return;

    const url = btn.dataset.url;
    if (url) window.location.href = url;

});

document.addEventListener("click", (e) => {

    if (e.target.closest("#btnLogout")) {
        if (window.SpartanAuth?.logout) SpartanAuth.logout();
        else location.href = "/login.html";
    }

});
