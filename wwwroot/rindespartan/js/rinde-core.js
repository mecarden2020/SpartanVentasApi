console.log("RindeSpartan iniciado");

// =====================================================
// DATOS USUARIO TEMPORAL
// =====================================================

const nombreUsuario = document.getElementById("nombreUsuario");

if (nombreUsuario) {
    nombreUsuario.innerText = "Melchor Cardenas";
}

// =====================================================
// DASHBOARD TEMPORAL
// =====================================================

const saldoDisponible = document.getElementById("saldoDisponible");
const gastoUtilizado = document.getElementById("gastoUtilizado");
const pendientes = document.getElementById("pendientes");

if (saldoDisponible) {
    saldoDisponible.innerText = "$ 350.000";
}

if (gastoUtilizado) {
    gastoUtilizado.innerText = "$ 150.000";
}

if (pendientes) {
    pendientes.innerText = "3";
}

// =====================================================
// UTILIDADES GENERALES
// =====================================================

function formatoMoneda(valor) {

    return Number(valor).toLocaleString("es-CL", {
        style: "currency",
        currency: "CLP"
    });
}

function formatearFecha(fecha) {

    if (!fecha)
        return "-";

    const d = new Date(fecha);

    if (isNaN(d.getTime()))
        return fecha;

    return d.toLocaleDateString("es-CL");
}