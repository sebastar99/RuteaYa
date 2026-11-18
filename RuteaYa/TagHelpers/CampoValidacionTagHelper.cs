using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace RuteaYa.Web.TagHelpers;

// Incluye inputs escritos a mano (HERE, filtros y paradas), además de asp-for.
[HtmlTargetElement("input")]
[HtmlTargetElement("select")]
[HtmlTargetElement("textarea")]
public sealed class CampoValidacionTagHelper : TagHelper
{
    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = null!;
    public override int Order => int.MaxValue;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var nombre = output.Attributes["name"]?.Value?.ToString();
        if (nombre == null || !ViewContext.ViewData.ModelState.TryGetValue(nombre, out var estado)) return;
        var tipo = output.Attributes["type"]?.Value?.ToString()?.ToLowerInvariant();
        if (!context.AllAttributes.ContainsName("asp-for") && estado.AttemptedValue != null && tipo is not ("hidden" or "password" or "file" or "checkbox" or "radio"))
        {
            if (output.TagName == "input") output.Attributes.SetAttribute("value", estado.AttemptedValue);
            if (output.TagName == "textarea") output.Content.SetContent(estado.AttemptedValue);
        }
        if (estado.Errors.Count == 0 || tipo == "hidden") return;
        var clases = output.Attributes["class"]?.Value?.ToString() ?? "";
        if (!clases.Split(' ').Contains("input-validation-error")) output.Attributes.SetAttribute("class", clases + " input-validation-error");
        output.Attributes.SetAttribute("aria-invalid", "true");
    }
}
