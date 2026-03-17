using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Security.Claims;

namespace SpartanVentasApi.Controllers
{
    [ApiController]
    [Route("api/wall")]
    [Authorize]
    public class WallController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        private readonly IWebHostEnvironment _env;

        public WallController(IConfiguration cfg, IWebHostEnvironment env)
        {
            _cfg = cfg;
            _env = env;
        }

        private string ConnStr =>
            _cfg.GetConnectionString("SAP")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:SAP");

        // =========================
        // Identity helpers
        // =========================
        private string GetLogin()
        {
            return
                User.FindFirst("login")?.Value
                ?? User.FindFirst("username")?.Value
                ?? User.FindFirst(ClaimTypes.Name)?.Value
                ?? User.Identity?.Name
                ?? User.FindFirst("unique_name")?.Value
                ?? User.FindFirst("name")?.Value
                ?? User.FindFirst("sub")?.Value
                ?? "";
        }

        private string GetAlias()
        {
            return
                User.FindFirst("username")?.Value
                ?? User.FindFirst("alias")?.Value
                ?? User.FindFirst("name")?.Value
                ?? "";
        }

        private string GetRole()
        {
            return
                User.FindFirst(ClaimTypes.Role)?.Value
                ?? User.FindFirst("role")?.Value
                ?? "";
        }

        private bool IsAdmin()
        {
            var r = (GetRole() ?? "").Trim();
            return string.Equals(r, "ADMIN", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAdminVendedores()
        {
            var r = (GetRole() ?? "").Trim();
            return string.Equals(r, "ADMIN_VENDEDORES", StringComparison.OrdinalIgnoreCase);
        }

        // =========================
        // DTOs / VMs
        // =========================
        public record CreatePostDto(string Category, string Text);

        public class WallPostVm
        {
            public int Id { get; set; }
            public string Category { get; set; } = "";
            public string Text { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public string CreatedByLogin { get; set; } = "";
            public string CreatedByAlias { get; set; } = "";
            public bool IsPinned { get; set; }
            public bool IsDeleted { get; set; }
            public string? ImageUrl { get; set; }
            public int LikeCount { get; set; }
            public bool LikedByMe { get; set; }
        }

        // ============================================================
        // GET /api/wall/posts?page=1&pageSize=10&category=...
        // ============================================================
        [HttpGet("posts")]
        public async Task<IActionResult> GetPosts([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? category = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 50) pageSize = 50;

            var me = (GetLogin() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(me)) return Unauthorized("No se pudo obtener el usuario.");

            var offset = (page - 1) * pageSize;

            const string sqlCount = @"
SELECT COUNT(1)
FROM dbo.WallPost P
WHERE P.IsDeleted = 0
  AND (@cat IS NULL OR P.Category = @cat);";

            const string sql = @"
SELECT
    P.Id,
    P.Category,
    P.[Text]      AS [Text],
    P.CreatedAt,
    P.CreatedByLogin,
    P.CreatedByAlias,
    P.IsPinned,
    P.IsDeleted,
    P.ImageUrl,
    (SELECT COUNT(1) FROM dbo.WallPostLike L WHERE L.PostId = P.Id) AS LikeCount,
    CAST(CASE WHEN EXISTS (
        SELECT 1 FROM dbo.WallPostLike L2 WHERE L2.PostId = P.Id AND L2.Login = @me
    ) THEN 1 ELSE 0 END AS bit) AS LikedByMe
FROM dbo.WallPost P
WHERE P.IsDeleted = 0
  AND (@cat IS NULL OR P.Category = @cat)
ORDER BY P.IsPinned DESC, P.CreatedAt DESC
OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY;";

            await using var cn = new SqlConnection(ConnStr);
            var total = await cn.ExecuteScalarAsync<int>(sqlCount, new { cat = category });
            var items = (await cn.QueryAsync<WallPostVm>(sql, new { cat = category, me, offset, take = pageSize })).ToList();

            return Ok(new { page, pageSize, total, items });
        }

        // ============================================================
        // POST /api/wall/posts
        // ============================================================
        [HttpPost("posts")]
        public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
        {
            var login = (GetLogin() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized("No se pudo obtener el usuario.");

            var alias = (GetAlias() ?? "").Trim();
            var cat = (dto.Category ?? "").Trim();
            var text = (dto.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(cat)) return BadRequest("Categoría requerida.");
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return BadRequest("Texto demasiado corto.");

            const string sql = @"
INSERT INTO dbo.WallPost(Category, [Text], CreatedAt, CreatedByLogin, CreatedByAlias, IsPinned, IsDeleted, ImageUrl)
OUTPUT INSERTED.Id
VALUES(@cat, @text, SYSUTCDATETIME(), @login, @alias, 0, 0, NULL);";

            await using var cn = new SqlConnection(ConnStr);
            var id = await cn.ExecuteScalarAsync<int>(sql, new { cat, text, login, alias });
            return Ok(new { id });
        }

        // ============================================================
        // POST /api/wall/posts/{id}/like
        // ============================================================
        [HttpPost("posts/{id:int}/like")]
        public async Task<IActionResult> ToggleLike([FromRoute] int id)
        {
            var login = (GetLogin() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized("No se pudo obtener el usuario.");

            const string sqlCheckPost = @"SELECT COUNT(1) FROM dbo.WallPost WHERE Id=@id AND IsDeleted=0;";

            const string sqlToggle = @"
IF EXISTS (SELECT 1 FROM dbo.WallPostLike WHERE PostId=@id AND Login=@login)
BEGIN
    DELETE FROM dbo.WallPostLike WHERE PostId=@id AND Login=@login;
    SELECT CAST(0 AS bit) AS Liked;
END
ELSE
BEGIN
    INSERT INTO dbo.WallPostLike(PostId, Login) VALUES(@id, @login);
    SELECT CAST(1 AS bit) AS Liked;
END";

            const string sqlCountLikes = @"SELECT COUNT(1) FROM dbo.WallPostLike WHERE PostId=@id;";

            await using var cn = new SqlConnection(ConnStr);

            var exists = await cn.ExecuteScalarAsync<int>(sqlCheckPost, new { id });
            if (exists == 0) return NotFound("Post no existe.");

            await cn.OpenAsync();
            using var tx = cn.BeginTransaction();

            try
            {
                var liked = await cn.ExecuteScalarAsync<bool>(sqlToggle, new { id, login }, tx);
                var likeCount = await cn.ExecuteScalarAsync<int>(sqlCountLikes, new { id }, tx);

                tx.Commit();
                return Ok(new { postId = id, liked, likeCount });
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // ============================================================
        // POST /api/wall/posts/{id}/pin  (ADMIN_VENDEDORES o ADMIN)
        // ============================================================
        [HttpPost("posts/{id:int}/pin")]
        public async Task<IActionResult> TogglePin([FromRoute] int id)
        {
            if (!IsAdmin() && !IsAdminVendedores()) return Forbid();

            const string sqlCheckPost = @"SELECT COUNT(1) FROM dbo.WallPost WHERE Id=@id AND IsDeleted=0;";

            const string sqlTogglePin = @"
UPDATE dbo.WallPost
SET IsPinned = CASE WHEN IsPinned = 1 THEN 0 ELSE 1 END
WHERE Id = @id AND IsDeleted = 0;

SELECT CAST(IsPinned AS bit)
FROM dbo.WallPost
WHERE Id = @id;";

            await using var cn = new SqlConnection(ConnStr);

            var exists = await cn.ExecuteScalarAsync<int>(sqlCheckPost, new { id });
            if (exists == 0) return NotFound("Post no existe.");

            var pinned = await cn.ExecuteScalarAsync<bool>(sqlTogglePin, new { id });
            return Ok(new { postId = id, pinned });
        }

        // ============================================================
        // DELETE /api/wall/posts/{id}  (dueño o ADMIN_VENDEDORES) + borrar archivo
        // ============================================================
        [HttpDelete("posts/{id:int}")]
        public async Task<IActionResult> DeletePost([FromRoute] int id)
        {
            var login = (GetLogin() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized();

            const string sqlGet = @"
SELECT CreatedByLogin, ImageUrl
FROM dbo.WallPost
WHERE Id = @id AND IsDeleted = 0;";

            const string sqlDel = @"
UPDATE dbo.WallPost
SET IsDeleted = 1
WHERE Id = @id AND IsDeleted = 0;";

            await using var cn = new SqlConnection(ConnStr);
            var row = await cn.QueryFirstOrDefaultAsync(sqlGet, new { id });
            if (row == null) return NotFound("Post no existe.");

            string owner = (string)(row.CreatedByLogin ?? "");
            string? imageUrl = row.ImageUrl as string;

            // Dueño o ADMIN_VENDEDORES (o ADMIN)
            if (!string.Equals(owner, login, StringComparison.OrdinalIgnoreCase)
                && !IsAdminVendedores()
                && !IsAdmin())
                return Forbid();

            var affected = await cn.ExecuteAsync(sqlDel, new { id });
            if (affected == 0) return NotFound("Post no existe.");

            // borrar archivo físico si aplica
            TryDeleteWallFile(imageUrl);

            return Ok(new { ok = true });
        }

        private void TryDeleteWallFile(string? imageUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imageUrl)) return;

                // solo permitimos borrar dentro de /uploads/wall/
                if (!imageUrl.StartsWith("/uploads/wall/", StringComparison.OrdinalIgnoreCase))
                    return;

                var rel = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar); // uploads\wall\...
                var abs = Path.Combine(_env.WebRootPath, rel);

                if (System.IO.File.Exists(abs))
                    System.IO.File.Delete(abs);
            }
            catch
            {
                // opcional: log
            }
        }

        // ============================================================
        // POST /api/wall/posts/{id}/image
        // ============================================================
        [HttpPost("posts/{id:int}/image")]
        [RequestSizeLimit(8_000_000)]
        public async Task<IActionResult> UploadPostImage([FromRoute] int id, IFormFile file)
        {
            var login = (GetLogin() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(login)) return Unauthorized("No se pudo obtener el usuario.");

            if (file == null || file.Length <= 0) return BadRequest("Archivo vacío.");
            if (file.Length > 8_000_000) return BadRequest("Imagen demasiado grande (máx 8MB).");

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
            if (!allowed.Contains(ext)) return BadRequest("Formato no permitido. Usa JPG/PNG/WEBP.");

            const string sqlOwner = @"
SELECT CreatedByLogin
FROM dbo.WallPost
WHERE Id=@id AND IsDeleted=0;";

            await using var cn = new SqlConnection(ConnStr);
            var owner = await cn.ExecuteScalarAsync<string?>(sqlOwner, new { id });

            if (string.IsNullOrWhiteSpace(owner)) return NotFound("Post no existe.");
            if (!IsAdmin() && !IsAdminVendedores() && !string.Equals(owner, login, StringComparison.OrdinalIgnoreCase))
                return Forbid();

            var absDir = Path.Combine(_env.WebRootPath, "uploads", "wall");
            Directory.CreateDirectory(absDir);

            var fname = $"{id}_{Guid.NewGuid():N}{ext}";
            var absPath = Path.Combine(absDir, fname);

            await using (var fs = System.IO.File.Create(absPath))
            {
                await file.CopyToAsync(fs);
            }

            var imageUrl = $"/uploads/wall/{fname}";

            const string sqlUpdate = @"
UPDATE dbo.WallPost
SET ImageUrl = @imageUrl
WHERE Id = @id AND IsDeleted=0;";

            await cn.ExecuteAsync(sqlUpdate, new { id, imageUrl });

            return Ok(new { postId = id, imageUrl });
        }
    }
}
