using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Reglas previas y búsqueda de inserción para un solo envío sobre datos en memoria.
/// Un envío ocupa un cupo y su peso durante toda la salida. Delega cada alternativa
/// a IDesviosServicio y evalúa horarios conservadoramente con la duración total.
/// No infiere instantes de retiro/entrega ni combina envíos.
/// </summary>
public class CompatibilidadServicio : ICompatibilidadServicio
{
    private readonly IDesviosServicio _desviosServicio;

    public CompatibilidadServicio(IDesviosServicio desviosServicio)
    {
        ArgumentNullException.ThrowIfNull(desviosServicio);
        _desviosServicio = desviosServicio;
    }

    public async Task<ResultadoCompatibilidadDto> EvaluarAsync(
        ViajeCompatibilidadDto viaje,
        EnvioCompatibilidadDto envio,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validar(viaje, envio);

        var motivos = new List<MotivoCompatibilidad>();
        if (!viaje.Activo)
            motivos.Add(MotivoCompatibilidad.ViajeInactivo);
        if (!viaje.Disponible)
            motivos.Add(MotivoCompatibilidad.ViajeNoDisponible);
        if (envio.Estado != EstadoEnvio.BuscandoConductor)
            motivos.Add(MotivoCompatibilidad.EnvioNoDisponible);
        if (viaje.PesoDisponibleKg.HasValue && envio.PesoKg > viaje.PesoDisponibleKg.Value)
            motivos.Add(MotivoCompatibilidad.PesoExcedido);
        if (viaje.CuposDisponibles == 0)
            motivos.Add(MotivoCompatibilidad.SinCupo);
        if (viaje.SalidaProgramada.HasValue && envio.FechaLimiteEntrega.HasValue &&
            viaje.SalidaProgramada.Value > envio.FechaLimiteEntrega.Value)
            motivos.Add(MotivoCompatibilidad.SalidaPosteriorAlVencimiento);

        // Se resuelve el rechazo antes de agregar datos desconocidos o reglas pendientes.
        var incompatible = motivos.Count > 0;
        if (!viaje.PesoDisponibleKg.HasValue)
            motivos.Add(MotivoCompatibilidad.PesoDisponibleDesconocido);
        if (!viaje.CuposDisponibles.HasValue)
            motivos.Add(MotivoCompatibilidad.CuposDisponiblesDesconocidos);

        var resultado = new ResultadoCompatibilidadDto
        {
            ViajeId = viaje.ViajeId,
            EnvioId = envio.EnvioId,
            Estado = incompatible ? EstadoCompatibilidad.Incompatible : EstadoCompatibilidad.Indeterminado,
            Motivos = motivos.AsReadOnly()
        };

        if (incompatible || !viaje.PesoDisponibleKg.HasValue || !viaje.CuposDisponibles.HasValue)
        {
            motivos.Add(MotivoCompatibilidad.DesvioNoEvaluado);
            motivos.Add(MotivoCompatibilidad.HorarioNoEvaluado);
            return resultado;
        }

        var cantidadSegmentos = viaje.RecorridoBase.Count - 1;
        decimal limiteMetros = viaje.DesvioMaxKm * 1000m;
        InsercionCompatibilidadDto? mejorCompatible = null;
        InsercionCompatibilidadDto? mejorProvisional = null;
        var hayInsercionGeografica = false;
        var rechazosTemporales = new List<MotivoCompatibilidad>();
        var evaluacion = await _desviosServicio.PrepararAsync(
            viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (evaluacion == null)
            throw new InvalidOperationException("El servicio de desvíos no preparó la evaluación.");

        // Todas las parejas i <= j; el orden ascendente también hace determinista el recorrido.
        // No se corta al hallar una válida: otra puede tener menor distancia o tiempo adicional.
        for (var retiro = 0; retiro < cantidadSegmentos; retiro++)
        {
            for (var entrega = retiro; entrega < cantidadSegmentos; entrega++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var desvio = await evaluacion.CalcularAsync(retiro, entrega, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (desvio == null)
                    throw new InvalidOperationException("El servicio de desvíos no devolvió un resultado.");

                if (desvio.DistanciaAdicionalMetros > limiteMetros)
                    continue;

                hayInsercionGeografica = true;
                var candidata = new InsercionCompatibilidadDto
                {
                    SegmentoRetiro = retiro,
                    SegmentoEntrega = entrega,
                    Desvio = desvio
                };
                EvaluarHorario(viaje, envio, candidata);
                if (candidata.EstadoTemporal == EstadoCompatibilidad.Compatible)
                {
                    if (mejorCompatible == null || EsMejor(candidata, mejorCompatible))
                        mejorCompatible = candidata;
                }
                else if (candidata.EstadoTemporal == EstadoCompatibilidad.Indeterminado)
                {
                    if (mejorProvisional == null || EsMejor(candidata, mejorProvisional))
                        mejorProvisional = candidata;
                }
                else
                {
                    foreach (var motivo in candidata.MotivosTemporales)
                        if (!rechazosTemporales.Contains(motivo))
                            rechazosTemporales.Add(motivo);
                }
            }
        }

        var mejor = mejorCompatible ?? mejorProvisional;
        resultado.InsercionSeleccionada = mejor;
        if (!hayInsercionGeografica)
        {
            resultado.Estado = EstadoCompatibilidad.Incompatible;
            motivos.Add(MotivoCompatibilidad.DesvioExcedido);
            motivos.Add(MotivoCompatibilidad.HorarioNoEvaluado);
        }
        else
        {
            motivos.Add(MotivoCompatibilidad.DesvioAprobado);
            resultado.Estado = mejor?.EstadoTemporal ?? EstadoCompatibilidad.Incompatible;
            // Si todas fallaron, los motivos pueden corresponder a distintas inserciones.
            motivos.AddRange(mejor?.MotivosTemporales ?? rechazosTemporales);
        }
        return resultado;
    }

    private static void EvaluarHorario(
        ViajeCompatibilidadDto viaje, EnvioCompatibilidadDto envio, InsercionCompatibilidadDto insercion)
    {
        if (insercion.Desvio.DuracionConEnvioSegundos < 0)
            throw new InvalidOperationException("El servicio de desvíos devolvió una duración total negativa.");

        var motivos = new List<MotivoCompatibilidad>();
        insercion.MotivosTemporales = motivos.AsReadOnly();
        if (!viaje.SalidaProgramada.HasValue)
        {
            motivos.Add(MotivoCompatibilidad.SalidaNoInformada);
            motivos.Add(MotivoCompatibilidad.HorarioNoEvaluado);
            return;
        }

        var salida = viaje.SalidaProgramada.Value;
        DateTimeOffset fin;
        try
        {
            // Suma exacta en ticks, sin redondeos de double ni convertir a una zona fija.
            fin = salida.AddTicks(checked(insercion.Desvio.DuracionConEnvioSegundos * TimeSpan.TicksPerSecond));
        }
        catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("La duración del recorrido excede el rango de fechas permitido.");
        }
        insercion.FinalizacionEstimada = fin;

        var incompatible = false;
        if (viaje.LlegadaMaxima.HasValue && fin > viaje.LlegadaMaxima.Value)
        {
            motivos.Add(MotivoCompatibilidad.LlegadaMaximaExcedida);
            incompatible = true;
        }
        if (envio.FechaLimiteEntrega.HasValue && fin > envio.FechaLimiteEntrega.Value)
            motivos.Add(MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable);

        if (envio.DisponibleDesde.HasValue)
        {
            if (envio.DisponibleDesde.Value > fin)
            {
                motivos.Add(MotivoCompatibilidad.DisponibilidadPosteriorAlRecorrido);
                incompatible = true;
            }
            else if (envio.DisponibleDesde.Value > salida)
            {
                // Incluye disponibilidad exactamente al finalizar: no se conoce el instante del retiro.
                motivos.Add(MotivoCompatibilidad.HorarioRetiroNoVerificable);
            }
        }

        if (incompatible)
            insercion.EstadoTemporal = EstadoCompatibilidad.Incompatible;
        else if (motivos.Count > 0)
            insercion.EstadoTemporal = EstadoCompatibilidad.Indeterminado;
        else
        {
            insercion.EstadoTemporal = EstadoCompatibilidad.Compatible;
            motivos.Add(MotivoCompatibilidad.HorarioAprobado);
        }
    }

    private static bool EsMejor(InsercionCompatibilidadDto candidata, InsercionCompatibilidadDto actual)
    {
        // Comparar, en vez de restar, evita desbordamientos con diferencias negativas.
        return (candidata.Desvio.DistanciaAdicionalMetros, candidata.Desvio.TiempoAdicionalSegundos,
                candidata.SegmentoRetiro, candidata.SegmentoEntrega)
            .CompareTo((actual.Desvio.DistanciaAdicionalMetros, actual.Desvio.TiempoAdicionalSegundos,
                actual.SegmentoRetiro, actual.SegmentoEntrega)) < 0;
    }

    private static void Validar(ViajeCompatibilidadDto viaje, EnvioCompatibilidadDto envio)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        ArgumentNullException.ThrowIfNull(envio);

        if (viaje.ViajeId <= 0 || envio.EnvioId <= 0)
            throw new ArgumentException("Los identificadores del viaje y del envío deben ser positivos.");

        if (viaje.RecorridoBase == null || viaje.RecorridoBase.Count < 2)
            throw new ArgumentException("El recorrido base debe incluir al menos origen y destino.", nameof(viaje));
        foreach (var punto in viaje.RecorridoBase)
            ValidarPunto(punto, nameof(viaje));
        ValidarPunto(envio.Retiro, nameof(envio));
        ValidarPunto(envio.Entrega, nameof(envio));

        if (envio.PesoKg <= 0)
            throw new ArgumentException("El peso del envío debe ser mayor que cero.", nameof(envio));
        if (viaje.PesoDisponibleKg < 0 || viaje.CuposDisponibles < 0)
            throw new ArgumentException("La capacidad conocida no puede ser negativa.", nameof(viaje));
        if (viaje.DesvioMaxKm < 0 || viaje.DesvioMaxKm > decimal.MaxValue / 1000m)
            throw new ArgumentException("El desvío máximo debe ser no negativo y convertible a metros.", nameof(viaje));
        if (!Enum.IsDefined(envio.Estado))
            throw new ArgumentException("El estado del envío no es válido.", nameof(envio));

        // Solo coherencia interna de ventanas, usando instantes con offset.
        // Las reglas cruzadas se evalúan fuera de esta validación; no se consulta la hora actual.
        if (viaje.SalidaProgramada.HasValue && viaje.LlegadaMaxima.HasValue &&
            viaje.LlegadaMaxima.Value < viaje.SalidaProgramada.Value)
            throw new ArgumentException("La llegada máxima no puede ser anterior a la salida.", nameof(viaje));
        if (envio.DisponibleDesde.HasValue && envio.FechaLimiteEntrega.HasValue &&
            envio.FechaLimiteEntrega.Value < envio.DisponibleDesde.Value)
            throw new ArgumentException("La fecha límite no puede ser anterior a la disponibilidad del envío.", nameof(envio));
    }

    private static void ValidarPunto(PuntoRutaDto punto, string parametro)
    {
        if (punto == null || punto.Latitud < -90 || punto.Latitud > 90 ||
            punto.Longitud < -180 || punto.Longitud > 180)
            throw new ArgumentException(
                "Cada punto debe tener latitud entre -90 y 90 y longitud entre -180 y 180.", parametro);
    }
}
