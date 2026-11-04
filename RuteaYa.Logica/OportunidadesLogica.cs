using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public class OportunidadesLogica : IOportunidadesLogica
{
    private readonly RuteaYaDbContext _db;

    private static readonly Expression<Func<Viaje, ViajeOportunidadesDto>> DatosViaje = v =>
        new ViajeOportunidadesDto { Id = v.Id, Origen = v.Origen, Destino = v.Destino, Activo = v.Activo };

    private static readonly Expression<Func<Envio, EnvioDto>> DatosEnvio = e => new EnvioDto
    {
        Id = e.Id, UsuarioId = e.UsuarioId, OrigenDir = e.OrigenDir, DestinoDir = e.DestinoDir,
        FechaLimite = e.FechaLimite, PesoKg = e.PesoKg, Tamano = e.Tamano,
        Descripcion = e.Descripcion, Monto = e.Monto, Estado = e.Estado
    };

    public OportunidadesLogica(RuteaYaDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<ListadoCandidatosDto?> ConsultarAsync(
        int conductorId, int? viajeId = null, int pagina = 1, int tamanoPagina = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (conductorId <= 0) throw new ArgumentOutOfRangeException(nameof(conductorId));
        if (viajeId.HasValue && viajeId.Value <= 0) throw new ArgumentOutOfRangeException(nameof(viajeId));
        if (pagina < 1) throw new ArgumentOutOfRangeException(nameof(pagina));
        if (tamanoPagina < 1 || tamanoPagina > 50) throw new ArgumentOutOfRangeException(nameof(tamanoPagina));

        // Una sola consulta ligera sirve al selector y a la verificación de propiedad.
        // No cargar días, paradas ni máximos de capacidad que aún no vamos a evaluar.
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

        // No hay una regla existente de exclusión de envíos propios. Incluimos todos
        // los publicados como disponibles; no usamos el listado por dueño de EnviosLogica.
        var disponibles = _db.Envios.AsNoTracking()
            .Where(e => e.Estado == EstadoEnvio.BuscandoConductor);
        resultado.TotalCandidatos = await disponibles.CountAsync(cancellationToken);
        resultado.Pagina = Math.Min(pagina, Math.Max(1, resultado.TotalPaginas));
        if (resultado.TotalCandidatos == 0) return resultado;

        var candidatos = await disponibles.OrderByDescending(e => e.Id)
            .Skip((resultado.Pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(DatosEnvio)
            .ToListAsync(cancellationToken);
        resultado.Candidatos = candidatos.AsReadOnly();
        return resultado;
    }

    public async Task<DetalleCandidatoDto?> ObtenerDetalleAsync(
        int conductorId, int viajeId, int envioId, CancellationToken cancellationToken = default)
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

        // Revalidar al abrir: el estado pudo cambiar después de mostrar el listado.
        var envio = await _db.Envios.AsNoTracking()
            .Where(e => e.Id == envioId && e.Estado == EstadoEnvio.BuscandoConductor)
            .Select(DatosEnvio)
            .SingleOrDefaultAsync(cancellationToken);
        return envio == null ? null : new DetalleCandidatoDto { Viaje = viaje, Envio = envio };
    }
}
