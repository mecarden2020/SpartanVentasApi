
namespace SpartanVentasApi.Models.RindeSpartan.DTO
{
    public class RindeNuevaSolicitudRequest
    {
        // ==========================================
        // COMPATIBILIDAD CON RINDESPARTAN ACTUAL
        // ==========================================

        public string TipoGasto { get; set; } = "";
        public string Justificacion { get; set; } = "";

        // ==========================================
        // CABECERA - FORMATO TRADICIONAL
        // ==========================================

        public string? Objetivo { get; set; }

        public DateTime? FechaInforme { get; set; }

        public DateTime? PeriodoDesde { get; set; }

        public DateTime? PeriodoHasta { get; set; }

        public int? SupervisorUsuarioId { get; set; }

        public decimal? ProvisionAnticipo { get; set; }

        // ==========================================
        // DETALLE DE DOCUMENTOS
        // ==========================================

        public List<RindeDocumentoAdjuntoRequest> Documentos
        { get; set; } = new();
    }

    public class RindeDocumentoAdjuntoRequest
    {
        // Campos existentes
        public DateTime FechaDocumento { get; set; }

        public string TipoDocumento { get; set; } = "";

        public string? NumeroDocumento { get; set; }

        public string Proveedor { get; set; } = "";

        public decimal Monto { get; set; }

        public IFormFile? ArchivoDocumento { get; set; }

        // ==========================================
        // NUEVOS CAMPOS - PLANILLA DE RENDICIÓN
        // ==========================================

        public DateTime? FechaGasto { get; set; }

        public string? DescripcionGasto { get; set; }

        public string? CategoriaGasto { get; set; }
    }
}
