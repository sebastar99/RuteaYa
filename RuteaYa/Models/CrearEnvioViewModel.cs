using Microsoft.AspNetCore.Mvc.ModelBinding;
using RuteaYa.Logica.DTOs;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Web.Models;

public class CrearEnvioViewModel : CrearEnvioDto, IValidatableObject
{
    [Required(ErrorMessage = "Ingresá la dirección de retiro."), StringLength(200, ErrorMessage = "La dirección de retiro no puede superar los 200 caracteres.")]
    public new string OrigenDir { get => base.OrigenDir; set => base.OrigenDir = value; }
    [Required(ErrorMessage = "Ingresá la dirección de entrega."), StringLength(200, ErrorMessage = "La dirección de entrega no puede superar los 200 caracteres.")]
    public new string DestinoDir { get => base.DestinoDir; set => base.DestinoDir = value; }
    [ModelBinder(BinderType = typeof(DecimalHtmlBinder)), Range(0.1, 1000.0, ErrorMessage = "El peso debe ser mayor a 0 kg y no superar 1000 kg.")]
    public new decimal PesoKg { get => base.PesoKg; set => base.PesoKg = value; }
    [ModelBinder(BinderType = typeof(DecimalHtmlBinder)), Range(1, 1000000, ErrorMessage = "El monto ofrecido debe ser mayor a 0.")]
    public new decimal Monto { get => base.Monto; set => base.Monto = value; }
    [BindRequired]
    public new DateTime FechaLimite { get => base.FechaLimite; set => base.FechaLimite = value; }
    public string? FormularioDirecciones { get; set; }
    public string? RetiroComprobante { get; set; }
    public string? EntregaComprobante { get; set; }
    [BindNever] public CampoDireccionViewModel? RetiroCampo { get; set; }
    [BindNever] public CampoDireccionViewModel? EntregaCampo { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // Presentación por campo de las mismas reglas que valida EnviosLogica/HorariosEnvio.
        if (FechaLimite < DateTime.Now.Date) yield return new("La fecha límite no puede ser anterior a hoy.", new[] { nameof(FechaLimite) });
        if (RetiroSinRestriccion && RetiroDisponibleDesdeLocal.HasValue)
            yield return new("Elegí un horario de retiro o marcá sin restricción, no ambos.", new[] { nameof(RetiroDisponibleDesdeLocal), nameof(RetiroSinRestriccion) });
        if (EntregaLimiteLocal.HasValue && EntregaLimiteLocal.Value.Date != FechaLimite.Date)
            yield return new("El horario límite debe corresponder a la fecha límite publicada.", new[] { nameof(EntregaLimiteLocal) });
        if (RetiroDisponibleDesdeLocal.HasValue && EntregaLimiteLocal.HasValue && RetiroDisponibleDesdeLocal > EntregaLimiteLocal)
            yield return new("El retiro no puede comenzar después del límite de entrega.", new[] { nameof(RetiroDisponibleDesdeLocal) });
    }
}
