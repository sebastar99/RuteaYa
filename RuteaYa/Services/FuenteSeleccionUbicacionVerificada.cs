using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Services;

/// <summary>Puente de confianza: recupera estado servidor y nunca transforma coordenadas de un POST en una confirmación.</summary>
public sealed class FuenteSeleccionUbicacionVerificada(SeleccionDireccionServicio selecciones) : IFuenteSeleccionUbicacionVerificada
{
    public SeleccionUbicacionVerificadaDto Obtener(int usuarioId, ContextoUbicacionDto contexto, string tokenSeleccion)
    {
        var estado = selecciones.ObtenerConfirmada(usuarioId, contexto.AmbitoSeleccion, tokenSeleccion);
        if (!estado.Confirmada || !estado.Acceso.HasValue || !estado.ConfirmadaEnUtc.HasValue || !estado.VenceEnUtc.HasValue)
            throw new ComprobanteUbicacionException("seleccion_no_confirmada", "Falta una selección confirmada en el servidor.");
        return new(estado.Ubicacion, estado.Acceso.Value, estado.ConfirmadaEnUtc.Value, estado.VenceEnUtc.Value);
    }
}
