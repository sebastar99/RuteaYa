using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Inserta retiro y entrega en los segmentos de la ruta base indicados por el llamador.
/// El segmento i une los puntos i e i + 1; los índices no cambian al insertar.
/// Dentro del mismo segmento, retiro precede a entrega. Conserva todos los puntos
/// originales en orden. No evalúa otras posiciones ni verifica cercanía geométrica
/// al segmento: el desvío corresponde únicamente a la inserción solicitada.
/// No agrega tiempos de carga o descarga a los del proveedor.
/// </summary>
public class DesviosServicio : IDesviosServicio
{
    private readonly IRutasServicio _rutasServicio;

    public DesviosServicio(IRutasServicio rutasServicio)
    {
        ArgumentNullException.ThrowIfNull(rutasServicio);
        _rutasServicio = rutasServicio;
    }

    public async Task<ResultadoDesvioDto> CalcularAsync(
        IReadOnlyList<PuntoRutaDto> rutaBase,
        PuntoRutaDto retiro,
        PuntoRutaDto entrega,
        decimal desvioMaxKm,
        int segmentoRetiro,
        int segmentoEntrega,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(rutaBase);

        if (rutaBase.Count < 2)
            throw new ArgumentException(
                "La ruta base debe contener al menos origen y destino.", nameof(rutaBase));

        if (segmentoRetiro < 0 || segmentoRetiro >= rutaBase.Count - 1)
            throw new ArgumentOutOfRangeException(
                nameof(segmentoRetiro), "El segmento de retiro debe existir en la ruta base.");

        if (segmentoEntrega < 0 || segmentoEntrega >= rutaBase.Count - 1)
            throw new ArgumentOutOfRangeException(
                nameof(segmentoEntrega), "El segmento de entrega debe existir en la ruta base.");

        if (segmentoRetiro > segmentoEntrega)
            throw new ArgumentException(
                "El segmento de entrega no puede ser anterior al de retiro.", nameof(segmentoEntrega));

        if (desvioMaxKm < 0 || desvioMaxKm > decimal.MaxValue / 1000m)
            throw new ArgumentOutOfRangeException(
                nameof(desvioMaxKm), "El límite debe ser no negativo y convertible a metros.");

        decimal desvioMaxMetros = desvioMaxKm * 1000m;
        var original = rutaBase.Select(p => ValidarYCopiar(p, nameof(rutaBase))).ToArray();
        var retiroValidado = ValidarYCopiar(retiro, nameof(retiro));
        var entregaValidada = ValidarYCopiar(entrega, nameof(entrega));

        var alternativa = new List<PuntoRutaDto>();
        for (var i = 0; i < original.Length - 1; i++)
        {
            alternativa.Add(original[i]);
            if (i == segmentoRetiro)
                alternativa.Add(retiroValidado);
            if (i == segmentoEntrega)
                alternativa.Add(entregaValidada);
        }
        alternativa.Add(original[^1]);

        var resultadoBase = await _rutasServicio.CalcularAsync(original, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidarResultado(resultadoBase);

        var resultadoConEnvio = await _rutasServicio.CalcularAsync(alternativa, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidarResultado(resultadoConEnvio);

        long distanciaAdicional = resultadoConEnvio.DistanciaMetros - resultadoBase.DistanciaMetros;
        long tiempoAdicional = resultadoConEnvio.DuracionSegundos - resultadoBase.DuracionSegundos;

        return new ResultadoDesvioDto
        {
            DistanciaOriginalMetros = resultadoBase.DistanciaMetros,
            DuracionOriginalSegundos = resultadoBase.DuracionSegundos,
            DistanciaConEnvioMetros = resultadoConEnvio.DistanciaMetros,
            DuracionConEnvioSegundos = resultadoConEnvio.DuracionSegundos,
            DistanciaAdicionalMetros = distanciaAdicional,
            TiempoAdicionalSegundos = tiempoAdicional,
            CumpleLimiteDesvio = distanciaAdicional <= desvioMaxMetros
        };
    }

    private static PuntoRutaDto ValidarYCopiar(PuntoRutaDto punto, string parametro)
    {
        ArgumentNullException.ThrowIfNull(punto, parametro);

        if (punto.Latitud < -90 || punto.Latitud > 90 ||
            punto.Longitud < -180 || punto.Longitud > 180)
            throw new ArgumentException(
                "La latitud debe estar entre -90 y 90 y la longitud entre -180 y 180.", parametro);

        return new PuntoRutaDto { Latitud = punto.Latitud, Longitud = punto.Longitud };
    }

    private static void ValidarResultado(ResultadoRutaDto resultado)
    {
        if (resultado == null || resultado.DistanciaMetros < 0 || resultado.DuracionSegundos < 0)
            throw new InvalidOperationException(
                "El servicio de rutas debe devolver distancia y duración totales no negativas.");
    }
}
