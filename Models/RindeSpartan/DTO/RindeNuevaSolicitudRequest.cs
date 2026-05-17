namespace SpartanVentasApi.Models.RindeSpartan.DTO
{
    public class RindeNuevaSolicitudRequest
    {
        public string TipoGasto { get; set; } = "";
        public string Justificacion { get; set; } = "";

        public List<RindeDocumentoAdjuntoRequest> Documentos { get; set; } = new();
    }

    public class RindeDocumentoAdjuntoRequest
    {
        public DateTime FechaDocumento { get; set; }

        public string TipoDocumento { get; set; } = "";

        public string? NumeroDocumento { get; set; }

        public string Proveedor { get; set; } = "";

        public decimal Monto { get; set; }

        public IFormFile? ArchivoDocumento { get; set; }
    }
}