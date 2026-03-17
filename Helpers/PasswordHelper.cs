using System.Security.Cryptography;
using System.Text;

namespace SpartanVentasApi.Helpers
{
    public static class PasswordHelper
    {
        /// <summary>
        /// Genera hash SHA256 compatible con SQL Server:
        /// HASHBYTES('SHA2_256', NVARCHAR) -> UTF-16 + HEX MAYÚSCULA
        /// </summary>
        public static string Sha256Hex(string input)
        {
            using var sha = SHA256.Create();

            // IMPORTANTE: Unicode = UTF-16 (igual que NVARCHAR en SQL)
            byte[] bytes = Encoding.UTF8.GetBytes(input);

            byte[] hash = sha.ComputeHash(bytes);

            // Hexadecimal en MAYÚSCULAS (igual que SQL)
            var sb = new StringBuilder(64);
            foreach (var b in hash)
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }
    }
}

