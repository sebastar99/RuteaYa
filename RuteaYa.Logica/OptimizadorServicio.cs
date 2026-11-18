using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IOptimizadorServicio
{
    /// <summary>Viaje o salida inexistente o ajena devuelve null, sin distinguirlos. Nunca modifica datos ni reserva envíos.</summary>
    Task<ResultadoOptimizacionDto?> OptimizarAsync(int conductorId, int viajeId, int salidaId, CancellationToken ct = default);

    /// <summary>Reevalúa en el servidor una selección concreta antes de confirmar o reservar. Null si viaje/salida no verificables.</summary>
    Task<SeleccionEvaluadaDto?> EvaluarSeleccionAsync(int conductorId, int viajeId, int salidaId, IReadOnlyList<int> envioIds, CancellationToken ct = default);

    /// <summary>Huella del estado evaluado: salida + versiones de todos los envíos de la salida. Solo sirve para detectar cambios.</summary>
    static string Huella(int salidaId, Guid versionSalida, IEnumerable<(int Id, byte[] Version)> envios)
    {
        var datos = "salida:" + salidaId + ":" + versionSalida.ToString("N") + "|" +
            string.Join(",", envios.OrderBy(e => e.Id).Select(e => e.Id + ":" + Convert.ToHexString(e.Version)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(datos)));
    }
}

/// <summary>
/// Optimizador de oportunidades: maximiza la ganancia de una salida explorando combinaciones de
/// envíos candidatos con búsqueda completa podada (cupos, peso y cota de desvío demostrada) y evalúa
/// cada una con el recorrido combinado real. El desvío siempre se compara contra el viaje original.
/// Límites explícitos: 10 candidatos evaluados, 5 paquetes por combinación, 80 consultas de ruta, 40 s.
/// </summary>
public sealed class OptimizadorServicio : IOptimizadorServicio
{
    private const int MaxCandidatos = 10, MaxPaquetes = 5, MaxPool = 40;
    private readonly RuteaYaDbContext _db;
    private readonly IDatosConfirmadosOportunidadServicio _datos;
    private readonly IRecorridoCombinadoServicio _recorridos;

    public OptimizadorServicio(RuteaYaDbContext db, IDatosConfirmadosOportunidadServicio datos, IRecorridoCombinadoServicio recorridos)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(recorridos);
        _db = db; _datos = datos; _recorridos = recorridos;
    }

    private sealed record Contexto(ResultadoOptimizacionDto Base, ContextoRecorridoDto Recorrido, SalidaViaje Salida,
        IReadOnlyList<PaqueteComprometidoDto> Comprometidos, string? Bloqueo);

    public async Task<ResultadoOptimizacionDto?> OptimizarAsync(int conductorId, int viajeId, int salidaId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var ctx = await CargarContextoAsync(conductorId, viajeId, salidaId, ct);
        if (ctx == null) return null;
        var r = ctx.Base;
        r.CandidatosDisponibles = await _db.Envios.AsNoTracking()
            .CountAsync(e => e.Estado == EstadoEnvio.BuscandoConductor && e.UsuarioId != conductorId, ct);
        if (ctx.Bloqueo != null) { r.Bloqueo = ctx.Bloqueo; return r; }
        if (r.CandidatosDisponibles == 0) return r;
        var recorrido = await _datos.ObtenerRecorridoBaseAsync(conductorId, viajeId, ct);
        if (recorrido == null) { r.Bloqueo = "El viaje no tiene todas sus direcciones confirmadas. Actualizalas desde Editar Viaje."; return r; }
        ctx.Recorrido.Base = recorrido.Puntos; ctx.Recorrido.EtiquetasBase = recorrido.Etiquetas;
        var presupuesto = new PresupuestoRutas();
        if (!await _recorridos.PrepararBaseAsync(ctx.Recorrido, presupuesto, ct))
        { r.Bloqueo = "No se pudo calcular el recorrido del viaje."; return r; }

        var fijos = ctx.Comprometidos.Select(Paquete).ToList();
        var seleccion = await CargarCandidatosAsync(conductorId, ctx, r, fijos, ct);
        var candidatos = seleccion.Where(c => c.MotivoDescarte == null).ToList();
        r.CandidatosEvaluados = candidatos.Count;
        var descartados = seleccion.Where(c => c.MotivoDescarte != null)
            .Select(c => new DescarteDto { EnvioId = c.Envio.Id, Descripcion = c.Envio.Descripcion ?? "", Motivo = c.MotivoDescarte! }).ToList();
        if (candidatos.Count > MaxCandidatos)
        {
            r.BusquedaCompleta = false;
            descartados.AddRange(candidatos.Skip(MaxCandidatos).Select(c => new DescarteDto { EnvioId = c.Envio.Id,
                Descripcion = c.Envio.Descripcion ?? "", Motivo = "no se evaluó: se priorizaron las " + MaxCandidatos + " ofertas más altas." }));
            candidatos = candidatos.Take(MaxCandidatos).ToList();
        }

        var grupos = Subconjuntos(candidatos, r.CuposDisponibles, r.PesoDisponibleKg)
            .GroupBy(g => g.Ganancia).OrderByDescending(g => g.Key);
        CombinacionDto? recomendada = null; var alternativas = new List<CombinacionDto>(); var evaluadas = 0; var completa = true;
        foreach (var tier in grupos)
        {
            if (recomendada != null && alternativas.Count >= 3) break;
            CombinacionDto? mejorTier = null;
            foreach (var sub in tier)
            {
                if (presupuesto.Agotado) { completa = false; break; }
                var paquetes = fijos.Concat(sub.Items.Select(i => candidatos[i].Paquete)).ToList();
                var resultado = await _recorridos.EvaluarAsync(ctx.Recorrido, paquetes, presupuesto, ct);
                evaluadas++;
                if (!resultado.Completo && resultado.Estado != EstadoCompatibilidad.Compatible) completa = false;
                if (resultado.Estado != EstadoCompatibilidad.Compatible) continue;
                var comb = Combinar(sub.Items.Select(i => candidatos[i].Envio).ToList(), paquetes.Skip(fijos.Count).ToList(), resultado, ctx);
                if (mejorTier == null || (comb.DesvioMetros, comb.TiempoAdicionalSegundos).CompareTo((mejorTier.DesvioMetros, mejorTier.TiempoAdicionalSegundos)) < 0)
                    mejorTier = comb;
                if (presupuesto.Agotado) { completa = false; break; }
            }
            if (mejorTier != null) { if (recomendada == null) recomendada = mejorTier; else alternativas.Add(mejorTier); }
            if (presupuesto.Agotado) { completa = false; break; }
        }
        r.Recomendada = recomendada; r.Alternativas = alternativas; r.Descartados = descartados;
        r.CombinacionesEvaluadas = evaluadas; r.SolicitudesDeRuta = presupuesto.Solicitudes;
        if (!completa) { r.BusquedaCompleta = false; r.Aviso = "Se alcanzó el límite de cálculo: la combinación mostrada es la mejor encontrada, pero puede no ser la óptima."; }
        if (recomendada == null && r.Bloqueo == null && evaluadas > 0 && !presupuesto.Agotado)
            r.Aviso = "Ninguna combinación de los envíos disponibles cumple todas las restricciones de tu salida.";
        return r;
    }

    public async Task<SeleccionEvaluadaDto?> EvaluarSeleccionAsync(int conductorId, int viajeId, int salidaId,
        IReadOnlyList<int> envioIds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envioIds);
        if (envioIds.Count == 0 || envioIds.Count > 50 || envioIds.Any(i => i <= 0) || envioIds.Distinct().Count() != envioIds.Count)
            throw new ArgumentException("La selección de envíos no es válida.");
        var ctx = await CargarContextoAsync(conductorId, viajeId, salidaId, ct);
        if (ctx == null) return null;
        var r = new SeleccionEvaluadaDto { ViajeId = viajeId, SalidaId = salidaId, Origen = ctx.Base.Origen, Destino = ctx.Base.Destino,
            Salida = ctx.Base.Salida, CuposDisponibles = ctx.Base.CuposDisponibles, PesoDisponibleKg = ctx.Base.PesoDisponibleKg };
        var envios = await _db.Envios.AsNoTracking().Where(e => envioIds.Contains(e.Id)).ToListAsync(ct);
        if (envios.Count == envioIds.Count && envios.All(e => e.ConductorId == conductorId && e.SalidaViajeId == salidaId && e.Estado == EstadoEnvio.ConductorAsignado))
        { r.YaReservada = true; r.Motivo = "Ya reservaste esta combinación; no hace falta confirmarla de nuevo."; return r; }
        if (ctx.Bloqueo != null) { r.Motivo = ctx.Bloqueo; return r; }
        var faltan = envioIds.Except(envios.Select(e => e.Id)).ToList();
        var noDisponibles = envios.Where(e => e.Estado != EstadoEnvio.BuscandoConductor || e.ConductorId.HasValue ||
            e.SalidaViajeId.HasValue || e.UsuarioId == conductorId).Select(e => e.Id).ToList();
        if (faltan.Count > 0 || noDisponibles.Count > 0)
        {
            r.EnviosNoDisponibles = faltan.Concat(noDisponibles).ToArray();
            r.Motivo = "Alguno de los envíos seleccionados ya no está disponible.";
            return r;
        }
        if (envios.Count > ctx.Base.CuposDisponibles || envios.Sum(e => e.PesoKg) > ctx.Base.PesoDisponibleKg)
        { r.Motivo = "La selección supera los cupos o el peso disponibles en la salida."; return r; }
        var recorrido = await _datos.ObtenerRecorridoBaseAsync(conductorId, viajeId, ct);
        if (recorrido == null) { r.Motivo = "No se pudo verificar el recorrido del viaje."; return r; }
        ctx.Recorrido.Base = recorrido.Puntos; ctx.Recorrido.EtiquetasBase = recorrido.Etiquetas;
        var datos = await _datos.ObtenerLoteAsync(ViajeDto(ctx), envios.Select(e => new EnvioDto
        {
            Id = e.Id, UsuarioId = e.UsuarioId, OrigenDir = e.OrigenDir, DestinoDir = e.DestinoDir, FechaLimite = e.FechaLimite,
            PesoKg = e.PesoKg, Tamano = e.Tamano, Descripcion = e.Descripcion, Monto = e.Monto, Estado = e.Estado
        }).ToList(), ct);
        var fijos = ctx.Comprometidos.Select(Paquete).ToList();
        var nuevos = new List<PaqueteRecorridoDto>();
        foreach (var e in envios.OrderBy(e => e.Id))
        {
            if (!datos.TryGetValue(e.Id, out var d) || d?.RetiroConfirmado == null || d.EntregaConfirmada == null ||
                d.DisponibleDesde.Estado == EstadoRestriccionTemporal.Desconocida || d.FechaLimiteEntrega.Estado != EstadoRestriccionTemporal.Informada)
            { r.EnviosNoDisponibles = new[] { e.Id }; r.Motivo = "Uno de los envíos ya no puede verificarse."; return r; }
            nuevos.Add(new() { EnvioId = e.Id, Descripcion = e.Descripcion ?? "", OrigenDir = e.OrigenDir, DestinoDir = e.DestinoDir,
                Retiro = d.RetiroConfirmado, Entrega = d.EntregaConfirmada, DisponibleDesde = d.DisponibleDesde, FechaLimiteEntrega = d.FechaLimiteEntrega });
        }
        var presupuesto = new PresupuestoRutas();
        if (!await _recorridos.PrepararBaseAsync(ctx.Recorrido, presupuesto, ct)) { r.Motivo = "No se pudo calcular el recorrido del viaje."; return r; }
        var resultado = await _recorridos.EvaluarAsync(ctx.Recorrido, fijos.Concat(nuevos).ToList(), presupuesto, ct);
        if (resultado.Estado != EstadoCompatibilidad.Compatible)
        {
            r.Motivos = resultado.Motivos;
            r.Motivo = resultado.Estado == EstadoCompatibilidad.Incompatible
                ? "La combinación ya no cumple las restricciones del viaje."
                : "La combinación no puede verificarse por completo.";
            return r;
        }
        r.Combinacion = Combinar(envios, nuevos, resultado, ctx);
        r.Huella = r.Combinacion.Huella; r.Valida = true;
        return r;
    }

    private async Task<Contexto?> CargarContextoAsync(int conductorId, int viajeId, int salidaId, CancellationToken ct)
    {
        if (conductorId <= 0 || viajeId <= 0 || salidaId <= 0) throw new ArgumentException("Los identificadores deben ser positivos.");
        var s = await _db.SalidasViaje.AsNoTracking().Include(x => x.Viaje)
            .SingleOrDefaultAsync(x => x.Id == salidaId && x.ViajeId == viajeId && x.Viaje.UsuarioId == conductorId, ct);
        if (s == null) return null;
        var v = s.Viaje;
        var dto = await new SalidasViajeServicio(_db).ObtenerAsync(conductorId, viajeId, salidaId, ct);
        var r = new ResultadoOptimizacionDto { ViajeId = v.Id, SalidaId = s.Id, Origen = v.Origen, Destino = v.Destino, Salida = s.FechaHora,
            CupoTotal = v.CantMaxPaquetes, PesoMaximoKg = v.PesoMax, DesvioMaxKm = v.DesvioMaxKm,
            CuposComprometidos = dto?.PaquetesComprometidos ?? 0, PesoComprometidoKg = dto?.PesoComprometidoKg ?? 0,
            CuposDisponibles = dto?.CuposDisponibles ?? 0, PesoDisponibleKg = dto?.PesoDisponibleKg ?? 0 };
        string? bloqueo = null;
        if (!v.Activo) bloqueo = "El viaje no está activo.";
        else if (dto == null || !dto.Disponible) bloqueo = "La salida ya no está disponible o no coincide con los días y horarios del viaje.";
        else if (dto.AdvertenciaCapacidad != null || !dto.CuposDisponibles.HasValue || !dto.PesoDisponibleKg.HasValue)
            bloqueo = dto.AdvertenciaCapacidad ?? "No se pudo verificar la capacidad restante de la salida.";
        else if (dto.CuposDisponibles == 0) bloqueo = "La salida ya no tiene cupos disponibles.";
        else if (dto.PesoDisponibleKg <= 0) bloqueo = "La salida ya no tiene peso disponible.";
        else if (dto.LlegadaMaxima.Estado == EstadoRestriccionTemporal.Desconocida)
            bloqueo = "La salida no tiene definido su horario de llegada; elegila nuevamente indicando el límite o sin restricción.";
        var comprometidos = await _datos.ObtenerComprometidosAsync(viajeId, salidaId, ct);
        if (bloqueo == null && comprometidos == null)
            bloqueo = "Hay paquetes asignados cuyas ubicaciones no pueden verificarse; no se puede evaluar el recorrido combinado.";
        if (bloqueo == null && comprometidos!.Any(c => c.Estado is not EstadoEnvio.ConductorAsignado))
            bloqueo = "La salida ya tiene paquetes retirados o entregados: no se pueden agregar nuevos paquetes.";
        return new(r, new() { DesvioMaxKm = v.DesvioMaxKm, Salida = s.FechaHora, LlegadaMaxima = dto?.LlegadaMaxima ?? RestriccionTemporalDto.Desconocida() },
            s, comprometidos ?? Array.Empty<PaqueteComprometidoDto>(), bloqueo);
    }

    private ViajeOportunidadesDto ViajeDto(Contexto ctx) => new()
    {
        Id = ctx.Base.ViajeId, UsuarioId = ctx.Salida.Viaje.UsuarioId, Origen = ctx.Base.Origen, Destino = ctx.Base.Destino,
        Activo = ctx.Salida.Viaje.Activo, DesvioMaxKm = ctx.Base.DesvioMaxKm,
        SalidaSeleccionada = new SalidaViajeDto { Id = ctx.Salida.Id, ViajeId = ctx.Base.ViajeId, FechaHora = ctx.Salida.FechaHora,
            Disponible = true, LlegadaMaxima = ctx.Recorrido.LlegadaMaxima, PaquetesComprometidos = ctx.Base.CuposComprometidos,
            PesoComprometidoKg = ctx.Base.PesoComprometidoKg, CuposDisponibles = ctx.Base.CuposDisponibles, PesoDisponibleKg = ctx.Base.PesoDisponibleKg }
    };

    private async Task<List<Candidato>> CargarCandidatosAsync(int conductorId, Contexto ctx, ResultadoOptimizacionDto r,
        List<PaqueteRecorridoDto> fijos, CancellationToken ct)
    {
        var pool = await _db.Envios.AsNoTracking()
            .Where(e => e.Estado == EstadoEnvio.BuscandoConductor && e.UsuarioId != conductorId)
            .OrderByDescending(e => e.Monto).ThenBy(e => e.Id).Take(MaxPool).ToListAsync(ct);
        var datos = await _datos.ObtenerLoteAsync(ViajeDto(ctx), pool.Select(e => new EnvioDto
        {
            Id = e.Id, UsuarioId = e.UsuarioId, OrigenDir = e.OrigenDir, DestinoDir = e.DestinoDir, FechaLimite = e.FechaLimite,
            PesoKg = e.PesoKg, Tamano = e.Tamano, Descripcion = e.Descripcion, Monto = e.Monto, Estado = e.Estado
        }).ToList(), ct);
        var lista = new List<Candidato>();
        foreach (var e in pool)
        {
            string? motivo = null; PaqueteRecorridoDto? paquete = null;
            datos.TryGetValue(e.Id, out var d);
            if (d?.RetiroConfirmado == null || d.EntregaConfirmada == null) motivo = "le faltan direcciones confirmadas.";
            else if (e.PesoKg <= 0) motivo = "su peso no es válido.";
            else if (e.PesoKg > r.PesoDisponibleKg) motivo = "supera el peso disponible en tu salida.";
            else if (d.FechaLimiteEntrega.Estado != EstadoRestriccionTemporal.Informada) motivo = "su fecha límite no puede interpretarse.";
            else if (d.FechaLimiteEntrega.Valor < ctx.Salida.FechaHora) motivo = "vence antes de la salida.";
            else if (d.DisponibleDesde.Estado == EstadoRestriccionTemporal.Desconocida) motivo = "no tiene definido su horario de retiro.";
            else
            {
                paquete = new() { EnvioId = e.Id, Descripcion = e.Descripcion ?? "", OrigenDir = e.OrigenDir, DestinoDir = e.DestinoDir,
                    Retiro = d.RetiroConfirmado, Entrega = d.EntregaConfirmada, DisponibleDesde = d.DisponibleDesde, FechaLimiteEntrega = d.FechaLimiteEntrega };
                // Solo se descarta con una cota demostrada en línea recta; nunca por una estimación.
                if (_recorridos.ImposibleProbado(ctx.Recorrido, fijos.Append(paquete).ToList())) motivo = "no entra en tu desvío máximo permitido.";
            }
            lista.Add(new(e, paquete!, motivo));
        }
        return lista;
    }

    private sealed record Candidato(Envio Envio, PaqueteRecorridoDto Paquete, string? MotivoDescarte);

    /// <summary>Todos los subconjuntos que respetan cupos y peso restantes. Los geográficos/temporales se evalúan después.</summary>
    private static List<(decimal Ganancia, List<int> Items)> Subconjuntos(List<Candidato> candidatos, int cupos, decimal pesoLibre)
    {
        var grupos = new List<(decimal, List<int>)>();
        void Rec(int i, decimal ganancia, decimal peso, List<int> sel)
        {
            if (i == candidatos.Count) { if (sel.Count > 0) grupos.Add((ganancia, sel.ToList())); return; }
            Rec(i + 1, ganancia, peso, sel);
            var c = candidatos[i].Envio;
            if (sel.Count < cupos && sel.Count < MaxPaquetes && peso + c.PesoKg <= pesoLibre)
            { sel.Add(i); Rec(i + 1, ganancia + c.Monto, peso + c.PesoKg, sel); sel.RemoveAt(sel.Count - 1); }
        }
        Rec(0, 0, 0, new List<int>());
        return grupos;
    }

    private CombinacionDto Combinar(IReadOnlyList<Envio> seleccionados, IReadOnlyList<PaqueteRecorridoDto> paquetes,
        ResultadoRecorridoDto r, Contexto ctx)
    {
        var versiones = seleccionados.Select(e => (e.Id, e.RowVersion)).Concat(ctx.Comprometidos.Select(c => (c.EnvioId, c.Version)));
        var porId = seleccionados.ToDictionary(e => e.Id);
        return new()
        {
            Paquetes = paquetes.Select(p => { var e = porId[p.EnvioId]; return new PaqueteOptimizadoDto
            {
                EnvioId = e.Id, Descripcion = e.Descripcion ?? "", OrigenDir = e.OrigenDir, DestinoDir = e.DestinoDir,
                PesoKg = e.PesoKg, Monto = e.Monto, FechaLimite = e.FechaLimite
            }; }).ToList(),
            GananciaTotal = seleccionados.Sum(e => e.Monto), PesoTotalKg = seleccionados.Sum(e => e.PesoKg),
            DistanciaOriginalMetros = r.DistanciaOriginalMetros, DistanciaCombinadaMetros = r.DistanciaCombinadaMetros,
            DesvioMetros = r.DesvioMetros, DuracionOriginalSegundos = r.DuracionOriginalSegundos,
            DuracionCombinadaSegundos = r.DuracionCombinadaSegundos, TiempoAdicionalSegundos = r.TiempoAdicionalSegundos,
            FinEstimado = r.FinEstimado, Orden = r.Orden,
            Huella = IOptimizadorServicio.Huella(ctx.Salida.Id, ctx.Salida.Version, versiones)
        };
    }

    private static PaqueteRecorridoDto Paquete(PaqueteComprometidoDto c) => new()
    {
        EnvioId = c.EnvioId, Descripcion = c.Descripcion, OrigenDir = c.OrigenDir, DestinoDir = c.DestinoDir,
        Retiro = c.Retiro, Entrega = c.Entrega, DisponibleDesde = c.DisponibleDesde, FechaLimiteEntrega = c.FechaLimiteEntrega
    };
}
