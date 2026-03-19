(function () {
    const LOGIN_URL = "/Spartan_login.html";

    window.getToken = function () {
        return localStorage.getItem("jwtToken")
            || localStorage.getItem("token")
            || localStorage.getItem("accessToken")
            || "";
    };

    window.getAuthHeaders = function () {
        const token = getToken();
        return {
            "Content-Type": "application/json",
            "Authorization": `Bearer ${token}`
        };
    };

    window.requireAuth = function () {
        const token = getToken();
        if (!token) {
            window.location.href = LOGIN_URL;
            return false;
        }
        return true;
    };

    window.logout = function () {
        localStorage.removeItem("jwtToken");
        localStorage.removeItem("token");
        localStorage.removeItem("accessToken");
        localStorage.removeItem("currentUsuario");
        localStorage.removeItem("currentNombre");
        localStorage.removeItem("currentRol");
        localStorage.removeItem("currentDivision");
        localStorage.removeItem("currentSlpCode");
        localStorage.removeItem("currentPermisos");
        window.location.href = LOGIN_URL;
    };

    window.getCurrentUserInfo = function () {
        return {
            usuario: localStorage.getItem("currentUsuario") || "",
            nombre: localStorage.getItem("currentNombre") || "",
            rol: localStorage.getItem("currentRol") || "",
            division: localStorage.getItem("currentDivision") || "",
            slpCode: localStorage.getItem("currentSlpCode") || ""
        };
    };
})();