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
        ValidarSegmentos(rutaBase.Count, segmentoRetiro, segmentoEntrega);

        var evaluacion = await PrepararAsync(rutaBase, retiro, entrega, desvioMaxKm, cancellationToken);
        return await evaluacion.CalcularAsync(segmentoRetiro, segmentoEntrega, cancellationToken);
    }

    public async Task<IEvaluacionDesvios> PrepararAsync(
        IReadOnlyList<PuntoRutaDto> rutaBase,
        PuntoRutaDto retiro,
        PuntoRutaDto entrega,
        decimal desvioMaxKm,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(rutaBase);

        if (rutaBase.Count < 2)
            throw new ArgumentException(
                "La ruta base debe contener al menos origen y destino.", nameof(rutaBase));

        if (desvioMaxKm < 0 || desvioMaxKm > decimal.MaxValue / 1000m)
            throw new ArgumentOutOfRangeException(
                nameof(desvioMaxKm), "El límite debe ser no negativo y convertible a metros.");

        decimal desvioMaxMetros = desvioMaxKm * 1000m;
        var original = rutaBase.Select(p => ValidarYCopiar(p, nameof(rutaBase))).ToArray();
        var retiroValidado = ValidarYCopiar(retiro, nameof(retiro));
        var entregaValidada = ValidarYCopiar(entrega, nameof(entrega));

        var resultadoBase = await _rutasServicio.CalcularAsync(original, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ValidarResultado(resultadoBase);

        return new EvaluacionDesvios(_rutasServicio, original, retiroValidado, entregaValidada,
            desvioMaxMetros, resultadoBase.DistanciaMetros, resultadoBase.DuracionSegundos);
    }

    private static void ValidarSegmentos(int cantidadPuntos, int segmentoRetiro, int segmentoEntrega)
    {
        if (cantidadPuntos < 2)
            throw new ArgumentException("La ruta base debe contener al menos origen y destino.", "rutaBase");
        if (segmentoRetiro < 0 || segmentoRetiro >= cantidadPuntos - 1)
            throw new ArgumentOutOfRangeException(
                nameof(segmentoRetiro), "El segmento de retiro debe existir en la ruta base.");
        if (segmentoEntrega < 0 || segmentoEntrega >= cantidadPuntos - 1)
            throw new ArgumentOutOfRangeException(
                nameof(segmentoEntrega), "El segmento de entrega debe existir en la ruta base.");
        if (segmentoRetiro > segmentoEntrega)
            throw new ArgumentException(
                "El segmento de entrega no puede ser anterior al de retiro.", nameof(segmentoEntrega));
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

    private sealed class EvaluacionDesvios : IEvaluacionDesvios
    {
        private readonly IRutasServicio _rutasServicio;
        private readonly PuntoRutaDto[] _original;
        private readonly PuntoRutaDto _retiro;
        private readonly PuntoRutaDto _entrega;
        private readonly decimal _limiteMetros;
        private readonly long _distanciaBaseMetros;
        private readonly long _duracionBaseSegundos;

        public EvaluacionDesvios(IRutasServicio rutasServicio, PuntoRutaDto[] original,
            PuntoRutaDto retiro, PuntoRutaDto entrega, decimal limiteMetros,
            long distanciaBaseMetros, long duracionBaseSegundos)
        {
            _rutasServicio = rutasServicio;
            _original = original;
            _retiro = retiro;
            _entrega = entrega;
            _limiteMetros = limiteMetros;
            _distanciaBaseMetros = distanciaBaseMetros;
            _duracionBaseSegundos = duracionBaseSegundos;
        }

        public async Task<ResultadoDesvioDto> CalcularAsync(
            int segmentoRetiro, int segmentoEntrega, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidarSegmentos(_original.Length, segmentoRetiro, segmentoEntrega);

            // Único algoritmo de inserción, compartido por cálculo individual y evaluación preparada.
            var alternativa = new List<PuntoRutaDto>();
            for (var i = 0; i < _original.Length - 1; i++)
            {
                alternativa.Add(_original[i]);
                if (i == segmentoRetiro)
                    alternativa.Add(_retiro);
                if (i == segmentoEntrega)
                    alternativa.Add(_entrega);
            }
            alternativa.Add(_original[^1]);

            var conEnvio = await _rutasServicio.CalcularAsync(alternativa, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ValidarResultado(conEnvio);
            long distanciaAdicional = conEnvio.DistanciaMetros - _distanciaBaseMetros;
            long tiempoAdicional = conEnvio.DuracionSegundos - _duracionBaseSegundos;

            return new ResultadoDesvioDto
            {
                DistanciaOriginalMetros = _distanciaBaseMetros,
                DuracionOriginalSegundos = _duracionBaseSegundos,
                DistanciaConEnvioMetros = conEnvio.DistanciaMetros,
                DuracionConEnvioSegundos = conEnvio.DuracionSegundos,
                DistanciaAdicionalMetros = distanciaAdicional,
                TiempoAdicionalSegundos = tiempoAdicional,
                CumpleLimiteDesvio = distanciaAdicional <= _limiteMetros
            };
        }
    }
}
