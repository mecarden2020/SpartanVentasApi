namespace SpartanVentasApi.Models
{
    public class VentaPeriodoDto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string NombreMes { get; set; } = string.Empty;
        public int CodVendedor { get; set; }
        public string NombreVendedor { get; set; } = string.Empty;
        public string CodigoCliente { get; set; } = string.Empty;
        public string NombreCliente { get; set; } = string.Empty;
        public decimal VentaNetaSN { get; set; }
        public int CantFacturas { get; set; }
        public int CantNC { get; set; }
    }
}
