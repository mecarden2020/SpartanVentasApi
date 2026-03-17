async function loadHeaderByRole(role) {

    let file = "";

    switch (role) {
        case "VENDEDOR":
            file = "/shared/header-vendedor.html";
            break;

        case "SUPERVISOR":
            file = "/shared/header-supervisor.html";
            break;

        case "GERENCIA":
            file = "/shared/header-gerencia.html";
            break;

        default:
            file = "/shared/header-vendedor.html";
    }

    const html = await fetch(file).then(r => r.text());
    document.getElementById("layoutHeader").innerHTML = html;
}
