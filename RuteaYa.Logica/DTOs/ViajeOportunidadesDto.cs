namespace RuteaYa.Logica.DTOs;

/// <summary>Proyección mínima de un viaje para el selector y encabezado de candidatos.</summary>
public class ViajeOportunidadesDto
{
    public int Id { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public bool Activo { get; set; }
}
