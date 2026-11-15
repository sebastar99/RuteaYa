using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RuteaYa.Web.Models;

/// <summary>Los inputs HTML number envían punto decimal, independientemente de la cultura del servidor.</summary>
public sealed class DecimalHtmlBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        if (decimal.TryParse(value.FirstValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            context.Result = ModelBindingResult.Success(number);
        else context.ModelState.TryAddModelError(context.ModelName, "Ingresá un número válido en el campo numérico.");
        return Task.CompletedTask;
    }
}
