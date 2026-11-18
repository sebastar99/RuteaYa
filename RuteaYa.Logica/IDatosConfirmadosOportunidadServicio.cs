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

    /// <summary>Paquetes ya asignados a la salida con sus puntos y restricciones verificadas. Null cuando alguno no puede verificarse.</summary>
    Task<IReadOnlyList<PaqueteComprometidoDto>?> ObtenerComprometidosAsync(int viajeId, int salidaId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PaqueteComprometidoDto>?>(null);

    /// <summary>Recorrido original del viaje (origen, paradas en orden, destino). Null cuando no puede verificarse completo.</summary>
    Task<RecorridoConfirmadoDto?> ObtenerRecorridoBaseAsync(int usuarioId, int viajeId, CancellationToken ct = default) =>
        Task.FromResult<RecorridoConfirmadoDto?>(null);
}
