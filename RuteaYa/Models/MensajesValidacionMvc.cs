using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace RuteaYa.Web.Models;

public static class MensajesValidacionMvc
{
    public static void Configurar(DefaultModelBindingMessageProvider mensajes)
    {
        mensajes.SetValueMustNotBeNullAccessor(_ => "Completá este campo.");
        mensajes.SetMissingBindRequiredValueAccessor(_ => "Completá este campo obligatorio.");
        mensajes.SetAttemptedValueIsInvalidAccessor((_, _) => "Ingresá un valor válido para este campo.");
        mensajes.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Ingresá un valor válido.");
        mensajes.SetValueIsInvalidAccessor(_ => "Ingresá un valor válido.");
        mensajes.SetValueMustBeANumberAccessor(_ => "Ingresá un número válido.");
        mensajes.SetNonPropertyValueMustBeANumberAccessor(() => "Ingresá un número válido.");
    }
}
