using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Services;

/// <summary>HERE Flexible Polyline v1. Se ignora únicamente la dimensión adicional.</summary>
public static class FlexiblePolyline
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    public static List<PuntoRutaDto> Decodificar(string? texto)
    {
        if (string.IsNullOrEmpty(texto) || texto.Length > 4_000_000) throw Invalida();
        var indice = 0;
        ulong Leer()
        {
            ulong valor = 0;
            for (var desplazamiento = 0; desplazamiento <= 60; desplazamiento += 5)
            {
                if (indice >= texto.Length) throw Invalida();
                var caracter = Alfabeto.IndexOf(texto[indice++]);
                if (caracter < 0 || (desplazamiento == 60 && (caracter & 31) > 15)) throw Invalida();
                valor |= (ulong)(caracter & 31) << desplazamiento;
                if ((caracter & 32) == 0) return valor;
            }
            throw Invalida();
        }
        long Delta()
        {
            var valor = Leer();
            var magnitud = (long)(valor >> 1);
            return (valor & 1) == 0 ? magnitud : ~magnitud;
        }

        try
        {
            if (Leer() != 1) throw Invalida();
            var cabecera = Leer();
            if (cabecera > 2047) throw Invalida();
            var precision = (int)(cabecera & 15);
            var dimension = (int)((cabecera >> 4) & 7);
            if (dimension is 4 or 5) throw Invalida(); // valores reservados
            var escala = (decimal)Math.Pow(10, precision);
            long latitud = 0, longitud = 0, adicional = 0;
            var puntos = new List<PuntoRutaDto>();
            while (indice < texto.Length)
            {
                latitud = checked(latitud + Delta());
                longitud = checked(longitud + Delta());
                if (dimension != 0) adicional = checked(adicional + Delta());
                var punto = new PuntoRutaDto { Latitud = latitud / escala, Longitud = longitud / escala };
                if (punto.Latitud is < -90 or > 90 || punto.Longitud is < -180 or > 180) throw Invalida();
                puntos.Add(punto);
                if (puntos.Count > 200_000) throw Invalida();
            }
            if (puntos.Count < 2) throw Invalida();
            return puntos;
        }
        catch (OverflowException) { throw Invalida(); }
    }

    private static InvalidOperationException Invalida() => new("HERE Routing devolvió una geometría inválida o incompleta.");
}
