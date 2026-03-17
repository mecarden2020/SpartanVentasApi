
using System.Collections.Generic;

namespace SpartanVentasApi.Models
{
    public class UserInfo
    {
        public int? SlpCode { get; set; }          // o int, según lo necesites

        // cadenas inicializadas para evitar el warning CS8618
        public string Username { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        // NUEVO: lista de permisos
        public List<string> Permisos { get; set; } = new List<string>();
    }
}

