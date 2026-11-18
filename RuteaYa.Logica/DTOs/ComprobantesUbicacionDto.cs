using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RuteaYa.Entidades;

namespace RuteaYa.Logica.DTOs;

/// <summary>Contexto construido por servidor, no una autorización enviada libremente por el cliente.</summary>
public sealed record ContextoUbicacionDto(Guid FormularioId, TipoRecursoUbicacion TipoRecurso,
    int? RecursoId, CampoUbicacion Campo, string ClaveCampo, string? RevisionRecurso)
{
    [JsonIgnore]
    public string AmbitoSeleccion => "persistencia:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));
}

public sealed record SeleccionUbicacionVerificadaDto(ResultadoGeocodificacionDto Ubicacion, int AccesoElegido,
    DateTimeOffset ConfirmadaEnUtc, DateTimeOffset VenceEnUtc);

public sealed record ComprobanteUbicacionDto(string Token, DateTimeOffset VenceEnUtc);

/// <summary>Comando interno de unidad de trabajo. Los destinos son entidades propias ya cargadas/agregadas en el DbContext.</summary>
public sealed record AsignacionUbicacionDto(ContextoUbicacionDto Contexto, string Token,
    Viaje? Viaje = null, Parada? Parada = null, Envio? Envio = null);
