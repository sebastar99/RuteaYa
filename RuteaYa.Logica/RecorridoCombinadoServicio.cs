using System.Diagnostics;
using System.Globalization;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Límite explícito de consultas de ruta y de tiempo por evaluación. Reutiliza resultados de secuencias idénticas
/// y no depende del proveedor concreto: solo conoce IRutasServicio.
/// </summary>
public sealed class PresupuestoRutas(int maxSolicitudes = 80, TimeSpan? maxTiempo = null)
{
    private readonly Dictionary<string, ResultadoRutaDto> _cache = new();
    private readonly Stopwatch _reloj = Stopwatch.StartNew();
    private readonly TimeSpan _maxTiempo = maxTiempo ?? TimeSpan.FromSeconds(40);
    public int MaxSolicitudes { get; } = maxSolicitudes;
    public int Solicitudes { get; private set; }
    public bool Agotado { get; private set; }

    /// <summary>Null cuando se alcanzó el límite: el llamador debe tratar la búsqueda como incompleta, nunca inventar un resultado.</summary>
    public async Task<ResultadoRutaDto?> CalcularAsync(IRutasServicio rutas, IReadOnlyList<PuntoRutaDto> puntos, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var clave = string.Join(";", puntos.Select(p => p.Latitud.ToString("G29", CultureInfo.InvariantCulture) + "," + p.Longitud.ToString("G29", CultureInfo.InvariantCulture)));
        if (_cache.TryGetValue(clave, out var guardado)) return guardado;
        if (Solicitudes >= MaxSolicitudes || _reloj.Elapsed > _maxTiempo) { Agotado = true; return null; }
        Solicitudes++;
        var r = await rutas.CalcularAsync(puntos, ct);
        if (r == null || r.DistanciaMetros < 0 || r.DuracionSegundos < 0)
            throw new InvalidOperationException("El proveedor no devolvió métricas de ruta válidas.");
        _cache[clave] = r;
        return r;
    }
}

public interface IRecorridoCombinadoServicio
{
    /// <summary>Calcula una sola vez la ruta del viaje original y completa el contexto.</summary>
    Task<bool> PrepararBaseAsync(ContextoRecorridoDto contexto, PresupuestoRutas presupuesto, CancellationToken ct = default);

    /// <summary>
    /// Busca el mejor orden de retiros y entregas dentro del recorrido original (que conserva origen, paradas y destino en orden)
    /// y verifica desvío total y horarios. El desvío siempre se compara contra el viaje original, nunca contra otra combinación.
    /// </summary>
    Task<ResultadoRecorridoDto> EvaluarAsync(ContextoRecorridoDto contexto, IReadOnlyList<PaqueteRecorridoDto> paquetes,
        PresupuestoRutas presupuesto, CancellationToken ct = default);

    /// <summary>Sin consultar rutas: verdadero solo si ningún orden puede cumplir el desvío, ni siquiera en línea recta.</summary>
    bool ImposibleProbado(ContextoRecorridoDto contexto, IReadOnlyList<PaqueteRecorridoDto> paquetes);
}

public sealed class RecorridoCombinadoServicio : IRecorridoCombinadoServicio
{
    private const int MaxOrdenes = 20000, MaxNodos = 400000, MaxOrdenesEvaluados = 30;
    // Una ruta por carretera nunca es más corta que la línea recta; el margen cubre el ajuste de puntos a la calle.
    private const double FactorCota = 0.9;
    private readonly IRutasServicio _rutas;

    public RecorridoCombinadoServicio(IRutasServicio rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        _rutas = rutas;
    }

    public async Task<bool> PrepararBaseAsync(ContextoRecorridoDto c, PresupuestoRutas presupuesto, CancellationToken ct = default)
    {
        if (c.Base.Count < 2) throw new ArgumentException("El recorrido base debe incluir origen y destino.");
        var r = await presupuesto.CalcularAsync(_rutas, c.Base, ct);
        if (r == null) return false;
        c.DistanciaBaseMetros = r.DistanciaMetros; c.DuracionBaseSegundos = r.DuracionSegundos;
        return true;
    }

    public bool ImposibleProbado(ContextoRecorridoDto c, IReadOnlyList<PaqueteRecorridoDto> paquetes)
    {
        var ordenes = Enumerar(c, paquetes, out var truncado);
        return !truncado && ordenes.Count == 0;
    }

    public async Task<ResultadoRecorridoDto> EvaluarAsync(ContextoRecorridoDto c, IReadOnlyList<PaqueteRecorridoDto> paquetes,
        PresupuestoRutas presupuesto, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(c); ArgumentNullException.ThrowIfNull(paquetes);
        if (c.DistanciaBaseMetros <= 0 && c.DuracionBaseSegundos <= 0) throw new InvalidOperationException("Falta calcular la ruta del viaje original.");
        var limite = c.DesvioMaxKm * 1000m;
        var resultado = new ResultadoRecorridoDto { DistanciaOriginalMetros = c.DistanciaBaseMetros, DuracionOriginalSegundos = c.DuracionBaseSegundos };
        var ordenes = Enumerar(c, paquetes, out var truncado);
        resultado.Completo = !truncado;
        if (ordenes.Count == 0 && !truncado)
        {
            resultado.Estado = EstadoCompatibilidad.Incompatible; resultado.ImposibleProbado = true;
            resultado.Motivos = new[] { MotivoCompatibilidad.DesvioExcedido };
            return resultado;
        }

        var restricciones = paquetes.Select(p => new CompatibilidadServicio.RestriccionesPaquete(p.DisponibleDesde, p.FechaLimiteEntrega)).ToArray();
        (long Distancia, long Duracion, int[] Camino, CompatibilidadServicio.ResultadoHorario Horario)? mejor = null, provisional = null;
        var rechazos = new List<MotivoCompatibilidad>(); var superoDesvio = false; var evaluadas = 0; var evaluadasOrdenes = 0;
        foreach (var (h, camino) in ordenes.OrderBy(o => o.H))
        {
            // Cota inferior demostrada: si ni la línea recta cabe en el desvío, tampoco los órdenes con mayor longitud.
            if (h * FactorCota - c.DistanciaBaseMetros > (double)limite) break;
            if (mejor.HasValue && h * FactorCota > mejor.Value.Distancia) break;
            if (evaluadasOrdenes >= MaxOrdenesEvaluados) { resultado.Completo = false; break; }
            evaluadasOrdenes++;
            var ruta = await presupuesto.CalcularAsync(_rutas, Puntos(c, paquetes, camino), ct);
            if (ruta == null) { resultado.Completo = false; break; }
            evaluadas++;
            if (ruta.DistanciaMetros - c.DistanciaBaseMetros > limite) { superoDesvio = true; continue; }
            var horario = CompatibilidadServicio.EvaluarHorarios(c.Salida, c.LlegadaMaxima, restricciones, ruta.DuracionSegundos);
            var item = (ruta.DistanciaMetros, ruta.DuracionSegundos, camino, horario);
            if (horario.Estado == EstadoCompatibilidad.Compatible)
            {
                if (!mejor.HasValue || (item.DistanciaMetros, item.DuracionSegundos).CompareTo((mejor.Value.Distancia, mejor.Value.Duracion)) < 0) mejor = item;
            }
            else if (horario.Estado == EstadoCompatibilidad.Indeterminado)
            {
                if (!provisional.HasValue || (item.DistanciaMetros, item.DuracionSegundos).CompareTo((provisional.Value.Distancia, provisional.Value.Duracion)) < 0) provisional = item;
            }
            else foreach (var m in horario.Motivos) if (!rechazos.Contains(m)) rechazos.Add(m);
        }

        var elegido = mejor ?? provisional;
        if (elegido.HasValue)
        {
            resultado.DistanciaCombinadaMetros = elegido.Value.Distancia; resultado.DuracionCombinadaSegundos = elegido.Value.Duracion;
            resultado.Orden = Describir(c, paquetes, elegido.Value.Camino); resultado.FinEstimado = elegido.Value.Horario.FinEstimado;
            resultado.Estado = elegido.Value.Horario.Estado;
            var motivos = new List<MotivoCompatibilidad> { MotivoCompatibilidad.DesvioAprobado };
            motivos.AddRange(elegido.Value.Horario.Motivos); resultado.Motivos = motivos;
        }
        else if (evaluadas == 0 && !resultado.Completo) resultado.Estado = EstadoCompatibilidad.Indeterminado;
        else if (rechazos.Count > 0) { resultado.Estado = EstadoCompatibilidad.Incompatible; resultado.Motivos = rechazos; }
        else { resultado.Estado = resultado.Completo || superoDesvio ? EstadoCompatibilidad.Incompatible : EstadoCompatibilidad.Indeterminado;
            resultado.Motivos = new[] { MotivoCompatibilidad.DesvioExcedido }; }
        return resultado;
    }

    private static double Metros(PuntoRutaDto a, PuntoRutaDto b)
    {
        const double R = 6371000; double Rad(decimal g) => (double)g * Math.PI / 180;
        var dLat = Rad(b.Latitud - a.Latitud); var dLng = Rad(b.Longitud - a.Longitud);
        var x = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(a.Latitud)) * Math.Cos(Rad(b.Latitud)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(x)));
    }

    // Nodos: 0..n = puntos originales; n+1+2i = retiro del paquete i; n+2+2i = entrega del paquete i.
    private static List<(double H, int[] Camino)> Enumerar(ContextoRecorridoDto c, IReadOnlyList<PaqueteRecorridoDto> paquetes, out bool truncado)
    {
        var n = c.Base.Count - 1; var m = paquetes.Count; var limite = (double)(c.DesvioMaxKm * 1000m);
        PuntoRutaDto Nodo(int k) => k <= n ? c.Base[k] : (k - n - 1) % 2 == 0 ? paquetes[(k - n - 1) / 2].Retiro : paquetes[(k - n - 1) / 2].Entrega;
        var sufijo = new double[n + 2];
        for (var i = n - 1; i >= 0; i--) sufijo[i] = sufijo[i + 1] + Metros(c.Base[i], c.Base[i + 1]);
        var estado = new int[m]; var camino = new List<int> { 0 };
        var salida = new List<(double, int[])>(); var nodos = 0; var corte = false;

        void Dfs(int baseIdx, int entregados, double acumulado)
        {
            if (corte) return;
            if (++nodos > MaxNodos || salida.Count >= MaxOrdenes) { corte = true; return; }
            var actual = Nodo(camino[^1]);
            if (baseIdx == n) { salida.Add((acumulado, camino.ToArray())); return; }
            var cota = acumulado + Metros(actual, c.Base[baseIdx + 1]) + sufijo[baseIdx + 1];
            if (cota * FactorCota - c.DistanciaBaseMetros > limite) return;
            // Avanzar al siguiente punto original; el destino solo se alcanza con todas las entregas hechas.
            if (baseIdx + 1 < n || entregados == m)
            {
                camino.Add(baseIdx + 1);
                Dfs(baseIdx + 1, entregados, acumulado + Metros(actual, c.Base[baseIdx + 1]));
                camino.RemoveAt(camino.Count - 1);
            }
            for (var i = 0; i < m && !corte; i++)
            {
                if (estado[i] == 2) continue;
                var nodo = n + 1 + 2 * i + estado[i];
                var paso = Metros(actual, Nodo(nodo));
                estado[i]++; camino.Add(nodo);
                Dfs(baseIdx, entregados + (estado[i] == 2 ? 1 : 0), acumulado + paso);
                camino.RemoveAt(camino.Count - 1); estado[i]--;
            }
        }
        Dfs(0, 0, 0);
        truncado = corte;
        return salida;
    }

    private static List<PuntoRutaDto> Puntos(ContextoRecorridoDto c, IReadOnlyList<PaqueteRecorridoDto> p, int[] camino)
    {
        var n = c.Base.Count - 1;
        return camino.Select(k => k <= n ? c.Base[k] : (k - n - 1) % 2 == 0 ? p[(k - n - 1) / 2].Retiro : p[(k - n - 1) / 2].Entrega)
            .Select(x => new PuntoRutaDto { Latitud = x.Latitud, Longitud = x.Longitud }).ToList();
    }

    private static List<ParadaRecorridoDto> Describir(ContextoRecorridoDto c, IReadOnlyList<PaqueteRecorridoDto> p, int[] camino)
    {
        var n = c.Base.Count - 1;
        return camino.Select(k =>
        {
            if (k <= n) return new ParadaRecorridoDto { Tipo = k == 0 ? "Origen" : k == n ? "Destino" : "Parada",
                Descripcion = k < c.EtiquetasBase.Count ? c.EtiquetasBase[k] : "" };
            var paquete = p[(k - n - 1) / 2]; var retiro = (k - n - 1) % 2 == 0;
            return new ParadaRecorridoDto { Tipo = retiro ? "Retiro" : "Entrega", EnvioId = paquete.EnvioId,
                Descripcion = paquete.Descripcion + " - " + (retiro ? paquete.OrigenDir : paquete.DestinoDir) };
        }).ToList();
    }
}
