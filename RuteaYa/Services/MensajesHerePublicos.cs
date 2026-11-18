namespace RuteaYa.Web.Services;

public static class MensajesHerePublicos
{
    public static string Http(int? estado) => estado switch {
        401 => "HERE rechazó la autenticación (401). Revisá la configuración privada de la clave.",
        403 => "HERE no autorizó este servicio (403). Revisá los permisos de la aplicación HERE.",
        429 => "HERE alcanzó el límite de solicitudes (429). Esperá un momento antes de reintentar.",
        >= 500 => "HERE no está disponible temporalmente. Intentá nuevamente más tarde.",
        null => "No se pudo conectar con HERE. Revisá la conexión del servidor.",
        _ => $"HERE rechazó la consulta (HTTP {estado}). Revisá las direcciones elegidas."
    };
}
