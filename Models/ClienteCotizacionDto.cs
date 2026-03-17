namespace SpartanVentasApi.Models
{
    public class ClienteCotizacionDto
    {
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public string BillingAddress { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string? ContactName { get; set; }
        public int? SalesPersonCode { get; set; }
        public string? SalesPersonName { get; set; } // si luego la quieres llenar desde SQL/OSLP
    }
}

