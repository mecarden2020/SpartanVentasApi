namespace SpartanVentasApi.Models
{
    public class DashboardVentaMesDto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string NombreMes { get; set; } = string.Empty;
        public decimal VentaNetaSN { get; set; }
    }
}

