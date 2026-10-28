using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public class RutasMockServicio : IRutasServicio
{
    private readonly List<(
        (decimal Latitud, decimal Longitud)[] Puntos,
        long DistanciaMetros,
        long DuracionSegundos)> _recorridos = new();

    /// <summary>
    /// Configura el resultado de una secuencia exacta de coordenadas.
    /// Si ya existe, reemplaza su resultado. Se copian los datos recibidos.
    /// </summary>
    public void ConfigurarRecorrido(
        IReadOnlyList<PuntoRutaDto> puntos,
        ResultadoRutaDto resultado)
    {
        var coordenadas = ValidarYCopiar(puntos);
        ArgumentNullException.ThrowIfNull(resultado);

        if (resultado.DistanciaMetros < 0 || resultado.DuracionSegundos < 0)
            throw new ArgumentException(
                "La distancia en metros y la duración en segundos no pueden ser negativas.",
                nameof(resultado));

        var indice = _recorridos.FindIndex(r => r.Puntos.SequenceEqual(coordenadas));
        var recorrido = (coordenadas, resultado.DistanciaMetros, resultado.DuracionSegundos);

        if (indice >= 0)
            _recorridos[indice] = recorrido;
        else
            _recorridos.Add(recorrido);
    }

    public Task<ResultadoRutaDto> CalcularAsync(
        IReadOnlyList<PuntoRutaDto> puntos,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var coordenadas = ValidarYCopiar(puntos);
        var indice = _recorridos.FindIndex(r => r.Puntos.SequenceEqual(coordenadas));

        if (indice < 0)
            throw new InvalidOperationException(
                "No hay un recorrido configurado para esas coordenadas en ese orden.");

        var recorrido = _recorridos[indice];
        return Task.FromResult(new ResultadoRutaDto
        {
            DistanciaMetros = recorrido.DistanciaMetros,
            DuracionSegundos = recorrido.DuracionSegundos
        });
    }

    private static (decimal Latitud, decimal Longitud)[] ValidarYCopiar(
        IReadOnlyList<PuntoRutaDto> puntos)
    {
        ArgumentNullException.ThrowIfNull(puntos);

        if (puntos.Count < 2)
            throw new ArgumentException(
                "El recorrido debe contener al menos dos puntos: origen y destino.",
                nameof(puntos));

        var coordenadas = new (decimal Latitud, decimal Longitud)[puntos.Count];

        for (var i = 0; i < puntos.Count; i++)
        {
            var punto = puntos[i];
            if (punto == null)
                throw new ArgumentException(
                    $"El punto en la posición {i} no puede ser nulo.", nameof(puntos));

            if (punto.Latitud < -90 || punto.Latitud > 90 ||
                punto.Longitud < -180 || punto.Longitud > 180)
                throw new ArgumentException(
                    $"El punto en la posición {i} debe tener latitud entre -90 y 90 y longitud entre -180 y 180.",
                    nameof(puntos));

            coordenadas[i] = (punto.Latitud, punto.Longitud);
        }

        return coordenadas;
    }
}
