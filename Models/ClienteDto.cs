using System.Text.Json.Serialization;

namespace SpartanVentasApi.Models
{
    /// <summary>
    /// DTO simplificado de cliente para la pantalla de cotizaciones.
    /// </summary>
    public class ClienteDto
    {
        [JsonPropertyName("cardCode")]
        public string CardCode { get; set; } = string.Empty;

        [JsonPropertyName("cardName")]
        public string CardName { get; set; } = string.Empty;

        [JsonPropertyName("billingAddress")]
        public string BillingAddress { get; set; } = string.Empty;

        [JsonPropertyName("shippingAddress")]
        public string ShippingAddress { get; set; } = string.Empty;

        [JsonPropertyName("contactName")]
        public string? ContactName { get; set; }

        [JsonPropertyName("salesPersonCode")]
        public int? SalesPersonCode { get; set; }

        [JsonPropertyName("salesPersonName")]
        public string? SalesPersonName { get; set; }
    }
}

