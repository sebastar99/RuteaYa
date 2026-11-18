using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public class EvaluacionOportunidadServicio : IEvaluacionOportunidadServicio
{
    private readonly IDatosConfirmadosOportunidadServicio _datos;
    private readonly ICompatibilidadServicio _motor;
    private readonly IRecorridoCombinadoServicio? _combinado;
    private PresupuestoRutas? _presupuesto;
    private (int Viaje, int Salida) _claveComprometidos;
    private IReadOnlyList<PaqueteComprometidoDto>? _comprometidos;
    private bool _comprometidosCargados;

    public EvaluacionOportunidadServicio(IDatosConfirmadosOportunidadServicio datos, ICompatibilidadServicio motor,
        IRecorridoCombinadoServicio? combinado = null)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(motor);
        _datos = datos;
        _motor = motor;
        _combinado = combinado;
    }

    public async Task<EvaluacionOportunidadDto> EvaluarAsync(
        ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(viaje);
        ArgumentNullException.ThrowIfNull(envio);
        if (viaje.Id <= 0 || envio.Id <= 0) throw new ArgumentException("Los identificadores deben ser positivos.");

        var datos = await _datos.ObtenerAsync(viaje, envio, cancellationToken);
        return await EvaluarConDatosAsync(viaje, envio, datos, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, EvaluacionOportunidadDto>> EvaluarLoteAsync(ViajeOportunidadesDto viaje,
        IReadOnlyList<EnvioDto> envios, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (envios.Count == 0) return new Dictionary<int, EvaluacionOportunidadDto>();
        var datos = await _datos.ObtenerLoteAsync(viaje, envios, ct);
        var resultado = new Dictionary<int, EvaluacionOportunidadDto>();
        foreach (var envio in envios)
            resultado.Add(envio.Id, await EvaluarConDatosAsync(viaje, envio, datos.GetValueOrDefault(envio.Id), ct));
        return resultado;
    }

    private async Task<EvaluacionOportunidadDto> EvaluarConDatosAsync(ViajeOportunidadesDto viaje, EnvioDto envio,
        DatosConfirmadosOportunidadDto? datos, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (datos == null)
            return new EvaluacionOportunidadDto { DatosPendientes = new[] { DatoPendienteOportunidad.FuenteDeDatosNoDisponible } };
        if (datos.ViajeId != viaje.Id || datos.EnvioId != envio.Id)
            throw new InvalidOperationException("Los datos confirmados no corresponden al candidato consultado.");

        var comprometidos = await ComprometidosAsync(viaje, cancellationToken);
        var resoluble = _combinado != null && comprometidos != null;
        var pendientes = RequisitosPendientes(viaje, envio, datos, resoluble);
        var coordenadas = !pendientes.Any(p => p is DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas or
            DatoPendienteOportunidad.RetiroSinCoordenadasConfirmadas or DatoPendienteOportunidad.EntregaSinCoordenadasConfirmadas);
        var rechazoPrevio = !viaje.Activo || datos.Disponible == false || envio.Estado != RuteaYa.Entidades.EstadoEnvio.BuscandoConductor ||
            datos.PesoMaximoPublicadoKg.HasValue && envio.PesoKg > datos.PesoMaximoPublicadoKg || datos.CuposMaximosPublicados == 0 ||
            datos.PesoDisponibleKg.HasValue && envio.PesoKg > datos.PesoDisponibleKg || datos.CuposDisponibles == 0;
        var parcial = datos.PermitirEvaluacionParcial && (coordenadas || rechazoPrevio) &&
            (rechazoPrevio || !pendientes.Contains(DatoPendienteOportunidad.RecorridoConCompromisosNoVerificable)) &&
            !pendientes.Any(p => p is DatoPendienteOportunidad.CapacidadRestanteInvalida or DatoPendienteOportunidad.PesoEnvioInvalido or
                DatoPendienteOportunidad.DesvioMaximoInvalido or DatoPendienteOportunidad.RestriccionesTemporalesInconsistentes);
        if (pendientes.Count > 0 && !parcial)
            return new EvaluacionOportunidadDto { DatosPendientes = pendientes.AsReadOnly() };

        var viajeMotor = new ViajeCompatibilidadDto
        {
            ViajeId = viaje.Id, Activo = viaje.Activo, Disponible = datos.Disponible,
            PermitirEvaluacionParcial = parcial, PesoMaximoPublicadoKg = datos.PesoMaximoPublicadoKg, CuposMaximosPublicados = datos.CuposMaximosPublicados,
            DesvioMaxKm = viaje.DesvioMaxKm, PesoDisponibleKg = datos.PesoDisponibleKg,
            CuposDisponibles = datos.CuposDisponibles, SalidaProgramada = datos.SalidaProgramada,
            LlegadaMaxima = datos.LlegadaMaxima,
            RecorridoBase = datos.RecorridoConfirmado?.Select(Copiar).ToArray() ?? Array.Empty<PuntoRutaDto>()
        };
        var envioMotor = new EnvioCompatibilidadDto
        {
            EnvioId = envio.Id, PesoKg = envio.PesoKg, Estado = envio.Estado,
            Retiro = datos.RetiroConfirmado == null ? null! : Copiar(datos.RetiroConfirmado),
            Entrega = datos.EntregaConfirmada == null ? null! : Copiar(datos.EntregaConfirmada),
            DisponibleDesde = datos.DisponibleDesde, FechaLimiteEntrega = datos.FechaLimiteEntrega
        };
        // Con compromisos verificables y todos los datos presentes se evalúa el recorrido combinado real:
        // el paquete nuevo se inserta junto con los ya asignados, no aislado.
        if (pendientes.Count == 0 && resoluble && comprometidos!.Count > 0)
        {
            var combinado = await EvaluarCombinadoAsync(viaje, envio, datos, comprometidos!, cancellationToken);
            return new EvaluacionOportunidadDto { ResultadoMotor = combinado, DatosPendientes = pendientes.AsReadOnly() };
        }
        var resultado = await _motor.EvaluarAsync(viajeMotor, envioMotor, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resultado == null || resultado.ViajeId != viaje.Id || resultado.EnvioId != envio.Id)
            throw new InvalidOperationException("El motor no devolvió una evaluación válida para el candidato.");
        return new EvaluacionOportunidadDto { ResultadoMotor = resultado, DatosPendientes = pendientes.AsReadOnly() };
    }

    private async Task<IReadOnlyList<PaqueteComprometidoDto>?> ComprometidosAsync(ViajeOportunidadesDto viaje, CancellationToken ct)
    {
        var salida = viaje.SalidaSeleccionada;
        if (salida?.PaquetesComprometidos is not > 0 || _combinado == null) return Array.Empty<PaqueteComprometidoDto>();
        if (_comprometidosCargados && _claveComprometidos == (viaje.Id, salida.Id)) return _comprometidos;
        _claveComprometidos = (viaje.Id, salida.Id);
        _comprometidos = await _datos.ObtenerComprometidosAsync(viaje.Id, salida.Id, ct);
        _comprometidosCargados = true;
        return _comprometidos;
    }

    private async Task<ResultadoCompatibilidadDto> EvaluarCombinadoAsync(ViajeOportunidadesDto viaje, EnvioDto envio,
        DatosConfirmadosOportunidadDto datos, IReadOnlyList<PaqueteComprometidoDto> comprometidos, CancellationToken ct)
    {
        var paquetes = comprometidos.Select(c => new PaqueteRecorridoDto { EnvioId = c.EnvioId, Descripcion = c.Descripcion,
            OrigenDir = c.OrigenDir, DestinoDir = c.DestinoDir, Retiro = c.Retiro, Entrega = c.Entrega,
            DisponibleDesde = c.DisponibleDesde, FechaLimiteEntrega = c.FechaLimiteEntrega }).ToList();
        paquetes.Add(new() { EnvioId = envio.Id, Descripcion = envio.Descripcion ?? "", OrigenDir = envio.OrigenDir,
            DestinoDir = envio.DestinoDir, Retiro = datos.RetiroConfirmado!, Entrega = datos.EntregaConfirmada!,
            DisponibleDesde = datos.DisponibleDesde, FechaLimiteEntrega = datos.FechaLimiteEntrega });
        _presupuesto ??= new PresupuestoRutas();
        var contexto = new ContextoRecorridoDto { Base = datos.RecorridoConfirmado!.ToArray(), DesvioMaxKm = viaje.DesvioMaxKm,
            Salida = datos.SalidaProgramada, LlegadaMaxima = datos.LlegadaMaxima };
        if (!await _combinado!.PrepararBaseAsync(contexto, _presupuesto, ct))
            return new() { ViajeId = viaje.Id, EnvioId = envio.Id, Estado = EstadoCompatibilidad.Indeterminado,
                Motivos = new[] { MotivoCompatibilidad.DesvioNoEvaluado } };
        var r = await _combinado.EvaluarAsync(contexto, paquetes, _presupuesto, ct);
        var resultado = new ResultadoCompatibilidadDto { ViajeId = viaje.Id, EnvioId = envio.Id, Estado = r.Estado, Motivos = r.Motivos };
        if (r.Orden.Count > 0)
            resultado.InsercionSeleccionada = new()
            {
                Desvio = new()
                {
                    DistanciaOriginalMetros = r.DistanciaOriginalMetros, DuracionOriginalSegundos = r.DuracionOriginalSegundos,
                    DistanciaConEnvioMetros = r.DistanciaCombinadaMetros, DuracionConEnvioSegundos = r.DuracionCombinadaSegundos,
                    DistanciaAdicionalMetros = r.DesvioMetros, TiempoAdicionalSegundos = r.TiempoAdicionalSegundos,
                    CumpleLimiteDesvio = r.DesvioMetros <= viaje.DesvioMaxKm * 1000m
                },
                EstadoTemporal = r.Estado, MotivosTemporales = r.Motivos, FinalizacionEstimada = r.FinEstimado
            };
        return resultado;
    }

    private static List<DatoPendienteOportunidad> RequisitosPendientes(
        ViajeOportunidadesDto viaje, EnvioDto envio, DatosConfirmadosOportunidadDto datos, bool compromisosVerificables)
    {
        var faltantes = new List<DatoPendienteOportunidad>();
        // Solo se marca cuando los compromisos no pueden verificarse: en ese caso no se
        // evalúa el candidato aislado como si esos paquetes no existieran.
        if (viaje.SalidaSeleccionada?.PaquetesComprometidos > 0 && !compromisosVerificables)
            faltantes.Add(DatoPendienteOportunidad.RecorridoConCompromisosNoVerificable);
        if (datos.RecorridoConfirmado == null || datos.RecorridoConfirmado.Count < 2 || !datos.RecorridoConfirmado.All(PuntoValido))
            faltantes.Add(DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas);
        if (!PuntoValido(datos.RetiroConfirmado)) faltantes.Add(DatoPendienteOportunidad.RetiroSinCoordenadasConfirmadas);
        if (!PuntoValido(datos.EntregaConfirmada)) faltantes.Add(DatoPendienteOportunidad.EntregaSinCoordenadasConfirmadas);
        if (!datos.SalidaProgramada.HasValue || datos.SalidaProgramada.Value == default)
            faltantes.Add(DatoPendienteOportunidad.SalidaConcretaDesconocida);
        if (!datos.Disponible.HasValue) faltantes.Add(DatoPendienteOportunidad.DisponibilidadSalidaDesconocida);
        if (!datos.PesoDisponibleKg.HasValue) faltantes.Add(DatoPendienteOportunidad.PesoDisponibleDesconocido);
        if (!datos.CuposDisponibles.HasValue) faltantes.Add(DatoPendienteOportunidad.CuposDisponiblesDesconocidos);
        if (datos.PesoDisponibleKg < 0 || datos.CuposDisponibles < 0) faltantes.Add(DatoPendienteOportunidad.CapacidadRestanteInvalida);
        if (envio.PesoKg <= 0) faltantes.Add(DatoPendienteOportunidad.PesoEnvioInvalido);
        if (viaje.DesvioMaxKm < 0 || viaje.DesvioMaxKm > decimal.MaxValue / 1000m)
            faltantes.Add(DatoPendienteOportunidad.DesvioMaximoInvalido);
        if (!RestriccionConocida(datos.LlegadaMaxima)) faltantes.Add(DatoPendienteOportunidad.LlegadaMaximaDesconocida);
        if (!RestriccionConocida(datos.DisponibleDesde)) faltantes.Add(DatoPendienteOportunidad.InicioRetiroDesconocido);
        // FechaLimite es obligatoria en Envio: no se descarta usando SinRestriccion.
        if (envio.FechaLimite == default || datos.FechaLimiteEntrega?.Estado != EstadoRestriccionTemporal.Informada)
            faltantes.Add(DatoPendienteOportunidad.VencimientoEntregaNoInterpretado);

        var llegada = datos.LlegadaMaxima?.Valor;
        var inicio = datos.DisponibleDesde?.Valor;
        var vencimiento = datos.FechaLimiteEntrega?.Valor;
        if ((llegada.HasValue && datos.SalidaProgramada.HasValue && llegada < datos.SalidaProgramada) ||
            (inicio.HasValue && vencimiento.HasValue && inicio > vencimiento) ||
            (envio.FechaLimite.Kind == DateTimeKind.Utc && vencimiento.HasValue &&
                vencimiento.Value != new DateTimeOffset(envio.FechaLimite)))
            faltantes.Add(DatoPendienteOportunidad.RestriccionesTemporalesInconsistentes);
        return faltantes;
    }

    private static bool RestriccionConocida(RestriccionTemporalDto? dato) =>
        dato?.Estado is EstadoRestriccionTemporal.Informada or EstadoRestriccionTemporal.SinRestriccion;

    private static bool PuntoValido(PuntoRutaDto? punto) => punto != null &&
        punto.Latitud >= -90 && punto.Latitud <= 90 && punto.Longitud >= -180 && punto.Longitud <= 180;

    private static PuntoRutaDto Copiar(PuntoRutaDto punto) => new() { Latitud = punto.Latitud, Longitud = punto.Longitud };
}
