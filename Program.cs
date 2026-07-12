using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================================
// CONFIGURACIÓN GENERAL
// ==========================================================

Console.WriteLine(
    $"Entorno actual: {builder.Environment.EnvironmentName}");

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile(
        "appsettings.json",
        optional: false,
        reloadOnChange: true)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true)
    .AddEnvironmentVariables();

// ==========================================================
// CONTROLLERS Y SWAGGER
// ==========================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton<
    SpartanVentasApi.Services.SapServiceLayerClient>();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SpartanVentasApi",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description =
            "Token JWT en Authorization. Ejemplo: Bearer {token}",
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

// ==========================================================
// JWT
// ==========================================================

var jwt = builder.Configuration.GetSection("Jwt");

if (string.IsNullOrWhiteSpace(jwt["Key"]))
{
    throw new InvalidOperationException(
        "Jwt:Key no está configurado en appsettings.json");
}

// ==========================================================
// SSO: CARPETA COMPARTIDA DE CLAVES
// ==========================================================

// En localhost, ambos proyectos usarán esta carpeta.
//
// En producción, mantendremos la misma ruta en el servidor.
// La identidad del Application Pool de ambos módulos deberá
// tener permisos de lectura y escritura sobre esta carpeta.

var sharedKeysPath = builder.Configuration[
    "SharedAuthentication:KeysPath"];

if (string.IsNullOrWhiteSpace(sharedKeysPath))
{
    sharedKeysPath = Path.Combine(
        builder.Environment.ContentRootPath,
        "SharedAuthKeys");
}

Directory.CreateDirectory(sharedKeysPath);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(sharedKeysPath))
    .SetApplicationName("SpartanCloud.SharedAuthentication");

// ==========================================================
// AUTENTICACIÓN HÍBRIDA: JWT + COOKIE SSO
// ==========================================================

const string smartScheme = "SpartanCloudSmartScheme";
const string sharedCookieScheme = "SpartanCloud.SharedCookie";

builder.Services
    .AddAuthentication(options =>
    {
        // Decide automáticamente entre JWT y cookie.
        options.DefaultScheme = smartScheme;
        options.DefaultAuthenticateScheme = smartScheme;
        options.DefaultChallengeScheme = smartScheme;
    })

    // Si la solicitud trae "Authorization: Bearer...",
    // utiliza JWT. En caso contrario, utiliza la cookie SSO.
    .AddPolicyScheme(
        smartScheme,
        smartScheme,
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authorization =
                    context.Request.Headers.Authorization.ToString();

                if (authorization.StartsWith(
                    "Bearer ",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                return sharedCookieScheme;
            };
        })

    // JWT ACTUAL: conserva la configuración existente.
    .AddJwtBearer(
        JwtBearerDefaults.AuthenticationScheme,
        options =>
        {
            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = jwt["Issuer"],
                    ValidAudience = jwt["Audience"],

                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwt["Key"]!)),

                    NameClaimType = "usuario",
                    RoleClaimType = ClaimTypes.Role
                };
        })

    // COOKIE COMPARTIDA DEL SSO.
    .AddCookie(
        sharedCookieScheme,
        options =>
        {
            // Debe ser idéntico en ConsultaSN.
            options.Cookie.Name = "SpartanCloud.Auth";

            // Compartida por todas las rutas del dominio.
            options.Cookie.Path = "/";

            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy =
                CookieSecurePolicy.Always;

            options.Cookie.SameSite =
                SameSiteMode.Lax;

            options.SlidingExpiration = true;
            options.ExpireTimeSpan =
                TimeSpan.FromHours(8);

            // Por ahora conservamos el login actual.
            options.LoginPath = "/login.html";
            options.AccessDeniedPath = "/acceso-denegado.html";

            // Evita que una API redirija a una página HTML.
            // Para llamadas /api devuelve 401 o 403.
            options.Events =
                new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        if (context.Request.Path
                            .StartsWithSegments("/api"))
                        {
                            context.Response.StatusCode =
                                StatusCodes.Status401Unauthorized;

                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(
                            context.RedirectUri);

                        return Task.CompletedTask;
                    },

                    OnRedirectToAccessDenied = context =>
                    {
                        if (context.Request.Path
                            .StartsWithSegments("/api"))
                        {
                            context.Response.StatusCode =
                                StatusCodes.Status403Forbidden;

                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(
                            context.RedirectUri);

                        return Task.CompletedTask;
                    }
                };
        });

builder.Services.AddAuthorization();

// ==========================================================
// PIPELINE HTTP
// ==========================================================

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();