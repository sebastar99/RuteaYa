using Microsoft.AspNetCore.Mvc.ModelBinding;
using RuteaYa.Logica.DTOs;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Web.Models;

public class CrearEnvioViewModel : CrearEnvioDto
{
    [ModelBinder(BinderType = typeof(DecimalHtmlBinder)), Range(0.1, 1000.0, ErrorMessage = "El peso debe ser mayor a 0 kg y no superar 1000 kg.")]
    public new decimal PesoKg { get => base.PesoKg; set => base.PesoKg = value; }
    [ModelBinder(BinderType = typeof(DecimalHtmlBinder)), Range(1, 1000000, ErrorMessage = "El monto ofrecido debe ser mayor a 0.")]
    public new decimal Monto { get => base.Monto; set => base.Monto = value; }
    public string? FormularioDirecciones { get; set; }
    public string? RetiroComprobante { get; set; }
    public string? EntregaComprobante { get; set; }
    [BindNever] public CampoDireccionViewModel? RetiroCampo { get; set; }
    [BindNever] public CampoDireccionViewModel? EntregaCampo { get; set; }
}
