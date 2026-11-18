using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Adaptación conservadora de los contratos actuales. No hay persistencia de
/// confirmaciones, salidas o reservas: no se fabrican a partir de direcciones,
/// coordenadas numéricas, horarios recurrentes o capacidades máximas.
/// </summary>
public class DatosConfirmadosOportunidadActualesServicio : IDatosConfirmadosOportunidadServicio
{
    public Task<DatosConfirmadosOportunidadDto?> ObtenerAsync(
        ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(viaje);
        ArgumentNullException.ThrowIfNull(envio);
        var datos = new DatosConfirmadosOportunidadDto { ViajeId = viaje.Id, EnvioId = envio.Id };

        // Solo UTC ya explícito identifica un instante sin elegir la zona del servidor.
        // datetime2 leído de SQL normalmente es Unspecified: permanece Desconocida.
        if (envio.FechaLimite != default && envio.FechaLimite.Kind == DateTimeKind.Utc)
            datos.FechaLimiteEntrega = RestriccionTemporalDto.Informada(new DateTimeOffset(envio.FechaLimite));
        return Task.FromResult<DatosConfirmadosOportunidadDto?>(datos);
    }
}
