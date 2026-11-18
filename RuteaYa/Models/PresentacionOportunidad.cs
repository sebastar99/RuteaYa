using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public enum EstadoVisualOportunidad { Compatible, NoCompatible, Pendiente }

public enum ResultadoCondicion { Cumple, NoCumple, Pendiente }

public sealed record CondicionOportunidad(string Nombre, ResultadoCondicion Resultado, string Detalle);

public sealed record MotivoNoAceptacion(string Motivo, string? Solucion);

/// <summary>
/// Traduce la evaluación del motor a mensajes breves para el conductor. No decide compatibilidad:
/// solo muestra el resultado real de cada validación y los datos que faltan.
/// </summary>
public static class PresentacionOportunidad
{
    public static EstadoVisualOportunidad Estado(EvaluacionOportunidadDto? evaluacion) => evaluacion == null || !evaluacion.MotorEjecutado
        ? EstadoVisualOportunidad.Pendiente
        : evaluacion.Estado switch
        {
            EstadoCompatibilidad.Compatible => EstadoVisualOportunidad.Compatible,
            EstadoCompatibilidad.Incompatible => EstadoVisualOportunidad.NoCompatible,
            _ => EstadoVisualOportunidad.Pendiente
        };

    public static string Etiqueta(EstadoVisualOportunidad estado) => estado switch
    {
        EstadoVisualOportunidad.Compatible => "Compatible",
        EstadoVisualOportunidad.NoCompatible => "No compatible",
        _ => "Pendiente de evaluación"
    };

    public static string Color(EstadoVisualOportunidad estado) => estado switch
    {
        EstadoVisualOportunidad.Compatible => "green",
        EstadoVisualOportunidad.NoCompatible => "red",
        _ => "orange"
    };

    /// <summary>Kilómetros y minutos adicionales de la mejor inserción, si el motor llegó a calcularla.</summary>
    public static (decimal Km, decimal Minutos)? Adicional(EvaluacionOportunidadDto? evaluacion)
    {
        var desvio = evaluacion?.ResultadoMotor?.InsercionSeleccionada?.Desvio;
        return desvio == null ? null : (Math.Round(desvio.DistanciaAdicionalMetros / 1000m, 1, MidpointRounding.AwayFromZero), Math.Round(desvio.TiempoAdicionalSegundos / 60m, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Un valor negativo es real (el proveedor estimó un recorrido más corto) y se muestra con su signo.</summary>
    public static string Firmado(decimal valor, string formato) => valor > 0 ? "+" + valor.ToString(formato) : valor.ToString(formato);

    public static string Resumen(EvaluacionOportunidadDto? evaluacion)
    {
        var estado = Estado(evaluacion);
        if (estado == EstadoVisualOportunidad.Compatible) return "Entra en tu recorrido.";
        var rechazo = evaluacion == null ? null : Rechazo(evaluacion);
        if (estado == EstadoVisualOportunidad.NoCompatible) return rechazo?.Motivo ?? "No cumple las condiciones de tu viaje.";
        return (evaluacion == null ? null : Pendiente(evaluacion)?.Motivo) ?? "Todavía no se puede evaluar.";
    }

    public static IReadOnlyList<CondicionOportunidad> Condiciones(DetalleCandidatoDto detalle)
    {
        var e = detalle.Evaluacion; var viaje = detalle.Viaje; var envio = detalle.Envio; var salida = viaje.SalidaSeleccionada;
        var lista = new List<CondicionOportunidad>();

        if (!viaje.Activo) lista.Add(new("Disponibilidad", ResultadoCondicion.NoCumple, "Tu viaje está inactivo."));
        else if (salida == null) lista.Add(new("Disponibilidad", ResultadoCondicion.Pendiente, "Elegí la fecha de salida de tu viaje."));
        else if (!salida.Disponible) lista.Add(new("Disponibilidad", ResultadoCondicion.NoCumple, "La salida ya no está disponible."));
        else if (Motivo(e, MotivoCompatibilidad.EnvioNoDisponible)) lista.Add(new("Disponibilidad", ResultadoCondicion.NoCumple, "El envío ya no está disponible."));
        else lista.Add(new("Disponibilidad", ResultadoCondicion.Cumple, "Tu salida y el envío están disponibles."));

        var peso = $"{envio.PesoKg:0.##} kg";
        if (salida == null) lista.Add(new("Peso permitido", ResultadoCondicion.Pendiente, "Se evalúa al elegir la salida."));
        else if (Motivo(e, MotivoCompatibilidad.PesoExcedido, MotivoCompatibilidad.PesoMaximoPublicadoExcedido) ||
            salida.PesoDisponibleKg.HasValue && envio.PesoKg > salida.PesoDisponibleKg)
            lista.Add(new("Peso permitido", ResultadoCondicion.NoCumple,
                salida.PesoDisponibleKg.HasValue ? $"El paquete pesa {peso} y te quedan {salida.PesoDisponibleKg:0.##} kg." : $"El paquete pesa {peso} y supera el máximo de tu viaje."));
        else if (!salida.PesoDisponibleKg.HasValue) lista.Add(new("Peso permitido", ResultadoCondicion.Pendiente, "No se puede asegurar el peso que te queda."));
        else lista.Add(new("Peso permitido", ResultadoCondicion.Cumple, $"El paquete pesa {peso} y te quedan {salida.PesoDisponibleKg:0.##} kg."));

        if (salida == null) lista.Add(new("Cupos disponibles", ResultadoCondicion.Pendiente, "Se evalúa al elegir la salida."));
        else if (Motivo(e, MotivoCompatibilidad.SinCupo) || salida.CuposDisponibles == 0)
            lista.Add(new("Cupos disponibles", ResultadoCondicion.NoCumple, "No te quedan cupos en esta salida."));
        else if (!salida.CuposDisponibles.HasValue) lista.Add(new("Cupos disponibles", ResultadoCondicion.Pendiente, "No se puede asegurar cuántos cupos te quedan."));
        else lista.Add(new("Cupos disponibles", ResultadoCondicion.Cumple, salida.CuposDisponibles == 1 ? "Te queda 1 cupo." : $"Te quedan {salida.CuposDisponibles} cupos."));

        var adicional = Adicional(e);
        if (Motivo(e, MotivoCompatibilidad.DesvioExcedido))
            lista.Add(new("Desvío permitido", ResultadoCondicion.NoCumple, $"Supera el desvío máximo de {viaje.DesvioMaxKm:0.##} km."));
        else if (Motivo(e, MotivoCompatibilidad.DesvioAprobado) && adicional.HasValue)
            lista.Add(new("Desvío permitido", ResultadoCondicion.Cumple, $"Cambia tu recorrido en {Firmado(adicional.Value.Km, "0.#")} km y {Firmado(adicional.Value.Minutos, "0")} min; tu máximo es {viaje.DesvioMaxKm:0.##} km."));
        else lista.Add(new("Desvío permitido", ResultadoCondicion.Pendiente, "Todavía no se calculó el desvío."));

        if (Motivo(e, MotivoCompatibilidad.SalidaPosteriorAlVencimiento))
            lista.Add(new("Horarios compatibles", ResultadoCondicion.NoCumple, "Tu salida es posterior a la fecha límite del envío."));
        else if (Motivo(e, MotivoCompatibilidad.LlegadaMaximaExcedida))
            lista.Add(new("Horarios compatibles", ResultadoCondicion.NoCumple, "Llegarías después de tu horario máximo."));
        else if (Motivo(e, MotivoCompatibilidad.DisponibilidadPosteriorAlRecorrido))
            lista.Add(new("Horarios compatibles", ResultadoCondicion.NoCumple, "El paquete estaría disponible cuando tu recorrido ya terminó."));
        else if (Motivo(e, MotivoCompatibilidad.HorarioAprobado))
            lista.Add(new("Horarios compatibles", ResultadoCondicion.Cumple, "Los horarios del viaje y del envío son compatibles."));
        else lista.Add(new("Horarios compatibles", ResultadoCondicion.Pendiente, Pendiente(e)?.Motivo ?? "Todavía no se pudieron verificar los horarios."));
        return lista;
    }

    /// <summary>Null si el envío se puede aceptar; si no, el motivo concreto y cómo resolverlo.</summary>
    public static MotivoNoAceptacion? NoSePuedeAceptar(DetalleCandidatoDto detalle)
    {
        var viaje = detalle.Viaje; var salida = viaje.SalidaSeleccionada; var e = detalle.Evaluacion;
        if (!viaje.Activo) return new("Tu viaje está inactivo.", "Elegí otro de tus viajes que esté activo.");
        if (salida == null) return new("Todavía no elegiste la salida de tu viaje.", "Volvé a Oportunidades y elegí la fecha de salida para evaluar este envío.");
        if (!salida.Disponible) return new("Esta salida ya no está disponible.", "Elegí otra fecha de salida de tu viaje.");
        if (Estado(e) == EstadoVisualOportunidad.Compatible && e.DatosPendientes.Count == 0) return null;
        return Rechazo(e) ?? Pendiente(e) ?? new("Todavía no se puede confirmar la compatibilidad.", "Volvé a intentarlo en unos minutos.");
    }

    /// <summary>Motivo concreto por el que una evaluación no alcanza para aceptar (rechazo comprobado o dato pendiente).</summary>
    public static MotivoNoAceptacion? Razon(EvaluacionOportunidadDto e) => Rechazo(e) ?? Pendiente(e);

    private static bool Motivo(EvaluacionOportunidadDto e, params MotivoCompatibilidad[] motivos) =>
        e.ResultadoMotor != null && motivos.Any(e.ResultadoMotor.Motivos.Contains);

    private static bool Dato(EvaluacionOportunidadDto e, params DatoPendienteOportunidad[] datos) => datos.Any(e.DatosPendientes.Contains);

    private static MotivoNoAceptacion? Rechazo(EvaluacionOportunidadDto e)
    {
        if (Motivo(e, MotivoCompatibilidad.ViajeInactivo)) return new("Tu viaje está inactivo.", "Elegí otro de tus viajes que esté activo.");
        if (Motivo(e, MotivoCompatibilidad.ViajeNoDisponible)) return new("Esta salida ya no está disponible.", "Elegí otra fecha de salida de tu viaje.");
        if (Motivo(e, MotivoCompatibilidad.EnvioNoDisponible)) return new("El envío ya no está disponible.", "Volvé al listado y elegí otro envío.");
        if (Motivo(e, MotivoCompatibilidad.PesoExcedido, MotivoCompatibilidad.PesoMaximoPublicadoExcedido))
            return new("El paquete supera el peso que te queda.", "Elegí otra salida con más lugar u otro envío más liviano.");
        if (Motivo(e, MotivoCompatibilidad.SinCupo)) return new("No te quedan cupos en esta salida.", "Elegí otra fecha de salida u otro viaje.");
        if (Motivo(e, MotivoCompatibilidad.DesvioExcedido))
            return new("Se desvía más de lo que permite tu viaje.", "Podés subir el desvío máximo editando tu viaje, o elegir otro envío.");
        if (Motivo(e, MotivoCompatibilidad.SalidaPosteriorAlVencimiento))
            return new("Tu salida es posterior a la fecha límite del envío.", "Elegí una fecha de salida anterior a la fecha límite.");
        if (Motivo(e, MotivoCompatibilidad.LlegadaMaximaExcedida))
            return new("Llegarías después de tu horario máximo.", "Elegí una salida sin horario límite o con un límite más tarde.");
        if (Motivo(e, MotivoCompatibilidad.DisponibilidadPosteriorAlRecorrido))
            return new("El paquete estaría disponible cuando tu recorrido ya terminó.", "Elegí una salida más tarde.");
        return e.MotorEjecutado && e.Estado == EstadoCompatibilidad.Incompatible
            ? new("No cumple las condiciones de tu viaje.", "Elegí otro envío o revisá los datos de tu viaje.") : null;
    }

    private static MotivoNoAceptacion? Pendiente(EvaluacionOportunidadDto e)
    {
        if (Dato(e, DatoPendienteOportunidad.SalidaConcretaDesconocida, DatoPendienteOportunidad.DisponibilidadSalidaDesconocida))
            return new("Elegí la fecha de salida para evaluarlo.", "Volvé a Oportunidades y elegí la fecha de salida de tu viaje.");
        if (Dato(e, DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas))
            return new("Faltan direcciones confirmadas en tu viaje.", "Editá tu viaje y elegí origen, destino y paradas de la lista de sugerencias.");
        if (Dato(e, DatoPendienteOportunidad.RetiroSinCoordenadasConfirmadas, DatoPendienteOportunidad.EntregaSinCoordenadasConfirmadas))
            return new("Faltan direcciones confirmadas en el envío.", "El remitente debe volver a publicar el envío eligiendo las direcciones de la lista.");
        if (Dato(e, DatoPendienteOportunidad.RecorridoConCompromisosNoVerificable))
            return new("Esta salida ya tiene paquetes asignados.", "Todavía no se pueden combinar varios paquetes en una salida: probá con otra fecha.");
        if (Dato(e, DatoPendienteOportunidad.InicioRetiroDesconocido))
            return new("Falta saber desde cuándo se puede retirar el paquete.", "El remitente debe indicar el horario de retiro o marcar que no tiene restricción.");
        if (Dato(e, DatoPendienteOportunidad.VencimientoEntregaNoInterpretado))
            return new("No se pudo interpretar la fecha límite del envío.", "Solo se pueden interpretar fechas de envíos con direcciones de Argentina elegidas de la lista.");
        if (Dato(e, DatoPendienteOportunidad.LlegadaMaximaDesconocida))
            return new("Falta tu horario máximo de llegada.", "Elegí la fecha de salida indicando un horario máximo o sin límite.");
        if (Dato(e, DatoPendienteOportunidad.PesoDisponibleDesconocido, DatoPendienteOportunidad.CuposDisponiblesDesconocidos, DatoPendienteOportunidad.CapacidadRestanteInvalida))
            return new("No se puede asegurar el lugar que te queda en la salida.", "Revisá tus paquetes asignados en Mis transportes.");
        if (Dato(e, DatoPendienteOportunidad.PesoEnvioInvalido, DatoPendienteOportunidad.DesvioMaximoInvalido, DatoPendienteOportunidad.RestriccionesTemporalesInconsistentes))
            return new("Hay datos inválidos en el viaje o en el envío.", "Revisá los datos de tu viaje; si son correctos, el envío debe publicarse de nuevo.");
        if (Motivo(e, MotivoCompatibilidad.HorarioRetiroNoVerificable))
            return new("No se puede confirmar el horario de retiro con tu recorrido.", "Elegí una salida que comience después de que el paquete esté disponible.");
        if (Motivo(e, MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable))
            return new("No se puede asegurar la entrega antes de la fecha límite.", "Elegí una salida más temprana.");
        if (Dato(e, DatoPendienteOportunidad.FuenteDeDatosNoDisponible))
            return new("No se pudieron cargar los datos del envío.", "Volvé a intentarlo en unos minutos.");
        return e.Estado == EstadoCompatibilidad.Indeterminado ? new("Todavía no se puede confirmar la compatibilidad.", "Volvé a intentarlo en unos minutos.") : null;
    }
}
