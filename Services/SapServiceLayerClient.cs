using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SpartanVentasApi.Models;

namespace SpartanVentasApi.Services
{
    public class SapServiceLayerOptions
    {
        public string BaseUrl { get; set; } = string.Empty;   // ej: https://servidor:50000/b1s/v1/
        public string CompanyDB { get; set; } = string.Empty; // ej: SPARTAN_PRODUCTIVO_2024
        public string UserName { get; set; } = string.Empty;  // ej: manager
        public string Password { get; set; } = string.Empty;  // clave SL
    }

    public class SapServiceLayerClient : IDisposable
    {
        private readonly SapServiceLayerOptions _opts;
        private readonly HttpClient _http;
        private bool _loggedIn;

        public SapServiceLayerClient(IConfiguration config)
        {
            _opts = config.GetSection("SapServiceLayer").Get<SapServiceLayerOptions>()
                    ?? throw new InvalidOperationException("Falta sección SapServiceLayer en appsettings.");

            var cookies = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = cookies,
                // SOLO para testing con certificado no confiable:
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/")
            };
        }

        // =========================================================
        // LOGIN
        // =========================================================
        private async Task EnsureLoginAsync()
        {
            if (_loggedIn) return;

            var payload = new
            {
                CompanyDB = _opts.CompanyDB,
                UserName = _opts.UserName,
                Password = _opts.Password
            };

            var resp = await _http.PostAsJsonAsync("Login", payload);
            if (!resp.IsSuccessStatusCode)
            {
                var txt = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Error login Service Layer: {(int)resp.StatusCode} - {txt}");
            }

            _loggedIn = true;
        }

        // =========================================================
        // CREAR COTIZACIÓN (OQUT/QUT1)
        // =========================================================
        /// <summary>
        /// Crea una cotización via Service Layer y devuelve el DocEntry.
        /// </summary>
        public async Task<int> CrearCotizacionAsync(CotizacionCrearDto dto)
        {
            await EnsureLoginAsync();

            var payload = new
            {
                CardCode = dto.CardCode,
                DocDate = dto.DocDate.ToString("yyyy-MM-dd"),
                DocDueDate = dto.DocDueDate.ToString("yyyy-MM-dd"),
                SalesPersonCode = dto.SalesPersonCode,
                Comments = dto.Comments,

                Address = dto.BillingAddress,
                ShipToDescription = dto.ShippingAddress,
                // ContactPersonCode: lo puedes completar luego cuando tengas mapeado el contacto

                DocumentLines = dto.DocumentLines.Select(l => new
                {
                    ItemCode = l.ItemCode,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    WarehouseCode = l.WarehouseCode,
                    TaxCode = l.TaxCode
                })
            };

            var resp = await _http.PostAsJsonAsync("Quotations", payload);
            if (!resp.IsSuccessStatusCode)
            {
                var txtError = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Error creando cotización en SAP: {(int)resp.StatusCode} - {txtError}");
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            return json.RootElement.GetProperty("DocEntry").GetInt32();
        }

        // =========================================================
        // OBTENER CLIENTE
        // =========================================================
        /// <summary>
        /// Devuelve datos básicos de un BusinessPartner para la pantalla de cotizaciones.
        /// </summary>
        public async Task<ClienteDto?> ObtenerClienteAsync(string cardCode)
        {
            await EnsureLoginAsync();

            // Llamamos a BusinessPartners('CXXXX')
            var url = $"BusinessPartners('{WebUtility.UrlEncode(cardCode)}')?$expand=BPAddresses";
            var resp = await _http.GetAsync(url);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return null; // el controller devolverá 404
            }

            if (!resp.IsSuccessStatusCode)
            {
                var txt = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Error obteniendo cliente en SAP: {(int)resp.StatusCode} - {txt}");
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            var root = json.RootElement;

            var dto = new ClienteDto
            {
                CardCode = root.TryGetProperty("CardCode", out var cc)
                            ? cc.GetString() ?? string.Empty
                            : string.Empty,
                CardName = root.TryGetProperty("CardName", out var cn)
                            ? cn.GetString() ?? string.Empty
                            : string.Empty,
                SalesPersonCode = root.TryGetProperty("SalesPersonCode", out var spc)
                            ? spc.GetInt32()
                            : (int?)null,
                SalesPersonName = root.TryGetProperty("SalesPersonName", out var spn)
                            ? spn.GetString()
                            : null
            };

            // Direcciones (BPAddresses viene como arreglo)
            if (root.TryGetProperty("BPAddresses", out var addrsElem) &&
                addrsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var addr in addrsElem.EnumerateArray())
                {
                    var addrType = addr.TryGetProperty("AddressType", out var t)
                        ? t.GetString()
                        : null;

                    var street = addr.TryGetProperty("Street", out var st) ? st.GetString() : "";
                    var block = addr.TryGetProperty("Block", out var bl) ? bl.GetString() : "";
                    var city = addr.TryGetProperty("City", out var ct) ? ct.GetString() : "";

                    var full = string.Join(" ",
                        new[] { street, block, city }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (addrType == "bo_BillTo")
                        dto.BillingAddress = full;
                    else if (addrType == "bo_ShipTo")
                        dto.ShippingAddress = full;
                }
            }

            // Contacto (por ahora dejamos el código o vacío)
            if (root.TryGetProperty("ContactPerson", out var cntElem) &&
                cntElem.ValueKind == JsonValueKind.Number)
            {
                dto.ContactName = cntElem.GetInt32().ToString();
                // Si más adelante expandes contactos, aquí puedes mapear el nombre real
            }

            return dto;
        }

        /// <summary>
        /// Obtiene el PDF de una cotización ya existente.
        /// Devuelve los bytes del archivo PDF para que el controlador los entregue como File().
        /// </summary>
        public async Task<byte[]> ObtenerPdfCotizacionAsync(int docEntry)
        {
            await EnsureLoginAsync();

            // IMPORTANTE:
            // La URL exacta para imprimir/exportar a PDF puede variar según
            // tu versión de SAP B1 y cómo tengas configurados los layouts.
            //
            // Aquí dejo un ejemplo típico. Si en tu entorno el endpoint es distinto
            // (p.ej. usa $export, PrintPreview, ReportLayouts, etc.), solo ajusta la URL:
            //
            //   var url = $"Quotations({docEntry})/$print";
            //
            // Revisa la documentación de tu Service Layer o prueba la URL con Postman.

            var url = $"Quotations({docEntry})/$print";

            var resp = await _http.PostAsync(url, content: null);
            if (!resp.IsSuccessStatusCode)
            {
                var txtError = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Error obteniendo PDF de cotización {docEntry}: {(int)resp.StatusCode} - {txtError}");
            }

            // Se asume que el cuerpo de la respuesta es el PDF en binario.
            var pdfBytes = await resp.Content.ReadAsByteArrayAsync();
            return pdfBytes;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
