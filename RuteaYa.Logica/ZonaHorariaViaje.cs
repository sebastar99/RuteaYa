using RuteaYa.Entidades;

namespace RuteaYa.Logica;

public static class ZonaHorariaViaje
{
    public const string Argentina = "America/Argentina/Buenos_Aires";
    public static string? ArgentinaPorDireccion(string? direccion) =>
        direccion != null && direccion.Trim().EndsWith(", Argentina", StringComparison.OrdinalIgnoreCase) ? Argentina : null;

    /// <summary>
    /// Zona única para los horarios de un envío. Solo se resuelve si todas las direcciones confirmadas
    /// son de Argentina; si no puede determinarse devuelve null y nunca se inventa una zona.
    /// </summary>
    public static TimeZoneInfo? PorDirecciones(params string?[] direcciones)
    {
        if (direcciones.Length == 0 || direcciones.Any(d => ArgentinaPorDireccion(d) == null)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(Argentina); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    public static string? Automatica(Viaje v)
    {
        var u = v.OrigenUbicacion;
        return u != null && u.UsuarioId == v.UsuarioId && u.TipoRecurso == TipoRecursoUbicacion.Viaje &&
            u.Campo == CampoUbicacion.OrigenViaje && (!u.RecursoId.HasValue || u.RecursoId == v.Id) &&
            u.ConsumidaEnUtc.HasValue && u.ConfirmadaEnUtc != default && u.ConfirmadaEnUtc <= u.ConsumidaEnUtc &&
            u.ConsumidaEnUtc <= u.VenceEnUtc && u.IdHere.StartsWith("here:", StringComparison.Ordinal) &&
            u.Proveedor == "HERE-v7" && u.DireccionNormalizada == v.Origen ? ArgentinaPorDireccion(u.DireccionNormalizada) : null;
    }

    public static TimeZoneInfo Resolver(Viaje viaje, string? seleccion)
    {
        var id = Automatica(viaje) ?? seleccion;
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Elegí la zona horaria del lugar de salida.", "ZonaHorariaId");
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new ArgumentException("Elegí una zona horaria válida de la lista.", "ZonaHorariaId"); }
    }

    public static DateTimeOffset Convertir(DateTime local, TimeZoneInfo zona)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zona.IsInvalidTime(local)) throw new ArgumentException("Ese horario no existe por un cambio de hora. Elegí otro horario.");
        if (zona.IsAmbiguousTime(local)) throw new ArgumentException("Ese horario coincide con un cambio de hora. Elegí otro horario sin ambigüedad.");
        return new DateTimeOffset(local, zona.GetUtcOffset(local));
    }
}
