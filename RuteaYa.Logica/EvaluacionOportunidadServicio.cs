using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public class EvaluacionOportunidadServicio : IEvaluacionOportunidadServicio
{
    private readonly IDatosConfirmadosOportunidadServicio _datos;
    private readonly ICompatibilidadServicio _motor;

    public EvaluacionOportunidadServicio(IDatosConfirmadosOportunidadServicio datos, ICompatibilidadServicio motor)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(motor);
        _datos = datos;
        _motor = motor;
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

        var pendientes = RequisitosPendientes(viaje, envio, datos);
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
        var resultado = await _motor.EvaluarAsync(viajeMotor, envioMotor, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resultado == null || resultado.ViajeId != viaje.Id || resultado.EnvioId != envio.Id)
            throw new InvalidOperationException("El motor no devolvió una evaluación válida para el candidato.");
        return new EvaluacionOportunidadDto { ResultadoMotor = resultado, DatosPendientes = pendientes.AsReadOnly() };
    }

    private static List<DatoPendienteOportunidad> RequisitosPendientes(
        ViajeOportunidadesDto viaje, EnvioDto envio, DatosConfirmadosOportunidadDto datos)
    {
        var faltantes = new List<DatoPendienteOportunidad>();
        // No se conserva el orden de inserción de los paquetes ya asignados. No se
        // evalúa el candidato aislado como si esos compromisos no existieran.
        if (viaje.SalidaSeleccionada?.PaquetesComprometidos > 0)
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
