namespace SpartanVentasApi.Models
{
    public class DashboardTopClienteDto
    {
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public decimal Venta12Meses { get; set; }
        public decimal VentaUlt3Meses { get; set; }
    }
}

