namespace SpartanVentasApi.Models.Reportes
{
    public class VentasSemanalesDto
    {
        public int Year { get; set; }
        public int MesNum { get; set; }
        public string Mes { get; set; } = string.Empty;
        public string ZonaChile { get; set; } = string.Empty;
        public string Gerencia { get; set; } = string.Empty;
        public string Supervisor { get; set; } = string.Empty;
        public string Div { get; set; } = string.Empty;
        public int SlpCode { get; set; }
        public string Vendedor { get; set; } = string.Empty;

        public decimal Facturas { get; set; }
        public decimal Pedidos { get; set; }
        public decimal Entregas { get; set; }
        public decimal Total { get; set; }

        public decimal MetaSemanal { get; set; }
        public decimal CumplimientoPct { get; set; }
    }
}
