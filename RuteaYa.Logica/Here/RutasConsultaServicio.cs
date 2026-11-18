using System.Globalization;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Services;

/// <summary>Reutilización solo durante una solicitud MVC; no conserva rutas entre usuarios/solicitudes.</summary>
public sealed class RutasConsultaServicio(IRutasServicio proveedor) : IRutasServicio
{
    private readonly Dictionary<string, Task<ResultadoRutaDto>> _rutas = new();
    public async Task<ResultadoRutaDto> CalcularAsync(IReadOnlyList<PuntoRutaDto> puntos, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(puntos);
        if (puntos.Any(p => p == null)) throw new ArgumentException("Falta un punto de ruta.");
        var copia = puntos.Select(p => new PuntoRutaDto { Latitud = p.Latitud, Longitud = p.Longitud }).ToArray();
        var clave = string.Join(";", copia.Select(p => p.Latitud.ToString("G29", CultureInfo.InvariantCulture) + "," + p.Longitud.ToString("G29", CultureInfo.InvariantCulture)));
        Task<ResultadoRutaDto> consulta;
        lock (_rutas) {
            if (!_rutas.TryGetValue(clave, out consulta!)) { consulta = proveedor.CalcularAsync(copia, cancellationToken); _rutas.Add(clave, consulta); }
        }
        try {
            var r = await consulta;
            cancellationToken.ThrowIfCancellationRequested();
            if (r == null || r.DistanciaMetros < 0 || r.DuracionSegundos < 0)
                throw new InvalidOperationException("El proveedor no devolvió métricas de ruta válidas.");
            return new() { DistanciaMetros = r.DistanciaMetros, DuracionSegundos = r.DuracionSegundos };
        }
        catch { lock (_rutas) { if (_rutas.GetValueOrDefault(clave) == consulta) _rutas.Remove(clave); } throw; }
    }
}
