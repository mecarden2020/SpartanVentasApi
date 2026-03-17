using System.Text.Json.Serialization;

namespace SpartanVentasApi.Models
{
    public class CotizacionLineaDto
    {
        // Código del artículo en SAP (OITM.ItemCode)
        [JsonPropertyName("ItemCode")]
        public string ItemCode { get; set; } = string.Empty;

        // Descripción libre (si quieres sobreescribir la descripción del maestro)
        [JsonPropertyName("ItemDescription")]
        public string? ItemDescription { get; set; }

        // Cantidad solicitada
        [JsonPropertyName("Quantity")]
        public decimal Quantity { get; set; }

        // Precio por unidad (moneda del documento)
        [JsonPropertyName("UnitPrice")]
        public decimal UnitPrice { get; set; }

        // Bodega (almacén)
        [JsonPropertyName("WarehouseCode")]
        public string WarehouseCode { get; set; } = string.Empty;

        // Código de impuesto (Si corresponde, por ejemplo "IVA19")
        [JsonPropertyName("TaxCode")]
        public string? TaxCode { get; set; }
    }
}
