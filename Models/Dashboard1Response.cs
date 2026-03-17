using System.Collections.Generic;

namespace SpartanVentasApi.Models
{
    public class Dashboard1Response
    {
        // Serie de barras 12 meses
        public List<DashboardVentaMesDto> Ventas12Meses { get; set; } = new();

        // Todos los clientes con movimiento en 12 meses (para tabla opcional)
        public List<DashboardTopClienteDto> TopClientes { get; set; } = new();

        // Top 5 con compras en los últimos 3 meses
        public List<DashboardTopClienteDto> TopActivos3Meses { get; set; } = new();

        // Top 5 que NO compraron en los últimos 3 meses (pero sí en 12m)
        public List<DashboardTopClienteDto> TopSinCompras3Meses { get; set; } = new();

        // Resumen rápido
        public decimal Total12Meses { get; set; }
        public int ClientesConMovimiento12Meses { get; set; }
    }
}

