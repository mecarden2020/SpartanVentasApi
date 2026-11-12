
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SpartanVentasApi.Models.RindeSpartan;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PDFtoImage;
using SkiaSharp;

namespace SpartanVentasApi.Services.RindeSpartan;

public class RindeComprobanteReader
{
    private static readonly CultureInfo CulturaCL =
        CultureInfo.GetCultureInfo("es-CL");

    private const string PatronRut =
        @"(?<!\d)(\d{1,2}\.?\d{3}\.?\d{3}-[\dKk])(?!\w)";

    public RindeLecturaComprobanteResult LeerPdf(Stream archivo)
    {
        var resultado = new RindeLecturaComprobanteResult();

        using var documento = PdfDocument.Open(archivo);

        resultado.Paginas = documento.NumberOfPages;

        if (resultado.Paginas > 20)
            throw new InvalidDataException(
                "El comprobante supera el máximo de 20 páginas.");

        var texto = new StringBuilder();

        foreach (var pagina in documento.GetPages())
        {
            texto.AppendLine(
                ContentOrderTextExtractor.GetText(pagina));
        }

        string contenido = texto.ToString();

        // ================================================
        // DIAGNÓSTICO TEMPORAL - SOLO DESARROLLO
        // ================================================
#if DEBUG
        string carpetaDiagnostico = Path.Combine(
            Path.GetTempPath(),
            "RindeSpartan",
            "DiagnosticoPdf");

        Directory.CreateDirectory(carpetaDiagnostico);

        string archivoDiagnostico = Path.Combine(
            carpetaDiagnostico,
            $"lectura_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.txt");

        File.WriteAllText(
            archivoDiagnostico,
            contenido,
            Encoding.UTF8);
#endif




        if (string.IsNullOrWhiteSpace(contenido) ||
            contenido.Trim().Length < 30)
        {
            resultado.MetodoLectura = "REQUIERE_OCR";

            resultado.Advertencias.Add(
                "El PDF no contiene suficiente texto digital. " +
                "Se necesitará OCR.");

            return resultado;
        }

        return InterpretarTexto(
            contenido,
            "PDF_TEXTO",
            resultado.Paginas);


    }



    public RindeLecturaComprobanteResult LeerPdfConOcr(
        Stream archivo,
        RindeImagenOcrService servicioOcr)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        ArgumentNullException.ThrowIfNull(servicioOcr);

        using var memoriaPdf = new MemoryStream();
        archivo.CopyTo(memoriaPdf);

        byte[] pdfBytes = memoriaPdf.ToArray();

        if (pdfBytes.Length == 0)
            throw new InvalidDataException("El PDF está vacío.");

        // Primera etapa: procesar la primera página.
        using var imagen = Conversion.ToImage(
            pdfBytes,
            page: 0);

        using var imagenPng = imagen.Encode(
            SKEncodedImageFormat.Png,
            100);

        using var memoriaImagen = new MemoryStream();
        imagenPng.SaveTo(memoriaImagen);
        memoriaImagen.Position = 0;

        // Reutilizar nuestro OCR multilectura.
        var resultado = LeerImagen(
            memoriaImagen,
            servicioOcr);

        resultado.MetodoLectura = "OCR_PDF";
        resultado.Paginas = 1;
        resultado.RequiereRevision = true;

        return resultado;
    }










    // Sustituir SOLO el método LeerImagen en RindeComprobanteReader.cs.
    // Requiere RindeImagenOcrService.cs actualizado con ExtraerLecturas().
    private static string NormalizarNombreParaConsenso(string valor)
    {
        var sinAcentos = valor.Normalize(NormalizationForm.FormD);
        sinAcentos = new string(sinAcentos.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(sinAcentos.ToUpperInvariant(), @"[^A-Z0-9]", "");
    }

    public RindeLecturaComprobanteResult LeerImagen(
        Stream archivo,
        RindeImagenOcrService servicioOcr)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        ArgumentNullException.ThrowIfNull(servicioOcr);

        var lecturas = servicioOcr.ExtraerLecturas(archivo)
            .OrderByDescending(x => x.Puntuacion)
            .ThenByDescending(x => x.Confianza)
            .ToList();

        // Cada interpretación utiliza las mismas reglas del lector PDF.
        var candidatos = lecturas
            .Select(x => (Lectura: x,
                Datos: InterpretarTexto(x.Texto, "OCR_IMAGEN", 1)))
            .ToList();

        if (candidatos.Count == 0)
            return new RindeLecturaComprobanteResult
            {
                MetodoLectura = "OCR_MULTILECTURA",
                Paginas = 1,
                RequiereRevision = true
            };

        var resultado = candidatos[0].Datos;
        resultado.MetodoLectura = "OCR_MULTILECTURA";

        // Por campo, preferir consenso entre lecturas independientes.
        // Ante empate entre valores diferentes, no completar automáticamente.
        var advertenciasComparacion = new List<string>();

        string? ElegirTexto(Func<RindeLecturaComprobanteResult, string?> selector,
            string campo)
        {
            var grupos = candidatos
                .Select(x => (Valor: selector(x.Datos)?.Trim(), x.Lectura.Puntuacion))
                .Where(x => !string.IsNullOrWhiteSpace(x.Valor))
                .GroupBy(x => campo == "proveedor" ? NormalizarNombreParaConsenso(x.Valor!) : x.Valor!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new {
                    Valor = g.OrderByDescending(x => x.Puntuacion).First().Valor!,
                    Votos = g.Count(),
                    MejorPuntuacion = g.Max(x => x.Puntuacion)
                })
                .OrderByDescending(x => x.Votos)
                .ThenByDescending(x => x.MejorPuntuacion)
                .ToList();

            if (grupos.Count == 0) return null;
            if (grupos.Count > 1 && grupos[0].Votos == grupos[1].Votos)
            {
                advertenciasComparacion.Add(
                    $"Existen lecturas OCR contradictorias para {campo}; revise el comprobante.");
                return null;
            }
            if (grupos.Count > 1)
                advertenciasComparacion.Add(
                    $"Se encontraron diferencias OCR en {campo}; se utilizó el valor mayoritario. Verifique.");
            return grupos[0].Valor;
        }

        DateTime? ElegirFecha()
        {
            var grupos = candidatos
                .Where(x => x.Datos.FechaDocumento.HasValue)
                .GroupBy(x => x.Datos.FechaDocumento!.Value.Date)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(x => x.Lectura.Puntuacion))
                .ToList();
            if (grupos.Count == 0) return null;
            if (grupos.Count > 1 && grupos[0].Count() == grupos[1].Count())
            {
                advertenciasComparacion.Add("Fechas OCR contradictorias; verifique la fecha de emisión.");
                return null;
            }
            return grupos[0].Key;
        }

        decimal? ElegirMonto()
        {
            var grupos = candidatos
                .Where(x => x.Datos.MontoTotal.HasValue)
                .GroupBy(x => x.Datos.MontoTotal!.Value)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(x => x.Lectura.Puntuacion))
                .ToList();
            if (grupos.Count == 0) return null;
            if (grupos.Count > 1 && grupos[0].Count() == grupos[1].Count())
            {
                advertenciasComparacion.Add("Montos OCR contradictorios; verifique el total pagado.");
                return null;
            }
            if (grupos.Count > 1)
                advertenciasComparacion.Add("Hay montos diferentes entre lecturas OCR; verifique el total seleccionado.");
            return grupos[0].Key;
        }

        resultado.TipoDocumento = ElegirTexto(x => x.TipoDocumento, "tipo de documento");
        resultado.NumeroDocumento = ElegirTexto(x => x.NumeroDocumento, "folio");
        resultado.RutProveedor = ElegirTexto(x => x.RutProveedor, "RUT del emisor");


        resultado.Proveedor = ElegirTexto(
            x => EsProveedorConfiable(x.Proveedor)
                ? x.Proveedor
                : null,
            "proveedor");



        resultado.FechaDocumento = ElegirFecha();
        resultado.MontoTotal = ElegirMonto();

        // Reconstruir advertencias a partir de los campos finales.
        resultado.Advertencias.Clear();
        foreach (var advertencia in advertenciasComparacion)
            resultado.Advertencias.Add(advertencia);
        if (resultado.TipoDocumento == null)
            resultado.Advertencias.Add("No se reconoció el tipo de documento.");
        if (resultado.NumeroDocumento == null)
            resultado.Advertencias.Add("No se reconoció un folio confiable.");
        if (resultado.RutProveedor == null)
            resultado.Advertencias.Add("No se reconoció un RUT válido del emisor.");
        if (resultado.Proveedor == null)
            resultado.Advertencias.Add("No se identificó con certeza la razón social del emisor.");
        if (resultado.FechaDocumento == null)
            resultado.Advertencias.Add("No se reconoció una fecha de emisión confiable.");
        if (resultado.MontoTotal == null)
            resultado.Advertencias.Add("No se reconoció un monto total confiable.");
        resultado.LecturaExitosa =
            resultado.TipoDocumento != null &&
            resultado.NumeroDocumento != null &&
            resultado.RutProveedor != null &&
            resultado.Proveedor != null &&
            resultado.FechaDocumento != null &&
            resultado.MontoTotal != null;
        resultado.RequiereRevision = true;
        return resultado;
    }













    public RindeLecturaComprobanteResult InterpretarTexto(
    string contenido,
    string metodoLectura = "PDF_TEXTO",
    int paginas = 1)
    {
        var resultado = new RindeLecturaComprobanteResult
        {
            Paginas = paginas,
            MetodoLectura = metodoLectura,
            RequiereRevision = true
        };

        if (string.IsNullOrWhiteSpace(contenido) ||
            contenido.Trim().Length < 30)
        {
            resultado.Advertencias.Add(
                "No se obtuvo suficiente texto del comprobante. " +
                "Revise la nitidez, iluminación y orientación " +
                "de la imagen.");

            return resultado;
        }

        string normalizado = Regex.Replace(
            contenido,
            @"[ \t]+",
            " ");

        resultado.TipoDocumento =
            DetectarTipo(normalizado);

        resultado.NumeroDocumento =
            BuscarFolio(normalizado);

        bool esComprobantePago = resultado.TipoDocumento == null &&
            Regex.IsMatch(normalizado,
                @"MERCADO\s*PAGO|D[ÉE]BITO\s+MASTERCARD|DUPLICADO\s+CLIENTE",
                RegexOptions.IgnoreCase);

        resultado.RutProveedor = esComprobantePago
            ? null
            : BuscarRutEmisor(normalizado);

        resultado.Proveedor = BuscarProveedor(
            normalizado,
            resultado.RutProveedor);

        resultado.FechaDocumento =
            BuscarFecha(normalizado);

        resultado.MontoTotal =
            BuscarMontoTotal(normalizado);

        if (resultado.TipoDocumento == null)
            resultado.Advertencias.Add(
                Regex.IsMatch(normalizado, @"MERCADO\s*PAGO|D[ÉE]BITO\s+MASTERCARD|DUPLICADO\s+CLIENTE", RegexOptions.IgnoreCase)
                    ? "Se identificó un posible comprobante de pago, pero no una boleta o factura tributaria. Verifique el respaldo exigido por Contabilidad."
                    : "No se reconoció el tipo de documento.");

        if (resultado.NumeroDocumento == null)
            resultado.Advertencias.Add(
                "No se reconoció el número de documento.");

        if (resultado.RutProveedor == null)
            resultado.Advertencias.Add(
                "No se reconoció un RUT válido del emisor.");

        if (resultado.Proveedor == null)
            resultado.Advertencias.Add(
                "No se pudo identificar con certeza " +
                "la razón social del emisor.");

        if (resultado.FechaDocumento == null)
            resultado.Advertencias.Add(
                "No se reconoció la fecha de emisión.");

        if (resultado.MontoTotal == null)
            resultado.Advertencias.Add(
                "No se identificó un total confiable.");

        resultado.LecturaExitosa =
            resultado.TipoDocumento != null &&
            resultado.NumeroDocumento != null &&
            resultado.RutProveedor != null &&
            resultado.Proveedor != null &&
            resultado.FechaDocumento != null &&
            resultado.MontoTotal != null;

        return resultado;
    }







    private static string? DetectarTipo(string texto)
    {
        if (Regex.IsMatch(
            texto,
            @"BOLETA\s+ELECTR[ÓO]NICA",
            RegexOptions.IgnoreCase))
            return "BOLETA";

        if (Regex.IsMatch(
            texto,
            @"FACTURA\s+ELECTR[ÓO]NICA",
            RegexOptions.IgnoreCase))
            return "FACTURA";

        return null;
    }


    private static string? BuscarFolio(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        // La zona tributaria es más confiable que números de OC o referencias.
        var patrones = new[]
        {
            @"(?:FACTURA|BOLETA)\s+ELECTR[ÓO0]NICA[\s\S]{0,100}?\bN[°ºoO.]?\s*[:#-]?\s*((?:\d[ \t]*){3,12})\b",
            @"\b(?:FOLIO|N[ÚU]MERO\s+(?:DE\s+)?(?:FACTURA|BOLETA)|NRO\.?\s+(?:FACTURA|BOLETA))\s*[:#-]?\s*((?:\d[ \t]*){3,12})\b",
            @"(?:FACTURA|BOLETA)\s+ELECTR[ÓO0]NICA\s*(?:N[°ºoO.]?|NRO\.?|N[ÚU]MERO)?\s*[:#-]?\s*((?:\d[ \t]*){3,12})\b"
        };
        foreach (var patron in patrones)
            foreach (Match m in Regex.Matches(texto, patron, RegexOptions.IgnoreCase))
            {
                var numero = Regex.Replace(m.Groups[1].Value, @"\s", "");
                if (numero.Length >= 3 && numero.Length <= 12) return numero;
            }
        return null;
    }


    private static string? BuscarRutEmisor(string texto)
    {
        // Se examina la cabecera del documento.
        // No se asume que cualquier RUT sea del emisor.

        string cabecera = texto.Length > 1800
            ? texto[..1800]
            : texto;

        var coincidencias = Regex.Matches(
            cabecera,
            PatronRut,
            RegexOptions.IgnoreCase);

        foreach (Match coincidencia in coincidencias)
        {
            string rut = coincidencia.Groups[1].Value;

            if (ValidarRut(rut))
                return NormalizarRut(rut);
        }

        return null;
    }

    private static bool ValidarRut(string rut)
    {
        string limpio = rut
            .Replace(".", "")
            .Replace("-", "")
            .ToUpperInvariant();

        if (limpio.Length < 2)
            return false;

        string cuerpo = limpio[..^1];
        char dv = limpio[^1];

        if (!int.TryParse(cuerpo, out int numero) ||
            numero <= 0)
            return false;

        int suma = 0;
        int factor = 2;

        while (numero > 0)
        {
            suma += (numero % 10) * factor;
            numero /= 10;

            factor = factor == 7 ? 2 : factor + 1;
        }

        int resto = 11 - (suma % 11);

        char dvCalculado = resto switch
        {
            11 => '0',
            10 => 'K',
            _ => (char)('0' + resto)
        };

        return dv == dvCalculado;
    }

    private static string NormalizarRut(string rut)
    {
        return rut.Replace(".", "")
                  .Trim()
                  .ToUpperInvariant();
    }


    private static string? BuscarProveedor(
        string texto,
        string? rutEmisor)
    {
        if (string.IsNullOrWhiteSpace(texto) ||
            string.IsNullOrWhiteSpace(rutEmisor))
            return null;

        string[] lineas = texto.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x.Trim(), @"\s+", " "))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        string rutNormalizado = NormalizarRut(rutEmisor);

        int limiteEmisor = lineas.Length;

        for (int i = 0; i < lineas.Length; i++)
        {
            if (Regex.IsMatch(
                lineas[i],
                @"^(?:DATOS\s+DEL\s+CLIENTE|SR\.?\s*\(ES\)|" +
                @"SE[ÑN]OR(?:ES)?|RECEPTOR)\s*:?",
                RegexOptions.IgnoreCase))
            {
                limiteEmisor = i;
                break;
            }
        }

        var candidatos = new List<(string Nombre, int Puntos)>();

        void AgregarCandidato(string valor, int puntos)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return;

            string nombre = LimpiarProveedor(valor);

            nombre = Regex.Replace(
                nombre,
                @"\s+",
                " ").Trim();

            if (!EsNombreEmisorValido(nombre))
                return;

            if (nombre.Length < 5 || nombre.Length > 120)
                return;

            if (Regex.IsMatch(
                nombre,
                @"^(?:GIRO|RUT|R\.U\.T|FACTURA|BOLETA|" +
                @"DIRECCION|DIRECCIÓN|TELEFONO|TELÉFONO|" +
                @"SUCURSAL|CASA MATRIZ)\b",
                RegexOptions.IgnoreCase))
                return;

            bool tieneFormaJuridica = Regex.IsMatch(
                nombre,
                @"\b(?:S\.?\s*A\.?|SPA|LTDA\.?|LIMITADA|" +
                @"E\.?\s*I\.?\s*R\.?\s*L\.?)\b",
                RegexOptions.IgnoreCase);

            if (tieneFormaJuridica)
                puntos += 35;

            int palabras = nombre.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries).Length;

            if (palabras >= 3)
                puntos += 10;

            if (palabras >= 5)
                puntos += 5;

            if (Regex.IsMatch(
                nombre,
                @"(?:FACTURA\s+ELECTRONICA|S\.I\.I\.|" +
                @"FECHA\s+EMISION|TOTAL\s+A\s+PAGAR)",
                RegexOptions.IgnoreCase))
                puntos -= 60;

            candidatos.Add((nombre, puntos));
        }

        // 1. Razón social identificada explícitamente.
        for (int i = 0; i < limiteEmisor; i++)
        {
            var match = Regex.Match(
                lineas[i],
                @"(?:RAZ[ÓO]N\s+SOCIAL(?:\s+DEL\s+EMISOR)?|" +
                @"NOMBRE\s+DEL\s+EMISOR|EMISOR)\s*:\s*(.+)",
                RegexOptions.IgnoreCase);

            if (match.Success)
                AgregarCandidato(match.Groups[1].Value, 100);
        }

        // 2. Líneas próximas al RUT del emisor.
        for (int i = 0; i < limiteEmisor; i++)
        {
            if (!ContieneRut(lineas[i], rutNormalizado))
                continue;

            for (int j = Math.Max(0, i - 4); j < i; j++)
            {
                int distancia = i - j;
                AgregarCandidato(
                    lineas[j],
                    75 - distancia * 5);
            }
        }

        // 3. Buscar nombres próximos a GIRO.
        for (int i = 0; i < Math.Min(limiteEmisor, 15); i++)
        {
            var match = Regex.Match(
                lineas[i],
                @"^(.+?)\s+GIRO\s*:",
                RegexOptions.IgnoreCase);

            if (match.Success)
                AgregarCandidato(match.Groups[1].Value, 85);

            if (Regex.IsMatch(
                lineas[i],
                @"^\s*GIRO\s*:",
                RegexOptions.IgnoreCase))
            {
                for (int j = Math.Max(0, i - 3); j < i; j++)
                    AgregarCandidato(lineas[j], 65);
            }
        }

        // 4. Evaluar líneas de la cabecera del documento.
        for (int i = 0; i < Math.Min(limiteEmisor, 12); i++)
        {
            string linea = lineas[i];

            if (Regex.IsMatch(
                linea,
                @"\b(?:S\.?\s*A\.?|SPA|LTDA\.?|LIMITADA|" +
                @"E\.?\s*I\.?\s*R\.?\s*L\.?)\b",
                RegexOptions.IgnoreCase))
            {
                AgregarCandidato(linea, 60);
            }
        }

        // 5. Mantener búsqueda de nombre asociado al RUT.
        for (int i = 0; i < lineas.Length; i++)
        {
            var match = Regex.Match(
                lineas[i],
                @"^NOMBRE\s+DE\s*:\s*(.+)$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                continue;

            bool rutCoincidente = false;

            for (int j = i + 1;
                 j <= Math.Min(i + 3, lineas.Length - 1);
                 j++)
            {
                if (ContieneRut(lineas[j], rutNormalizado))
                {
                    rutCoincidente = true;
                    break;
                }
            }

            if (rutCoincidente)
                AgregarCandidato(match.Groups[1].Value, 90);
        }

        if (candidatos.Count == 0)
            return null;

        return candidatos
            .OrderByDescending(x => x.Puntos)
            .ThenByDescending(x => x.Nombre.Length)
            .First()
            .Nombre;
    }


    private static bool ContieneRut(
        string linea,
        string rutNormalizado)
    {
        var coincidencias = Regex.Matches(
            linea,
            PatronRut,
            RegexOptions.IgnoreCase);

        return coincidencias.Cast<Match>().Any(m =>
            NormalizarRut(m.Groups[1].Value) ==
            rutNormalizado);
    }

    private static bool EsNombreEmisorValido(string candidato)
    {
        if (string.IsNullOrWhiteSpace(candidato)) return false;
        candidato = candidato.Trim();
        if (candidato.Length < 7 || candidato.Length > 120) return false;
        if (Regex.IsMatch(candidato, @"\d{2,}")) return false;
        if (Regex.IsMatch(candidato, @"^(.)\1*(?:\s+\1+)*$", RegexOptions.IgnoreCase)) return false;
        var palabras = Regex.Matches(candidato, @"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{2,}")
            .Cast<Match>().Select(m => m.Value).ToArray();
        if (palabras.Length < 2 || !palabras.Any(x => x.Length >= 4)) return false;
        string[] excluidos = {
            "CLIENTE", "SPARTAN DE CHILE", "SEÑOR", "SEÑORES", "SR.(ES)",
            "RECEPTOR", "DESTINATARIO", "MESA CENTRAL", "TELÉFONO", "TELEFONO",
            "DIRECCIÓN", "DIRECCION", "COMUNA", "CIUDAD", "FACTURA", "BOLETA",
            "TOTAL", "SUCURSAL", "CONTACTO", "WWW.", "CORREO", "CASA MATRIZ",
            "FECHA", "S.I.I.", "GIRO", "VENDEDOR", "ORDEN DE COMPRA"
        };
        return !excluidos.Any(x => candidato.Contains(x, StringComparison.OrdinalIgnoreCase));
    }







    private static bool EsNombreProveedorValido(
        string candidato)
    {
        if (string.IsNullOrWhiteSpace(candidato))
            return false;

        if (candidato.Length < 5 ||
            candidato.Length > 120)
            return false;

        if (!Regex.IsMatch(candidato, @"[A-Za-zÁÉÍÓÚÑáéíóúñ]"))
            return false;

        string[] textosExcluidos =
        {
            "FACTURA",
            "BOLETA",
            "S.I.I.",
            "FECHA",
            "GIRO:",
            "DIRECCION",
            "DIRECCIÓN",
            "COMUNA",
            "CASA MATRIZ",
            "TOTAL",
            "CLIENTE",
            "SUCURSAL",
            "TELEFONO",
            "TELÉFONO",
            "WWW."
        };

        return !textosExcluidos.Any(x =>
            candidato.Contains(
                x,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string LimpiarProveedor(string valor)
    {
        return Regex.Replace(
            valor.Trim(),
            @"\s+",
            " ");
    }

    // Priorizar fecha de emisión, aceptar formatos chilenos e ISO.
    // No deducir una fecha desde números aislados del comprobante.
    private static DateTime? BuscarFecha(string texto)
    {
        string[] formatos = {
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy",
            "d/M/yy", "dd/MM/yy", "d-M-yy", "dd-MM-yy", "d.M.yy", "dd.MM.yy",
            "yyyy-M-d", "yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd", "yyyy.M.d", "yyyy.MM.dd",
            "d 'de' MMMM 'de' yyyy", "dd 'de' MMMM 'de' yyyy", "d MMMM yyyy", "dd MMMM yyyy"
        };
        const string fecha = @"(\d{4}[/.-]\d{1,2}[/.-]\d{1,2}|\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}|\d{1,2}\s+(?:de\s+)?[a-záéíóú]+\s+(?:de\s+)?\d{4})";
        // Se busca dentro de la misma línea y, si OCR dividió columnas, en la siguiente.
        var lineas = texto.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var etiquetas = new[] {
            @"\bFECHA\s+(?:DE\s+)?EMISI[ÓO0]N\b",
            @"\bFECHA\s+(?:DEL?\s+)?DOCUMENTO\b",
            @"\bFECHA\s+(?:DE\s+)?VENTA\b",
            @"\b(?:EMITIDO|EMISI[ÓO0]N)\s*[:\-]"
        };
        for (int i = 0; i < lineas.Length; i++)
        {
            foreach (var etiqueta in etiquetas)
            {
                var e = Regex.Match(lineas[i], etiqueta, RegexOptions.IgnoreCase);
                if (!e.Success) continue;
                var resto = lineas[i][(e.Index + e.Length)..];
                var candidatos = new List<string> { resto };
                if (i + 1 < lineas.Length) candidatos.Add(lineas[i + 1]);
                foreach (var zona in candidatos)
                {
                    var m = Regex.Match(zona, fecha, RegexOptions.IgnoreCase);
                    if (m.Success && DateTime.TryParseExact(
                        Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " "), formatos,
                        CulturaCL, DateTimeStyles.None, out var valor) &&
                        valor.Year >= 2000 && valor.Date <= DateTime.Today.AddDays(1))
                        return valor.Date;
                }
            }
        }
        return null;
    }

    // Total en CLP: no confundir separadores de miles con decimales.
    // Priorizar el total final y evitar impuestos, vuelto y subtotales.
    private static decimal? BuscarMontoTotal(string texto)
    {
        string[] lineas = texto.Split(new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        var candidatos = new List<(decimal Monto, int Prioridad, int Linea)>();
        string[] etiquetas = {
            @"\bTOTAL\s+A\s+PAGAR\b", @"\bTOTAL\s+PAG(?:ADO|OS)\b",
            @"\bTOTAL\s+(?:DE\s+)?(?:LA\s+)?COMPRA\b",
            @"\b(?:MONTO|IMPORTE)\s+TOTAL\b", @"\bTOTAL\b", @"\bMONTO\s+VENTA\b"
        };
        for (int i = 0; i < lineas.Length; i++)
        {
            string linea = lineas[i];
            if (Regex.IsMatch(linea,
                @"\b(?:SUBTOTAL|TOTAL\s+NETO|IVA|PROPINA|VUELTO|DESCUENTO|IMPUESTO|SALDO)\b",
                RegexOptions.IgnoreCase)) continue;
            for (int prioridad = 0; prioridad < etiquetas.Length; prioridad++)
            {
                Match etiqueta = Regex.Match(linea, etiquetas[prioridad], RegexOptions.IgnoreCase);
                if (!etiqueta.Success) continue;
                string resto = linea[(etiqueta.Index + etiqueta.Length)..];
                decimal? monto = ExtraerMontoClp(resto);
                // El OCR a menudo separa la etiqueta TOTAL de su importe.
                // Solo se acepta la siguiente línea cuando contiene exclusivamente el importe.
                if (!monto.HasValue && i + 1 < lineas.Length &&
                    Regex.IsMatch(lineas[i + 1], @"^\s*\$?\s*\d[\d.,]*\s*$"))
                    monto = ExtraerMontoClp(lineas[i + 1]);
                if (monto.HasValue) candidatos.Add((monto.Value, prioridad, i));
                break;
            }
        }
        if (candidatos.Count == 0) return null;
        return candidatos.OrderBy(x => x.Prioridad)
            .ThenByDescending(x => x.Linea).First().Monto;
    }

    private static decimal? ExtraerMontoClp(string texto)
    {
        // Acepta $44.050, 44.050, $ 15160 y 15,160 (OCR).
        // No acepta fragmentos numéricos pegados a letras o dígitos.
        var coincidencias = Regex.Matches(texto,
            @"(?<![\w.,])\$?\s*(\d{1,3}(?:[.,]\d{3})+|\d{1,9})(?![\w.,])");
        foreach (Match match in coincidencias)
        {
            string valor = match.Groups[1].Value.Replace(".", "").Replace(",", "");
            if (decimal.TryParse(valor, NumberStyles.None,
                CultureInfo.InvariantCulture, out decimal monto) && monto > 0)
                return monto;
        }
        return null;
    }



    private static bool EsProveedorConfiable(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return false;

        string valor = nombre.Trim();

        // Descartar etiquetas y descripciones comerciales.
        if (Regex.IsMatch(valor,
            @"^(?:GIRO|VENTA\s+DE|COMERCIALIZACION\s+DE|" +
            @"DISTRIBUCION\s+DE|FABRICACION\s+DE|" +
            @"ARTICULOS\s+DE|SERVICIOS\s+DE)\b",
            RegexOptions.IgnoreCase))
            return false;

        // Descartar encabezados propios del documento.
        if (Regex.IsMatch(valor,
            @"\b(?:FACTURA\s+ELECTRONICA|BOLETA\s+ELECTRONICA|" +
            @"FECHA\s+EMISION|TOTAL\s+A\s+PAGAR)\b",
            RegexOptions.IgnoreCase))
            return false;

        // Evitar aceptar nombres demasiado cortos.
        if (valor.Length < 6)
            return false;

        return true;
    }









}
