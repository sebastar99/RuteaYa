using RuteaYa.Entidades;
using System.Security.Cryptography;
using System.Text;

namespace RuteaYa.Logica.DTOs;

public class CampoViajeUbicacionDto
{
    public ContextoUbicacionDto Contexto { get; set; } = null!;
    public string? Comprobante { get; set; }
    // Solo se admite sin comprobante al conservar exactamente un dato existente.
    public string Texto { get; set; } = "";
    public int IdParada { get; set; }
}

public class UbicacionesViajeDto
{
    public CampoViajeUbicacionDto Origen { get; set; } = null!;
    public CampoViajeUbicacionDto Destino { get; set; } = null!;
    public List<CampoViajeUbicacionDto> Paradas { get; set; } = new();
}

public class SolicitudContextoViajeDto
{
    public int? ParadaId { get; set; }
    public int? ViajeId { get; set; }
    public Guid? FormularioId { get; set; }
    public CampoUbicacion Campo { get; set; }
    public string ClaveCampo { get; set; } = "";
}

public static class IdentidadParadaFormulario
{
    public static string Existente(Guid formulario, int paradaId) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"parada:{formulario:N}:{paradaId}"))[..16]).ToString("N");
}
