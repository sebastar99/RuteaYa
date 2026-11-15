namespace RuteaYa.Test;

public sealed class EdgeFactAttribute : FactAttribute
{
    public EdgeFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Helpers.EdgeNavegador.Ejecutable))
            Skip = "Requiere Microsoft Edge instalado para la regresión de navegador real.";
    }
}
