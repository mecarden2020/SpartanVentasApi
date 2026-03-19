document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("loginForm");
    const txtUsuario = document.getElementById("txtUsuario");
    const txtClave = document.getElementById("txtClave");
    const msgError = document.getElementById("msgError");
    const msgOk = document.getElementById("msgOk");
    const btnIngresar = document.getElementById("btnIngresar");

    function mostrarError(msg) {
        msgError.textContent = msg;
        msgError.classList.remove("hidden");
        msgOk.classList.add("hidden");
    }

    function mostrarOk(msg) {
        msgOk.textContent = msg;
        msgOk.classList.remove("hidden");
        msgError.classList.add("hidden");
    }

    function limpiarMensajes() {
        msgError.textContent = "";
        msgOk.textContent = "";
        msgError.classList.add("hidden");
        msgOk.classList.add("hidden");
    }

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        limpiarMensajes();

        const usuario = txtUsuario.value.trim();
        const clave = txtClave.value.trim();

        if (!usuario || !clave) {
            mostrarError("Debe ingresar usuario y clave.");
            return;
        }

        btnIngresar.disabled = true;

        try {
            const resp = await fetch("/api/auth/login", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    Username: usuario,
                    Password: clave
                })
            });

            const text = await resp.text();
            let data = {};

            try {
                data = text ? JSON.parse(text) : {};
            } catch {
                data = {};
            }

            if (!resp.ok) {
                mostrarError(
                    typeof data === "string"
                        ? data
                        : data?.mensaje || data?.message || "Credenciales inválidas."
                );
                return;
            }

            const token = data.token || "";

            if (!token) {
                mostrarError("La autenticación fue exitosa, pero no se recibió token.");
                return;
            }

            localStorage.setItem("jwtToken", token);
            localStorage.setItem("currentUsuario", data.username || "");
            localStorage.setItem("currentNombre", data.login || "");
            localStorage.setItem("currentRol", data.role || "");
            localStorage.setItem("currentSlpCode", data.slpCode != null ? data.slpCode : "");
            localStorage.setItem("currentPermisos", JSON.stringify(data.permisos || []));

            mostrarOk("Acceso correcto. Redirigiendo...");

            setTimeout(() => {
                window.location.href = "/admin_user.html";
            }, 400);

        } catch (err) {
            console.error("Error login:", err);
            mostrarError("No fue posible conectar con el servidor.");
        } finally {
            btnIngresar.disabled = false;
        }
    });
});