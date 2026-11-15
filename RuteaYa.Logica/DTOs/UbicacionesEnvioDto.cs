namespace RuteaYa.Logica.DTOs;

public class UbicacionesEnvioDto
{
    public ContextoUbicacionDto Retiro { get; set; } = null!;
    public ContextoUbicacionDto Entrega { get; set; } = null!;
    public string ComprobanteRetiro { get; set; } = "";
    public string ComprobanteEntrega { get; set; } = "";
}
