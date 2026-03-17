using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 🔍 Mostrar en consola qué entorno se está usando
Console.WriteLine($"Entorno actual: {builder.Environment.EnvironmentName}");

// 🔧 Cargar configuración por entorno
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 🟦🟦🟦 AQUI REGISTRAMOS EL SERVICIO SERVICE LAYER 🟦🟦🟦
builder.Services.AddSingleton<SpartanVentasApi.Services.SapServiceLayerClient>();
// 🟦🟦🟦 FIN DE LA SECCIÓN AGREGADA 🟦🟦🟦

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SpartanVentasApi",
        Version = "v1"
    });

    // 🔐 Definición para mostrar el botón Authorize (Bearer)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Token JWT en el header Authorization. Ejemplo: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 🔐 Configuración JWT
var jwt = builder.Configuration.GetSection("Jwt");

if (string.IsNullOrWhiteSpace(jwt["Key"]))
{
    throw new InvalidOperationException("Jwt:Key no está configurado en appsettings.json");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"]!)),

            // ✅ ESTE ES EL CAMBIO IMPORTANTE:
            // Define qué claim se usará para User.Identity.Name
            // Opción más común: "unique_name"
            // Si tu token usa ClaimTypes.Name, puedes poner: ClaimTypes.Name
            NameClaimType = "usuario",
            RoleClaimType = ClaimTypes.Role   // 👈 importante para [Authorize(Roles="...")]
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Swagger solo en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();      // para index.html
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
