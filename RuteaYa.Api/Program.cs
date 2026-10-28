
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RuteaYa.Entidades;
using RuteaYa.Logica;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("RuteaYa")
    ?? throw new InvalidOperationException(
        "No se configuró la conexión RuteaYa.");

builder.Services.AddDbContext<RuteaYaDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IPasswordHasher<Usuario>,
    PasswordHasher<Usuario>>();

builder.Services.AddScoped<IUsuariosLogica, UsuariosLogica>();

var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "No se configuró Jwt:Issuer.");

var audience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "No se configuró Jwt:Audience.");

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "No se configuró Jwt:Key.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "La clave JWT debe tener al menos 32 bytes.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();

        document.Components.SecuritySchemes ??=
            new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] =
            new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Ingresá el token JWT para autenticar la solicitud."
            };

        document.Security =
        [
            new Microsoft.OpenApi.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.OpenApiSecuritySchemeReference(
                        "Bearer",
                        document),
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
