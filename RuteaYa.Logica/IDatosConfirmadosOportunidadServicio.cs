using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Fuente del servidor para ubicaciones y datos confirmados. Se consulta después de verificar propiedad y estado.
/// No se vincula a parámetros HTTP. Debe referirse a la misma versión del viaje/envío;
/// el recorrido confirmado incluye origen, todas las paradas en orden y destino.
/// </summary>
public interface IDatosConfirmadosOportunidadServicio
{
    Task<DatosConfirmadosOportunidadDto?> ObtenerAsync(
        ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<int, DatosConfirmadosOportunidadDto?>> ObtenerLoteAsync(ViajeOportunidadesDto viaje,
        IReadOnlyList<EnvioDto> envios, CancellationToken ct = default)
    {
        var resultado = new Dictionary<int, DatosConfirmadosOportunidadDto?>();
        foreach (var envio in envios) resultado.Add(envio.Id, await ObtenerAsync(viaje, envio, ct));
        return resultado;
    }
}
