using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Reglas previas sobre datos en memoria. Un envío ocupa un cupo y su peso durante
/// toda la salida. No consulta rutas, calcula inserciones ni valida tiempos de viaje.
/// </summary>
public class CompatibilidadServicio : ICompatibilidadServicio
{
    public Task<ResultadoCompatibilidadDto> EvaluarAsync(
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

        motivos.Add(MotivoCompatibilidad.DesvioNoEvaluado);
        motivos.Add(MotivoCompatibilidad.HorarioNoEvaluado);

        return Task.FromResult(new ResultadoCompatibilidadDto
        {
            ViajeId = viaje.ViajeId,
            EnvioId = envio.EnvioId,
            Estado = incompatible ? EstadoCompatibilidad.Incompatible : EstadoCompatibilidad.Indeterminado,
            Motivos = motivos.AsReadOnly()
        });
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
