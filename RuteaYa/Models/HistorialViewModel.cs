
using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Web.Models;

public sealed class HistorialViewModel
{
    public string Buscar { get; set; } = string.Empty;

    public string Tipo { get; set; } = "Todos";

    public string Estado { get; set; } = "Todos";

    [DataType(DataType.Date)]
    public DateTime? Desde { get; set; }

    [DataType(DataType.Date)]
    public DateTime? Hasta { get; set; }

    public int TotalViajes { get; set; }

    public int TotalEnvios { get; set; }

    public int EnviosEntregados { get; set; }

    public decimal MontoEnviosEntregados { get; set; }

    public List<HistorialOperacionViewModel> Operaciones { get; set; } = new();
}

public sealed class HistorialOperacionViewModel
{
    public int Id { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public DateTime Fecha { get; set; }

    public string Detalle { get; set; } = string.Empty;

    public string Informacion { get; set; } = string.Empty;

    public string Estado { get; set; } = string.Empty;

    public string EstadoCss { get; set; } = string.Empty;

    public decimal? Monto { get; set; }
}
