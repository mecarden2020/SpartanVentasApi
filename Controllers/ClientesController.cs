using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpartanVentasApi.Services;
using System;
using System.Threading.Tasks;

namespace SpartanVentasApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ClientesController : ControllerBase
    {
        private readonly SapServiceLayerClient _sap;

        public ClientesController(SapServiceLayerClient sap)
        {
            _sap = sap;
        }

        // GET: api/clientes/C78251821-6
        [HttpGet("{cardCode}")]
        public async Task<IActionResult> GetCliente(string cardCode)
        {
            try
            {
                var cli = await _sap.ObtenerClienteAsync(cardCode);
                if (cli == null)
                    return NotFound(new { message = "Cliente no encontrado en SAP." });

                return Ok(cli);
            }
            catch (Exception ex)
            {
                // Esto es lo que ves como 500 en Chrome
                return StatusCode(500, new
                {
                    message = "Error al obtener cliente desde SAP Service Layer.",
                    detail = ex.Message
                });
            }
        }
    }
}
