
namespace SpartanVentasApi.Models.RindeSpartan;

public class RindeLecturaComprobanteResult
{
    public bool LecturaExitosa { get; set; }
    public bool RequiereRevision { get; set; } = true;

    public string? TipoDocumento { get; set; }
    public string? NumeroDocumento { get; set; }
    public string? Proveedor { get; set; }
    public string? RutProveedor { get; set; }

    public DateTime? FechaDocumento { get; set; }
    public decimal? MontoTotal { get; set; }

    public int Paginas { get; set; }
    public string MetodoLectura { get; set; } = "PDF_TEXTO";

    public List<string> Advertencias { get; set; } = new();
}

