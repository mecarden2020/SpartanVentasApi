namespace SpartanVentasApi.Models
{
    public class VentaClienteDetalleDto
    {
        public string CardCode { get; set; } = "";
        public string CardName { get; set; } = "";
        public decimal NetoCliente { get; set; }
        public string DetalleJson { get; set; } = "[]";
    }
}
