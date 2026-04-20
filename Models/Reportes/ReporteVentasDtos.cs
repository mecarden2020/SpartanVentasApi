namespace SpartanVentasApi.Models.Reportes
{
    public class ReporteVentasKpiDto
    {
        public decimal VentaTotal { get; set; }
        public decimal MetaTotal { get; set; }
        public decimal CumplimientoPct { get; set; }
        public decimal Brecha { get; set; }
    }

    public class ReporteVentasEquipoDto
    {
        public string Equipo { get; set; } = string.Empty;
        public decimal Venta { get; set; }
        public decimal Meta { get; set; }
        public decimal CumplimientoPct { get; set; }
        public decimal Brecha { get; set; }
    }

    public class ReporteVentasVendedorDto
    {
        public string Equipo { get; set; } = string.Empty;
        public int SlpCode { get; set; }
        public string EmpleadoVentas { get; set; } = string.Empty;
        public decimal Venta { get; set; }
        public decimal Meta { get; set; }
        public decimal CumplimientoPct { get; set; }
        public decimal Brecha { get; set; }
    }

    public class ReporteVentasProductoDto
    {
        public string Equipo { get; set; } = string.Empty;
        public string EmpleadoVentas { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal Venta { get; set; }
    }

    public class ReporteVentasResponseDto
    {
        public ReporteVentasKpiDto Kpis { get; set; } = new();
        public List<ReporteVentasEquipoDto> Equipos { get; set; } = new();
        public List<ReporteVentasVendedorDto> Vendedores { get; set; } = new();
        public List<ReporteVentasProductoDto> Productos { get; set; } = new();
        public List<ReporteVentasClienteTopDto> ClientesTop { get; set; } = new();

    }

    public class ReporteVentasClienteTopDto
    {
        public string Equipo { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public string EmpleadoVentas { get; set; } = string.Empty;
        public decimal Venta { get; set; }
        public decimal AportePct { get; set; }
    }










}