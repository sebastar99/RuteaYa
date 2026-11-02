using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Fuente del servidor para los datos confirmados que no están representados
/// en las entidades actuales. Se consulta después de verificar propiedad y estado.
/// No se vincula a parámetros HTTP. Debe referirse a la misma versión del viaje/envío;
/// el recorrido confirmado incluye origen, todas las paradas en orden y destino.
/// </summary>
public interface IDatosConfirmadosOportunidadServicio
{
    Task<DatosConfirmadosOportunidadDto?> ObtenerAsync(
        ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default);
}
