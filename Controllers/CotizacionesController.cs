using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpartanVentasApi.Services;
using SpartanVentasApi.Models;

[ApiController]
[Route("api/[controller]")]
public class CotizacionesController : ControllerBase
{
    private readonly SapServiceLayerClient _sap;

    // 👇 ESTA ES LA INYECCIÓN DEL SERVICIO (lo importante)
    public CotizacionesController(SapServiceLayerClient sap)
    {
        _sap = sap;
    }

    // ============================================
    // Crear cotización en SAP (POST)
    // ============================================
    [Authorize]
    // [Authorize(Roles = "VENDEDOR,ADMIN")]
    [HttpPost("crear")]
    public async Task<IActionResult> CrearCotizacion([FromBody] CotizacionCrearDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await _sap.CrearCotizacionAsync(dto);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error al crear cotización: {ex.Message}");
        }
    }


    // ============================================
    // Descargar PDF generado por SAP
    // ============================================
    [Authorize]
    [HttpGet("{docEntry}/pdf")]
    public async Task<IActionResult> DescargarPdf(int docEntry)
    {
        try
        {
            var pdfBytes = await _sap.ObtenerPdfCotizacionAsync(docEntry);

            return File(
                pdfBytes,
                "application/pdf",
                $"Cotizacion_{docEntry}.pdf"
            );
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"No se pudo descargar el PDF: {ex.Message}");
        }
    }
}
