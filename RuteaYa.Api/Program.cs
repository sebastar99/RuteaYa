
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RuteaYa.Api.Services;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// SQL Server
var connectionString = builder.Configuration
    .GetConnectionString("RuteaYa")
    ?? throw new InvalidOperationException(
        "No se configuró la conexión RuteaYa.");

builder.Services.AddDbContext<RuteaYaDbContext>(options =>
    options.UseSqlServer(connectionString));

// Servicios
builder.Services.AddScoped<IPasswordHasher<Usuario>,
    PasswordHasher<Usuario>>();

builder.Services.AddScoped<IUsuariosLogica, UsuariosLogica>();
builder.Services.AddScoped<IViajesLogica, ViajesLogica>();
// Validación y consumo sobre EF: no requiere un cliente ni una clave HERE en la API.
builder.Services.AddScoped<IUbicacionesConfirmadasServicio, UbicacionesConfirmadasServicio>();
builder.Services.AddScoped<ITokenServicio, TokenServicio>();

// Optimizador y asignaciones: la misma lógica que el MVC. La clave HERE queda en configuración privada.
builder.Services.Configure<HereRoutingOpciones>(builder.Configuration.GetSection("HereRouting"));
builder.Services.AddHttpClient<RutasHereServicio>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IRutasServicio>(sp => new RutasConsultaServicio(sp.GetRequiredService<RutasHereServicio>()));
builder.Services.AddScoped<IRecorridoCombinadoServicio, RecorridoCombinadoServicio>();
builder.Services.AddScoped<IOptimizadorServicio, OptimizadorServicio>();
builder.Services.AddScoped<IDatosConfirmadosOportunidadServicio, DatosConfirmadosOportunidadPersistidosServicio>();
builder.Services.AddScoped<IDesviosServicio, DesviosServicio>();
builder.Services.AddScoped<ICompatibilidadServicio, CompatibilidadServicio>();
builder.Services.AddScoped<IEvaluacionOportunidadServicio>(sp => new EvaluacionOportunidadServicio(
    sp.GetRequiredService<IDatosConfirmadosOportunidadServicio>(),
    sp.GetRequiredService<ICompatibilidadServicio>(),
    sp.GetService<IRecorridoCombinadoServicio>()));
builder.Services.AddScoped<IOportunidadesLogica, OportunidadesLogica>();
builder.Services.AddScoped<EnviosLogica>(sp => new EnviosLogica(
    sp.GetRequiredService<RuteaYaDbContext>(), null!,
    sp.GetService<IUbicacionesConfirmadasServicio>(),
    sp.GetService<IOportunidadesLogica>(),
    sp.GetService<IOptimizadorServicio>()));

// JWT
var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Falta Jwt:Issuer.");

var audience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Falta Jwt:Audience.");

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Configurá Jwt:Key mediante User Secrets o una variable de entorno.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "La clave JWT debe tener al menos 32 bytes.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = true;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey)),

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();

        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Ingresá tu token JWT."
            };

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference(
                        "Bearer", document),
                    []
                }
            }
        ];

        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "RuteaYa API v1");

        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
