using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Reglas previas y búsqueda de inserción para un solo envío sobre datos en memoria.
/// Un envío ocupa un cupo y su peso durante toda la salida. Delega cada alternativa
/// a IDesviosServicio, sin construir rutas ni evaluar horarios. No combina envíos.
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
        InsercionCompatibilidadDto? mejor = null;

        // Todas las parejas i <= j; el orden ascendente también hace determinista el recorrido.
        // No se corta al hallar una válida: otra puede tener menor distancia o tiempo adicional.
        for (var retiro = 0; retiro < cantidadSegmentos; retiro++)
        {
            for (var entrega = retiro; entrega < cantidadSegmentos; entrega++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var desvio = await _desviosServicio.CalcularAsync(
                    viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm,
                    retiro, entrega, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (desvio == null)
                    throw new InvalidOperationException("El servicio de desvíos no devolvió un resultado.");

                if (desvio.DistanciaAdicionalMetros > limiteMetros)
                    continue;

                var candidata = new InsercionCompatibilidadDto
                {
                    SegmentoRetiro = retiro,
                    SegmentoEntrega = entrega,
                    Desvio = desvio
                };
                if (mejor == null || EsMejor(candidata, mejor))
                    mejor = candidata;
            }
        }

        resultado.InsercionSeleccionada = mejor;
        if (mejor == null)
        {
            resultado.Estado = EstadoCompatibilidad.Incompatible;
            motivos.Add(MotivoCompatibilidad.DesvioExcedido);
        }
        else
        {
            motivos.Add(MotivoCompatibilidad.DesvioAprobado);
        }
        motivos.Add(MotivoCompatibilidad.HorarioNoEvaluado);
        return resultado;
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
        // No se compara con la hora actual ni entre viaje y envío en esta etapa.
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
