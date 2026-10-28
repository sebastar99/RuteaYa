
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("RuteaYa")
    ?? throw new InvalidOperationException(
        "No se configuró la conexión RuteaYa.");

builder.Services.AddDbContext<RuteaYaDbContext>(options =>
    options.UseSqlServer(connectionString));

// Usamos el hasher para proteger las contraseñas.
// No usamos ASP.NET Identity ni sus tablas.
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

builder.Services.AddScoped<IUsuariosLogica, UsuariosLogica>();
builder.Services.AddScoped<IViajesLogica, ViajesLogica>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
app.UseAuthorization();
app.MapControllers();

app.Run();
