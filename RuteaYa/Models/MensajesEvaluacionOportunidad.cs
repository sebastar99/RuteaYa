using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public static class MensajesEvaluacionOportunidad
{
    public static string DatoPendiente(DatoPendienteOportunidad dato) => dato switch
    {
        DatoPendienteOportunidad.FuenteDeDatosNoDisponible => "No está disponible la fuente de datos confirmados.",
        DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas => "Falta el recorrido completo con coordenadas válidas y confirmadas, incluidas sus paradas ordenadas.",
        DatoPendienteOportunidad.RetiroSinCoordenadasConfirmadas => "Faltan coordenadas válidas y confirmadas del retiro.",
        DatoPendienteOportunidad.EntregaSinCoordenadasConfirmadas => "Faltan coordenadas válidas y confirmadas de la entrega.",
        DatoPendienteOportunidad.SalidaConcretaDesconocida => "Falta una salida concreta con fecha, hora y offset conocidos.",
        DatoPendienteOportunidad.DisponibilidadSalidaDesconocida => "No se conoce la disponibilidad de esa salida; un viaje activo no la confirma.",
        DatoPendienteOportunidad.PesoDisponibleDesconocido => "Falta el peso restante real de la salida; no se usa el peso máximo como capacidad libre.",
        DatoPendienteOportunidad.CuposDisponiblesDesconocidos => "Falta la cantidad de cupos restantes de la salida.",
        DatoPendienteOportunidad.LlegadaMaximaDesconocida => "No se conoce el límite de llegada ni se confirmó que no aplique.",
        DatoPendienteOportunidad.InicioRetiroDesconocido => "No se conoce el inicio de disponibilidad para retirar ni se confirmó que no tenga restricción.",
        DatoPendienteOportunidad.VencimientoEntregaNoInterpretado => "El vencimiento obligatorio del envío todavía no está interpretado como un instante concreto.",
        DatoPendienteOportunidad.CapacidadRestanteInvalida => "La capacidad restante informada es inválida y debe revisarse.",
        DatoPendienteOportunidad.PesoEnvioInvalido => "El peso publicado del envío es inválido y debe revisarse.",
        DatoPendienteOportunidad.DesvioMaximoInvalido => "El límite de desvío del viaje es inválido y debe revisarse.",
        DatoPendienteOportunidad.RestriccionesTemporalesInconsistentes => "Las fechas confirmadas son inconsistentes entre sí o con la publicación; deben revisarse.",
        DatoPendienteOportunidad.RecorridoConCompromisosNoVerificable => "La salida ya tiene paquetes comprometidos y no hay un recorrido combinado confirmado. No se puede aceptar otro paquete con estos datos.",
        _ => "Faltan datos válidos y confirmados para evaluar."
    };

    public static string Motivo(MotivoCompatibilidad motivo) => motivo switch
    {
        MotivoCompatibilidad.ViajeInactivo => "El viaje está inactivo.",
        MotivoCompatibilidad.ViajeNoDisponible => "La salida no está disponible.",
        MotivoCompatibilidad.EnvioNoDisponible => "El envío no está disponible.",
        MotivoCompatibilidad.PesoExcedido => "El paquete supera el peso disponible.",
        MotivoCompatibilidad.SinCupo => "No quedan cupos para paquetes.",
        MotivoCompatibilidad.PesoDisponibleDesconocido => "No se conoce el peso disponible.",
        MotivoCompatibilidad.CuposDisponiblesDesconocidos => "No se conocen los cupos restantes.",
        MotivoCompatibilidad.DesvioNoEvaluado => "No se evaluó el desvío.",
        MotivoCompatibilidad.HorarioNoEvaluado => "No se evaluaron los horarios de las inserciones.",
        MotivoCompatibilidad.DesvioExcedido => "Ninguna inserción cumple el límite de distancia adicional.",
        MotivoCompatibilidad.DesvioAprobado => "Hay una inserción que cumple el límite de distancia adicional.",
        MotivoCompatibilidad.SalidaNoInformada => "No se informó una salida concreta.",
        MotivoCompatibilidad.SalidaPosteriorAlVencimiento => "La salida es posterior al vencimiento del envío.",
        MotivoCompatibilidad.LlegadaMaximaExcedida => "La finalización estimada supera la llegada máxima del viaje.",
        MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable => "No se puede confirmar la entrega antes del vencimiento con la duración total disponible.",
        MotivoCompatibilidad.DisponibilidadPosteriorAlRecorrido => "El retiro estaría disponible después de finalizar el recorrido.",
        MotivoCompatibilidad.HorarioRetiroNoVerificable => "Falta el instante de retiro para verificar su disponibilidad durante el recorrido.",
        MotivoCompatibilidad.HorarioAprobado => "Se verificaron las restricciones temporales informadas bajo la estimación calculada.",
        MotivoCompatibilidad.LlegadaMaximaDesconocida => "El límite de llegada es desconocido.",
        MotivoCompatibilidad.DisponibilidadRetiroDesconocida => "La disponibilidad temporal del retiro es desconocida.",
        MotivoCompatibilidad.VencimientoEntregaDesconocido => "El vencimiento de entrega es desconocido.",
        MotivoCompatibilidad.DisponibilidadSalidaDesconocida => "No se conoce la disponibilidad de una salida concreta.",
        MotivoCompatibilidad.PesoMaximoPublicadoExcedido => "El paquete supera incluso el peso máximo publicado del viaje.",
        _ => "La evaluación requiere revisar sus condiciones."
    };
}
