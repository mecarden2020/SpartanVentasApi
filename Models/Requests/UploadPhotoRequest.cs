using Microsoft.AspNetCore.Http;

namespace SpartanVentasApi.Models.Requests
{
    public class UploadPhotoRequest
    {
        public IFormFile File { get; set; } = default!;
    }
}

