using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SpartanVentasApi.Models
{
    /// <summary>
    /// DTO que se envía al Service Layer para crear una cotización (OQUT + QUT1).
    /// Los nombres de las propiedades están alineados con el JSON que espera SAP.
    /// </summary>
    public class CotizacionCrearDto
    {
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public DateTime DocDueDate { get; set; }

        // Nuevos campos
        public string BillingAddress { get; set; } = string.Empty;   // Dirección facturación
        public string ShippingAddress { get; set; } = string.Empty;  // Dirección despacho
        public string ContactName { get; set; } = string.Empty;      // Nombre contacto (opcional)
        public int SalesPersonCode { get; set; }                     // Vendedor (SlpCode)
        public string Comments { get; set; } = string.Empty;
        public List<CotizacionLineaDto> DocumentLines { get; set; } = new();
    }


}
