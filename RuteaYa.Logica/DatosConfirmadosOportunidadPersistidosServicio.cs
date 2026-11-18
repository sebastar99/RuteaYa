using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>Consultas sin tracking, lazy-loading ni consultas por candidato.</summary>
public sealed class DatosConfirmadosOportunidadPersistidosServicio(RuteaYaDbContext db) : IDatosConfirmadosOportunidadServicio
{
    public async Task<DatosConfirmadosOportunidadDto?> ObtenerAsync(ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default) =>
        (await ObtenerLoteAsync(viaje, new[] { envio }, cancellationToken)).GetValueOrDefault(envio.Id);

    public async Task<IReadOnlyDictionary<int, DatosConfirmadosOportunidadDto?>> ObtenerLoteAsync(ViajeOportunidadesDto viaje,
        IReadOnlyList<EnvioDto> envios, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var resultado = new Dictionary<int, DatosConfirmadosOportunidadDto?>();
        if (envios.Count == 0 || viaje.UsuarioId <= 0) return resultado;
        var v = await db.Viajes.AsNoTracking().SingleOrDefaultAsync(v => v.Id == viaje.Id && v.UsuarioId == viaje.UsuarioId, ct);
        if (v == null || v.Origen != viaje.Origen || v.Destino != viaje.Destino || v.Activo != viaje.Activo || v.DesvioMaxKm != viaje.DesvioMaxKm) return resultado;
        // El selector es exclusivamente del servidor y ya revalidó salida y compromisos en esta consulta.
        var salida = viaje.SalidaSeleccionada;
        if (salida != null && salida.ViajeId != v.Id) return resultado;
        var paradas = await db.Paradas.AsNoTracking().Where(p => p.ViajeId == v.Id).OrderBy(p => p.Orden).ToListAsync(ct);
        var ids = envios.Select(e => e.Id).Distinct().ToArray();
        var paquetes = await db.Envios.AsNoTracking().Where(e => ids.Contains(e.Id) && e.UsuarioId != v.UsuarioId && e.Estado == EstadoEnvio.BuscandoConductor).ToListAsync(ct);
        var ubicaciones = await CargarUbicaciones(
            new[] { v.OrigenUbicacionId, v.DestinoUbicacionId }
                .Concat(paradas.Select(p => p.UbicacionConfirmadaId))
                .Concat(paquetes.SelectMany(e => new[] { e.RetiroUbicacionId, e.EntregaUbicacionId })), ct);
        var puntos = new List<PuntoRutaDto?> { LeerPunto(ubicaciones, v.OrigenUbicacionId, v.UsuarioId, TipoRecursoUbicacion.Viaje, v.Id, CampoUbicacion.OrigenViaje, v.Origen) };
        puntos.AddRange(paradas.Select(p => LeerPunto(ubicaciones, p.UbicacionConfirmadaId, v.UsuarioId, TipoRecursoUbicacion.Viaje, v.Id, CampoUbicacion.Parada, p.Ubicacion)));
        puntos.Add(LeerPunto(ubicaciones, v.DestinoUbicacionId, v.UsuarioId, TipoRecursoUbicacion.Viaje, v.Id, CampoUbicacion.DestinoViaje, v.Destino));
        var recorridoValido = puntos.All(p => p != null) && paradas.All(p => p.Orden > 0) && paradas.Select(p => p.Orden).Distinct().Count() == paradas.Count;
        foreach (var e in paquetes)
        {
            var publicado = envios.Single(p => p.Id == e.Id);
            if (publicado.UsuarioId != e.UsuarioId || publicado.OrigenDir != e.OrigenDir || publicado.DestinoDir != e.DestinoDir ||
                publicado.PesoKg != e.PesoKg || publicado.FechaLimite != e.FechaLimite) continue;
            var retiro = LeerPunto(ubicaciones, e.RetiroUbicacionId, e.UsuarioId, TipoRecursoUbicacion.Envio, e.Id, CampoUbicacion.RetiroEnvio, e.OrigenDir);
            var entrega = LeerPunto(ubicaciones, e.EntregaUbicacionId, e.UsuarioId, TipoRecursoUbicacion.Envio, e.Id, CampoUbicacion.EntregaEnvio, e.DestinoDir);
            var datos = new DatosConfirmadosOportunidadDto {
                ViajeId = v.Id, EnvioId = e.Id, PermitirEvaluacionParcial = true,
                RecorridoConfirmado = recorridoValido ? puntos.Select(p => p!).ToArray() : null,
                RetiroConfirmado = retiro, EntregaConfirmada = entrega,
                PesoMaximoPublicadoKg = v.PesoMax >= 0 ? v.PesoMax : null, CuposMaximosPublicados = v.CantMaxPaquetes >= 0 ? v.CantMaxPaquetes : null,
                // Inactivo sí prueba indisponibilidad. Activo no prueba disponibilidad de una salida.
                Disponible = !v.Activo ? false : salida?.Disponible,
                SalidaProgramada = salida?.FechaHora, PesoDisponibleKg = salida?.PesoDisponibleKg, CuposDisponibles = salida?.CuposDisponibles,
                LlegadaMaxima = salida?.LlegadaMaxima ?? RestriccionTemporalDto.Desconocida(),
                DisponibleDesde = RestriccionRetiro(e),
                FechaLimiteEntrega = RestriccionEntrega(e, retiro != null && entrega != null)
            };
            resultado.Add(e.Id, datos);
        }
        return resultado;
    }

    public async Task<RecorridoConfirmadoDto?> ObtenerRecorridoBaseAsync(int usuarioId, int viajeId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (usuarioId <= 0 || viajeId <= 0) return null;
        var v = await db.Viajes.AsNoTracking().SingleOrDefaultAsync(v => v.Id == viajeId && v.UsuarioId == usuarioId, ct);
        if (v == null) return null;
        var paradas = await db.Paradas.AsNoTracking().Where(p => p.ViajeId == v.Id).OrderBy(p => p.Orden).ToListAsync(ct);
        if (paradas.Any(p => p.Orden <= 0) || paradas.Select(p => p.Orden).Distinct().Count() != paradas.Count) return null;
        var ubicaciones = await CargarUbicaciones(
            new[] { v.OrigenUbicacionId, v.DestinoUbicacionId }.Concat(paradas.Select(p => p.UbicacionConfirmadaId)), ct);
        var puntos = new List<PuntoRutaDto?> { LeerPunto(ubicaciones, v.OrigenUbicacionId, v.UsuarioId, TipoRecursoUbicacion.Viaje, v.Id, CampoUbicacion.OrigenViaje, v.Origen) };
        puntos.AddRange(paradas.Select(p => LeerPunto(ubicaciones, p.UbicacionConfirmadaId, v.UsuarioId, TipoRecursoUbicacion.Viaje, v.Id, CampoUbicacion.Parada, p.Ubicacion)));
        puntos.Add(LeerPunto(ubicaciones, v.DestinoUbicacionId, v.UsuarioId, TipoRecursoUbicacion.Viaje, v.Id, CampoUbicacion.DestinoViaje, v.Destino));
        if (puntos.Any(p => p == null)) return null;
        var etiquetas = new List<string> { v.Origen };
        etiquetas.AddRange(paradas.Select(p => p.Ubicacion));
        etiquetas.Add(v.Destino);
        return new RecorridoConfirmadoDto { Puntos = puntos.Select(p => p!).ToArray(), Etiquetas = etiquetas };
    }

    public async Task<IReadOnlyList<PaqueteComprometidoDto>?> ObtenerComprometidosAsync(int viajeId, int salidaId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (salidaId <= 0) return Array.Empty<PaqueteComprometidoDto>();
        var comprometidos = await db.Envios.AsNoTracking()
            .Where(e => e.SalidaViajeId == salidaId && e.Estado != EstadoEnvio.Cancelado && e.Estado != EstadoEnvio.BuscandoConductor)
            .ToListAsync(ct);
        var ubicaciones = await CargarUbicaciones(comprometidos.SelectMany(e => new[] { e.RetiroUbicacionId, e.EntregaUbicacionId }), ct);
        var lista = new List<PaqueteComprometidoDto>();
        foreach (var e in comprometidos)
        {
            var retiro = LeerPunto(ubicaciones, e.RetiroUbicacionId, e.UsuarioId, TipoRecursoUbicacion.Envio, e.Id, CampoUbicacion.RetiroEnvio, e.OrigenDir);
            var entrega = LeerPunto(ubicaciones, e.EntregaUbicacionId, e.UsuarioId, TipoRecursoUbicacion.Envio, e.Id, CampoUbicacion.EntregaEnvio, e.DestinoDir);
            if (retiro == null || entrega == null) return null;
            lista.Add(new PaqueteComprometidoDto { EnvioId = e.Id, Descripcion = e.Descripcion ?? "", OrigenDir = e.OrigenDir,
                DestinoDir = e.DestinoDir, PesoKg = e.PesoKg, Estado = e.Estado, Retiro = retiro, Entrega = entrega,
                DisponibleDesde = RestriccionRetiro(e), FechaLimiteEntrega = RestriccionEntrega(e, true), Version = e.RowVersion });
        }
        return lista;
    }

    private async Task<Dictionary<Guid, PuntoConfirmado>> CargarUbicaciones(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var concretos = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        // No cargar hashes ni tokens al preparar evaluaciones.
        return await db.UbicacionesConfirmadas.AsNoTracking().Where(u => concretos.Contains(u.Id)).Select(u => new PuntoConfirmado {
            Id = u.Id, Usuario = u.UsuarioId, Tipo = u.TipoRecurso, Recurso = u.RecursoId, Campo = u.Campo,
            Texto = u.DireccionNormalizada, Lat = u.NavegacionLatitud, Lng = u.NavegacionLongitud,
            PosLat = u.PosicionLatitud, PosLng = u.PosicionLongitud, Acceso = u.AccesoElegido,
            Confirmada = u.ConfirmadaEnUtc, Consumida = u.ConsumidaEnUtc, Vence = u.VenceEnUtc, Proveedor = u.Proveedor,
            IdHere = u.IdHere }).ToDictionaryAsync(u => u.Id, ct);
    }

    private static PuntoRutaDto? LeerPunto(IReadOnlyDictionary<Guid, PuntoConfirmado> ubicaciones, Guid? id, int usuario,
        TipoRecursoUbicacion tipo, int recurso, CampoUbicacion campo, string texto)
    {
        if (!id.HasValue || !ubicaciones.TryGetValue(id.Value, out var u) || u.Usuario != usuario || u.Tipo != tipo ||
            u.Campo != campo || u.Recurso.HasValue && u.Recurso != recurso || u.Texto != texto ||
            u.Consumida == null || u.Confirmada == default || u.Confirmada > u.Consumida || u.Consumida > u.Vence ||
            u.Proveedor != "HERE-v7" || string.IsNullOrWhiteSpace(u.IdHere) || !u.IdHere.StartsWith("here:", StringComparison.Ordinal) ||
            u.Lat is < -90 or > 90 || u.Lng is < -180 or > 180 || u.PosLat is < -90 or > 90 || u.PosLng is < -180 or > 180 ||
            u.Acceso < -1 || u.Acceso == -1 && (u.Lat != u.PosLat || u.Lng != u.PosLng)) return null;
        return new() { Latitud = u.Lat, Longitud = u.Lng };
    }

    private static RestriccionTemporalDto RestriccionRetiro(Envio e) =>
        e.RetiroDisponibleDesde.HasValue ? RestriccionTemporalDto.Informada(e.RetiroDisponibleDesde.Value) :
        e.RetiroSinRestriccion ? RestriccionTemporalDto.SinRestriccion() : RestriccionTemporalDto.Desconocida();

    private static RestriccionTemporalDto RestriccionEntrega(Envio e, bool conPuntos)
    {
        // datetime2 no conserva zona ni Kind; no se interpreta como UTC ni con la zona del servidor.
        if (e.EntregaLimiteConOffset.HasValue && e.EntregaLimiteConOffset.Value.Date == e.FechaLimite.Date)
            return RestriccionTemporalDto.Informada(e.EntregaLimiteConOffset.Value);
        if (!e.EntregaLimiteConOffset.HasValue && e.FechaLimite != default && e.FechaLimite.Kind == DateTimeKind.Utc)
            return RestriccionTemporalDto.Informada(new DateTimeOffset(e.FechaLimite));
        // Una fecha límite sin horario (00:00) significa "hasta el final de ese día". Solo se interpreta cuando retiro y
        // entrega son direcciones argentinas confirmadas por HERE; si la zona no puede determinarse, el dato sigue pendiente.
        if (!e.EntregaLimiteConOffset.HasValue && e.FechaLimite != default && e.FechaLimite.TimeOfDay == TimeSpan.Zero && conPuntos &&
            ZonaHorariaViaje.PorDirecciones(e.OrigenDir, e.DestinoDir) is { } zona)
            return RestriccionTemporalDto.Informada(ZonaHorariaViaje.Convertir(e.FechaLimite.Date.AddDays(1).AddSeconds(-1), zona));
        return RestriccionTemporalDto.Desconocida();
    }

    private sealed class PuntoConfirmado
    {
        public Guid Id { get; set; } public int Usuario { get; set; } public TipoRecursoUbicacion Tipo { get; set; }
        public int? Recurso { get; set; } public CampoUbicacion Campo { get; set; } public string Texto { get; set; } = "";
        public decimal Lat { get; set; } public decimal Lng { get; set; } public decimal PosLat { get; set; } public decimal PosLng { get; set; }
        public int Acceso { get; set; } public DateTimeOffset Confirmada { get; set; } public DateTimeOffset? Consumida { get; set; }
        public DateTimeOffset Vence { get; set; } public string Proveedor { get; set; } = ""; public string IdHere { get; set; } = "";
    }
}
