
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpartanVentasApi.Services.RindeSpartan;
using PDFtoImage;
using SkiaSharp;

namespace SpartanVentasApi.Controllers.RindeSpartan;

[ApiController]
[Route("api/rindespartan")]
[Authorize]
public class RindeComprobantesController : ControllerBase
{
    private const int MaxArchivoBytes = 5 * 1024 * 1024;

    private readonly RindeComprobanteReader _reader;
    private readonly RindeImagenOcrService _ocr;
    private readonly ILogger<RindeComprobantesController> _logger;

    public RindeComprobantesController(
        RindeComprobanteReader reader,
        RindeImagenOcrService ocr,
        ILogger<RindeComprobantesController> logger)
    {
        _reader = reader;
        _ocr = ocr;
        _logger = logger;
    }

    // ======================================================
    // LECTURA DE COMPROBANTES
    // PDF DIGITAL / JPG / JPEG / PNG
    // ======================================================

    [HttpPost("leer-comprobante")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public IActionResult LeerComprobante(IFormFile archivo)
    {
        // ==================================================
        // 1. VALIDACIONES INICIALES
        // ==================================================

        if (archivo == null || archivo.Length == 0)
        {
            return BadRequest(
                "Debe adjuntar un comprobante PDF, JPG o PNG.");
        }

        if (archivo.Length > MaxArchivoBytes)
        {
            return BadRequest(
                "El comprobante supera el máximo de 5 MB.");
        }

        string extension = Path.GetExtension(
            archivo.FileName).ToLowerInvariant();

        if (extension != ".pdf" &&
            extension != ".jpg" &&
            extension != ".jpeg" &&
            extension != ".png")
        {
            return BadRequest(
                "Solo se permiten archivos PDF, JPG y PNG.");
        }

        try
        {
            // ==================================================
            // 2. CARGAR ARCHIVO EN MEMORIA
            // ==================================================

            using var memoria = new MemoryStream();

            using (var origen = archivo.OpenReadStream())
            {
                origen.CopyTo(memoria);
            }

            if (memoria.Length == 0 ||
                memoria.Length > MaxArchivoBytes)
            {
                return BadRequest(
                    "El archivo está vacío o supera los 5 MB.");
            }

            memoria.Position = 0;

            // ==================================================
            // 3. VALIDAR FIRMA REAL DEL ARCHIVO
            // ==================================================

            byte[] cabecera = new byte[8];

            int leidos = memoria.Read(
                cabecera, 0, cabecera.Length);

            bool esPdf =
                leidos >= 5 &&
                cabecera[0] == 0x25 &&
                cabecera[1] == 0x50 &&
                cabecera[2] == 0x44 &&
                cabecera[3] == 0x46 &&
                cabecera[4] == 0x2D;

            bool esJpeg =
                leidos >= 3 &&
                cabecera[0] == 0xFF &&
                cabecera[1] == 0xD8 &&
                cabecera[2] == 0xFF;

            bool esPng =
                leidos >= 8 &&
                cabecera[0] == 0x89 &&
                cabecera[1] == 0x50 &&
                cabecera[2] == 0x4E &&
                cabecera[3] == 0x47 &&
                cabecera[4] == 0x0D &&
                cabecera[5] == 0x0A &&
                cabecera[6] == 0x1A &&
                cabecera[7] == 0x0A;

            bool firmaValida = extension switch
            {
                ".pdf" => esPdf,
                ".jpg" => esJpeg,
                ".jpeg" => esJpeg,
                ".png" => esPng,
                _ => false
            };

            if (!firmaValida)
            {
                return BadRequest(
                    "El contenido del archivo no corresponde " +
                    "al formato indicado o está dañado.");
            }

            memoria.Position = 0;

            // ==================================================
            // 4. PDF DIGITAL / PDF ESCANEADO
            // ==================================================

            if (extension == ".pdf")
            {
                // Primera lectura: intentar extraer texto digital.
                memoria.Position = 0;

                var resultadoPdf = _reader.LeerPdf(memoria);

                // Si el PDF contiene texto digital, conservar
                // el comportamiento actual.
                if (resultadoPdf.MetodoLectura != "REQUIERE_OCR")
                {
                    return Ok(resultadoPdf);
                }

                // PDF escaneado: convertir primera página a imagen.
                memoria.Position = 0;

                using var imagen = Conversion.ToImage(
                    memoria,
                    page: 0,
                    options: new RenderOptions(
                        Dpi: 200
                    )
                );


                using var imagenMemoria = new MemoryStream();

                // Convertir SKBitmap a PNG para Tesseract.
                using (var imagenPng = imagen.Encode(
                    SKEncodedImageFormat.Png,
                    100))
                {
                    if (imagenPng == null)
                        throw new InvalidDataException(
                            "No se pudo convertir el PDF escaneado a imagen.");

                    imagenPng.SaveTo(imagenMemoria);
                }

                imagenMemoria.Position = 0;




                // Enviar la imagen al lector OCR existente.
                var resultadoOcr = _reader.LeerImagen(
                    imagenMemoria,
                    _ocr
                );

                return Ok(resultadoOcr);
            }

            // ==================================================
            // 5. IMAGEN: TESSERACT OCR
            // ==================================================

            var resultadoImagen = _reader.LeerImagen(
                memoria,
                _ocr);

            return Ok(resultadoImagen);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(
                ex,
                "Comprobante inválido en RindeSpartan.");

            return BadRequest(ex.Message);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Error de lectura del comprobante.");

            return BadRequest(
                "No se pudo leer el archivo adjunto.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error interno al procesar comprobante " +
                "en RindeSpartan.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "No se pudo procesar el comprobante. " +
                "Contacte a Informática si el problema persiste.");
        }
    }








}
