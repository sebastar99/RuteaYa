namespace RuteaYa.Web.Services;

public sealed class SeleccionTemporalNoDisponibleException(string mensaje) : ArgumentException(mensaje);
