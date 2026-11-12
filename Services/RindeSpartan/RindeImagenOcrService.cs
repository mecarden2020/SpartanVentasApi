using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;

namespace SpartanVentasApi.Services.RindeSpartan;

public sealed record RindeOcrLectura(
    string Metodo,
    string Texto,
    float Confianza,
    int Puntuacion);

public class RindeImagenOcrService
{
    private readonly string _rutaIdiomas;

    public RindeImagenOcrService(IWebHostEnvironment entorno)
    {
        _rutaIdiomas = Path.Combine(entorno.ContentRootPath, "tessdata");
    }

    // Conserva compatibilidad con el lector que espera un solo texto.
    public string ExtraerTexto(Stream archivo)
    {
        return ExtraerLecturas(archivo)
            .OrderByDescending(x => x.Puntuacion)
            .ThenByDescending(x => x.Confianza)
            .First().Texto;
    }

    public IReadOnlyList<RindeOcrLectura> ExtraerLecturas(Stream archivo)
    {
        ArgumentNullException.ThrowIfNull(archivo);

        if (!Directory.Exists(_rutaIdiomas))
            throw new DirectoryNotFoundException("No se encontró la carpeta tessdata.");

        using var memoria = new MemoryStream();
        archivo.CopyTo(memoria);
        byte[] bytes = memoria.ToArray();
        if (bytes.Length == 0)
            throw new InvalidDataException("La imagen está vacía.");

        using var imagenBase = Image.Load<Rgba32>(bytes);
        imagenBase.Mutate(x => x.AutoOrient());

        // Ampliar fotos angostas, sin reducir las de buena resolución.
        // El límite evita generar imágenes enormes en recibos muy largos.
        if (imagenBase.Width < 1600)
        {
            double factor = Math.Min(1600.0 / imagenBase.Width,
                                     5000.0 / imagenBase.Height);
            if (factor > 1.0)
            {
                int ancho = (int)Math.Round(imagenBase.Width * factor);
                int alto = (int)Math.Round(imagenBase.Height * factor);
                imagenBase.Mutate(x => x.Resize(ancho, alto));
            }
        }

        using var motor = new TesseractEngine(
            _rutaIdiomas, "spa+eng", EngineMode.Default);

        var resultados = new List<RindeOcrLectura>(5);
        resultados.Add(EjecutarLectura(motor, imagenBase,
            "ORIGINAL", PageSegMode.Auto));

        using (var gris = imagenBase.Clone())
        {
            gris.Mutate(x => x.Grayscale());
            resultados.Add(EjecutarLectura(motor, gris,
                "GRISES", PageSegMode.Auto));
        }

        using (var contraste = imagenBase.Clone())
        {
            contraste.Mutate(x => x.Grayscale()
                .Contrast(1.15f).GaussianSharpen(0.8f));
            resultados.Add(EjecutarLectura(motor, contraste,
                "CONTRASTE", PageSegMode.Auto));
        }

        using (var termica = imagenBase.Clone())
        {
            termica.Mutate(x => x.Grayscale()
                .Contrast(1.35f).GaussianSharpen(1.2f));
            resultados.Add(EjecutarLectura(motor, termica,
                "TERMICA", PageSegMode.SingleColumn));
        }

        using (var bloque = imagenBase.Clone())
        {
            bloque.Mutate(x => x.Grayscale().Contrast(1.10f));
            resultados.Add(EjecutarLectura(motor, bloque,
                "BLOQUE", PageSegMode.SingleBlock));
        }

        // Lectura adicional enfocada en la zona inferior del comprobante.
        // Se añade a las lecturas ORIGINAL y GRISES, en vez de generar
        // votos independientes que distorsionarían el consenso del lector.
        if (imagenBase.Height >= 500)
        {
            int inicioY = (int)Math.Floor(imagenBase.Height * 0.48);
            int alto = imagenBase.Height - inicioY;
            using var inferior = imagenBase.Clone(x => x.Crop(
                new Rectangle(0, inicioY, imagenBase.Width, alto)));

            // Ampliación local moderada para cifras pequeñas.
            if (inferior.Width < 1800 && inferior.Height < 4500)
            {
                double factor = Math.Min(1800.0 / inferior.Width,
                                         4500.0 / inferior.Height);
                if (factor > 1.0)
                {
                    inferior.Mutate(x => x.Resize(
                        (int)Math.Round(inferior.Width * factor),
                        (int)Math.Round(inferior.Height * factor)));
                }
            }

            var zonaOriginal = EjecutarLectura(motor, inferior,
                "ZONA_INFERIOR_ORIGINAL", PageSegMode.SingleBlock);

            using var inferiorGris = inferior.Clone();
            inferiorGris.Mutate(x => x.Grayscale()
                .Contrast(1.20f).GaussianSharpen(0.7f));
            var zonaGris = EjecutarLectura(motor, inferiorGris,
                "ZONA_INFERIOR_GRISES", PageSegMode.SingleBlock);

            resultados[0] = AgregarZona(resultados[0], zonaOriginal);
            resultados[1] = AgregarZona(resultados[1], zonaGris);

#if DEBUG
            System.Diagnostics.Debug.WriteLine("=== OCR ZONA INFERIOR ORIGINAL ===");
            System.Diagnostics.Debug.WriteLine(zonaOriginal.Texto);
            System.Diagnostics.Debug.WriteLine("=== OCR ZONA INFERIOR GRISES ===");
            System.Diagnostics.Debug.WriteLine(zonaGris.Texto);
#endif
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine("========== RINDESPARTAN OCR MULTILECTURA ==========");
        foreach (var r in resultados)
        {
            System.Diagnostics.Debug.WriteLine($"Método: {r.Metodo}");
            System.Diagnostics.Debug.WriteLine($"Caracteres: {r.Texto.Length}");
            System.Diagnostics.Debug.WriteLine($"Confianza: {r.Confianza:P1}");
            System.Diagnostics.Debug.WriteLine($"Puntuación: {r.Puntuacion}");
            System.Diagnostics.Debug.WriteLine(r.Texto);
            System.Diagnostics.Debug.WriteLine("------------------------------------------");
        }
        var mejor = resultados.OrderByDescending(x => x.Puntuacion)
            .ThenByDescending(x => x.Confianza).First();
        System.Diagnostics.Debug.WriteLine($"MÉTODO SELECCIONADO: {mejor.Metodo}");
        System.Diagnostics.Debug.WriteLine("========== FIN OCR MULTILECTURA ==========");
#endif
        return resultados.AsReadOnly();
    }

    private static RindeOcrLectura AgregarZona(
        RindeOcrLectura lecturaCompleta, RindeOcrLectura zona)
    {
        if (string.IsNullOrWhiteSpace(zona.Texto))
            return lecturaCompleta;

        // Separador legible para el diagnóstico, sin cambiar el contrato JSON.
        string combinado = lecturaCompleta.Texto +
            "\n\n--- ZONA INFERIOR ---\n" + zona.Texto;
        return lecturaCompleta with
        {
            Texto = combinado,
            Puntuacion = CalcularPuntuacion(combinado, lecturaCompleta.Confianza)
        };
    }

    private static RindeOcrLectura EjecutarLectura(
        TesseractEngine motor, Image imagen,
        string metodo, PageSegMode segmentacion)
    {
        using var memoria = new MemoryStream();
        imagen.Save(memoria, new PngEncoder());
        using var pix = Pix.LoadFromMemory(memoria.ToArray());
        using var pagina = motor.Process(pix, segmentacion);
        string texto = pagina.GetText() ?? string.Empty;
        float confianza = pagina.GetMeanConfidence();
        return new RindeOcrLectura(metodo, texto, confianza,
            CalcularPuntuacion(texto, confianza));
    }

    private static int CalcularPuntuacion(string texto, float confianza)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        int puntos = 0;

        if (Regex.IsMatch(texto,
            @"\b\d{1,2}(?:\.\d{3}|\d{3})(?:\.\d{3}|\d{3})\s*-\s*[\dKk]\b"))
            puntos += 30;
        if (Regex.IsMatch(texto, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{4}\b"))
            puntos += 30;
        if (Regex.IsMatch(texto,
            @"\b(total|valor|monto|pesos|pagar)\b", RegexOptions.IgnoreCase))
            puntos += 20;
        if (Regex.IsMatch(texto,
            @"\b(boleta|factura|peaje|estaci[oó]n|transbank)\b",
            RegexOptions.IgnoreCase))
            puntos += 10;

        // Bonificación si el texto contiene etiqueta y cifra monetaria.
        if (Regex.IsMatch(texto,
            @"(?im)\b(?:TOTAL|MONTO\s+VENTA|VALOR)\s*[:$]?\s*\$?\s*\d[\d.,]*"))
            puntos += 15;

        puntos += (int)Math.Round(Math.Clamp(confianza, 0f, 1f) * 20);
        return puntos;
    }
}
