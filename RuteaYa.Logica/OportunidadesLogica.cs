using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public class OportunidadesLogica : IOportunidadesLogica
{
    private readonly RuteaYaDbContext _db;
    private readonly IEvaluacionOportunidadServicio _evaluacion;

    private static readonly Expression<Func<Viaje, ViajeOportunidadesDto>> DatosViaje = v =>
        new ViajeOportunidadesDto { Id = v.Id, Origen = v.Origen, Destino = v.Destino, Activo = v.Activo, DesvioMaxKm = v.DesvioMaxKm, UsuarioId = v.UsuarioId };

    private static readonly Expression<Func<Envio, EnvioDto>> DatosEnvio = e => new EnvioDto
    {
        Id = e.Id, UsuarioId = e.UsuarioId, OrigenDir = e.OrigenDir, DestinoDir = e.DestinoDir,
        FechaLimite = e.FechaLimite, PesoKg = e.PesoKg, Tamano = e.Tamano,
        Descripcion = e.Descripcion, Monto = e.Monto, Estado = e.Estado
    };

    public OportunidadesLogica(RuteaYaDbContext db, IEvaluacionOportunidadServicio evaluacion)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(evaluacion);
        _db = db;
        _evaluacion = evaluacion;
    }

    public async Task<ListadoCandidatosDto?> ConsultarAsync(
        int conductorId, int? viajeId = null, int pagina = 1, int tamanoPagina = 10,
        CancellationToken cancellationToken = default)
        => await ConsultarInternoAsync(conductorId, viajeId, null, pagina, tamanoPagina, cancellationToken);

    public Task<int?> SeleccionarSalidaAsync(int conductorId, SeleccionSalidaDto seleccion, CancellationToken ct = default) =>
        new SalidasViajeServicio(_db).SeleccionarAsync(conductorId, seleccion, ct);

    public Task<ListadoCandidatosDto?> ConsultarSalidaAsync(int conductorId, int viajeId, int salidaId,
        int pagina = 1, int tamanoPagina = 10, CancellationToken ct = default) =>
        ConsultarInternoAsync(conductorId, viajeId, salidaId, pagina, tamanoPagina, ct);

    private async Task<ListadoCandidatosDto?> ConsultarInternoAsync(int conductorId, int? viajeId, int? salidaId,
        int pagina, int tamanoPagina, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (conductorId <= 0) throw new ArgumentOutOfRangeException(nameof(conductorId));
        if (viajeId.HasValue && viajeId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(viajeId));
        if (pagina < 1) throw new ArgumentOutOfRangeException(nameof(pagina));
        if (tamanoPagina < 1 || tamanoPagina > 50) throw new ArgumentOutOfRangeException(nameof(tamanoPagina));

        // Una sola consulta ligera sirve al selector y a la verificación de propiedad.
        var viajes = await _db.Viajes.AsNoTracking()
            .Where(v => v.UsuarioId == conductorId)
            .OrderByDescending(v => v.Id)
            .Select(DatosViaje)
            .ToListAsync(cancellationToken);

        var seleccionado = viajeId.HasValue ? viajes.FirstOrDefault(v => v.Id == viajeId.Value) : null;
        if (viajeId.HasValue && seleccionado == null)
            return null; // No consultar envíos ni distinguir viaje ajeno de inexistente.

        var resultado = new ListadoCandidatosDto
        {
            Viajes = viajes.AsReadOnly(), ViajeSeleccionado = seleccionado, TamanoPagina = tamanoPagina
        };
        if (seleccionado == null) return resultado;
        if (salidaId.HasValue)
        {
            seleccionado.SalidaSeleccionada = await new SalidasViajeServicio(_db).ObtenerAsync(conductorId, seleccionado.Id, salidaId.Value, cancellationToken);
            if (seleccionado.SalidaSeleccionada == null) return null;
        }

        // Candidatos de otros usuarios, coherente con el detalle y la regla de asignación.
        var disponibles = _db.Envios.AsNoTracking()
            .Where(e => e.Estado == EstadoEnvio.BuscandoConductor && e.UsuarioId != conductorId);
        resultado.TotalCandidatos = await disponibles.CountAsync(cancellationToken);
        resultado.Pagina = Math.Min(pagina, Math.Max(1, resultado.TotalPaginas));
        if (resultado.TotalCandidatos == 0) return resultado;

        var candidatos = await disponibles.OrderByDescending(e => e.Id)
            .Skip((resultado.Pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(DatosEnvio)
            .ToListAsync(cancellationToken);
        resultado.Candidatos = candidatos.AsReadOnly();
        resultado.Evaluaciones = await _evaluacion.EvaluarLoteAsync(seleccionado, resultado.Candidatos, cancellationToken);
        return resultado;
    }

    public async Task<DetalleCandidatoDto?> ObtenerDetalleAsync(
        int conductorId, int viajeId, int envioId, CancellationToken cancellationToken = default)
        => await ObtenerDetalleInternoAsync(conductorId, viajeId, null, envioId, cancellationToken);

    public Task<DetalleCandidatoDto?> ObtenerDetalleSalidaAsync(int conductorId, int viajeId, int salidaId, int envioId, CancellationToken ct = default) =>
        ObtenerDetalleInternoAsync(conductorId, viajeId, salidaId, envioId, ct);

    private async Task<DetalleCandidatoDto?> ObtenerDetalleInternoAsync(int conductorId, int viajeId, int? salidaId, int envioId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (conductorId <= 0) throw new ArgumentOutOfRangeException(nameof(conductorId));
        if (viajeId <= 0) throw new ArgumentOutOfRangeException(nameof(viajeId));
        if (envioId <= 0) throw new ArgumentOutOfRangeException(nameof(envioId));

        var viaje = await _db.Viajes.AsNoTracking()
            .Where(v => v.Id == viajeId && v.UsuarioId == conductorId)
            .Select(DatosViaje)
            .SingleOrDefaultAsync(cancellationToken);
        if (viaje == null) return null;
        if (salidaId.HasValue)
        {
            viaje.SalidaSeleccionada = await new SalidasViajeServicio(_db).ObtenerAsync(conductorId, viajeId, salidaId.Value, cancellationToken);
            if (viaje.SalidaSeleccionada == null) return null;
        }

        // Revalidar al abrir: el estado pudo cambiar después de mostrar el listado.
        var envio = await _db.Envios.AsNoTracking()
            .Where(e => e.Id == envioId && e.Estado == EstadoEnvio.BuscandoConductor && e.UsuarioId != conductorId)
            .Select(DatosEnvio)
            .SingleOrDefaultAsync(cancellationToken);
        if (envio == null) return null;
        var evaluacion = await _evaluacion.EvaluarAsync(viaje, envio, cancellationToken);
        return new DetalleCandidatoDto { Viaje = viaje, Envio = envio, Evaluacion = evaluacion };
    }
}
