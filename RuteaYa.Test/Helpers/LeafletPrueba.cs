using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace RuteaYa.Test.Helpers;

// La suite habitual es offline. La comprobación explícita con Leaflet real descarga
// únicamente sus recursos públicos; HERE y la base siguen simulados/SQLite en memoria.
internal static class LeafletPrueba
{
    public static bool Real => Environment.GetEnvironmentVariable("RUTEAYA_LEAFLET_REAL") == "1";
    public static string Reescribir(string html) => Real
        ? html.Replace("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js", "/prueba/leaflet.js")
            .Replace("https://unpkg.com/leaflet@1.9.4/dist/leaflet.css", "/prueba/leaflet.css")
            .Replace("</body>", "<script>window.pruebaLeafletReal=true;</script></body>")
        : System.Text.RegularExpressions.Regex.Replace(html, "https://unpkg.com/[^\"]+", "/prueba/cdn-ausente");
    public static void Mapear(WebApplication app)
    {
        if (!Real) return;
        var cssRuta = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Archivo())!, "..", "..", "RuteaYa", "wwwroot", "css", "recorridos.css"));
        app.MapGet("/css/recorridos.css", () => Results.Text(File.ReadAllText(cssRuta), "text/css"));
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var js = http.GetStringAsync("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js");
        var css = http.GetStringAsync("https://unpkg.com/leaflet@1.9.4/dist/leaflet.css");
        app.MapGet("/prueba/leaflet.js", async () => Results.Text(await js, "application/javascript"));
        app.MapGet("/prueba/leaflet.css", async () => Results.Text(await css, "text/css"));
        app.Lifetime.ApplicationStopped.Register(http.Dispose);
    }
    private static string Archivo([System.Runtime.CompilerServices.CallerFilePath] string archivo = "") => archivo;
}
