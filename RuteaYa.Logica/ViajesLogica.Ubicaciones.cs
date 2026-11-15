using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public partial class ViajesLogica
{
    private async Task<(bool Exito, string mensaje, ViajeDto? viaje)> GuardarConUbicacionesAsync(int usuarioId, int? id, CrearViajeDto dto)
    {
        var ubicaciones = _ubicaciones; // API valida comprobantes persistidos, sin proveedor HERE.
        var datos = dto.Ubicaciones!;
        if (datos.Origen?.Contexto == null || datos.Destino?.Contexto == null || datos.Paradas == null || datos.Paradas.Count > 50 ||
            datos.Paradas.Any(p => p?.Contexto == null))
            return (false, "Las ubicaciones y la lista de paradas no son válidas.", null);
        var viaje = id.HasValue ? await _db.Viajes.Include(v => v.Paradas).Include(v => v.Dias)
            .SingleOrDefaultAsync(v => v.Id == id && v.UsuarioId == usuarioId) :
            new Viaje { UsuarioId = usuarioId, Activo = true, CreadoEn = DateTime.UtcNow };
        if (viaje == null) return (false, "No se encontró el viaje.", null);
        if (!viaje.Activo) return (false, "No se puede editar un viaje inactivo.", null);
        var campos = new[] { datos.Origen, datos.Destino }.Concat(datos.Paradas).ToList();
        var primero = datos.Origen.Contexto;
        if (campos.Any(c => c.Contexto.TipoRecurso != TipoRecursoUbicacion.Viaje || c.Contexto.RecursoId != id ||
            c.Contexto.FormularioId != primero.FormularioId || c.Contexto.RevisionRecurso != primero.RevisionRecurso) ||
            datos.Origen.Contexto.Campo != CampoUbicacion.OrigenViaje || datos.Destino.Contexto.Campo != CampoUbicacion.DestinoViaje ||
            datos.Paradas.Any(p => p.Contexto.Campo != CampoUbicacion.Parada) ||
            datos.Paradas.Any(p => p.IdParada > 0 && p.Contexto.ClaveCampo != IdentidadParadaFormulario.Existente(primero.FormularioId, p.IdParada)) ||
            datos.Paradas.Select(p => p.Contexto.ClaveCampo).Distinct().Count() != datos.Paradas.Count ||
            datos.Paradas.Where(p => p.IdParada > 0).Select(p => p.IdParada).Distinct().Count() != datos.Paradas.Count(p => p.IdParada > 0) ||
            datos.Paradas.Any(p => p.IdParada < 0 || p.IdParada > 0 && (!id.HasValue || !viaje.Paradas.Any(a => a.Id == p.IdParada))))
            throw new ComprobanteUbicacionException("ubicaciones_invalidas", "Las ubicaciones no pertenecen a este formulario o viaje.");

        async Task<string> Direccion(CampoViajeUbicacionDto campo, string? existente)
        {
            if (!string.IsNullOrEmpty(campo.Comprobante))
                return await ubicaciones.ObtenerDireccionAsync(usuarioId, campo.Contexto, campo.Comprobante);
            if (!id.HasValue || existente == null || campo.Texto != existente)
                throw new ComprobanteUbicacionException("ubicacion_sin_confirmar", "Elegí y confirmá las direcciones nuevas o modificadas.");
            return existente; // incluido el legado pendiente, sin atribuirle coordenadas ni confirmación.
        }
        var origen = await Direccion(datos.Origen, id.HasValue ? viaje.Origen : null);
        var destino = await Direccion(datos.Destino, id.HasValue ? viaje.Destino : null);
        var paradas = new List<(CampoViajeUbicacionDto Campo, Parada Parada, string Texto)>();
        foreach (var campo in datos.Paradas)
        {
            var existente = viaje.Paradas.SingleOrDefault(p => p.Id == campo.IdParada && campo.IdParada > 0);
            var texto = await Direccion(campo, existente?.Ubicacion);
            paradas.Add((campo, existente ?? new Parada { Viaje = viaje, Ubicacion = texto }, texto));
        }
        var canonico = new CrearViajeDto { Origen = origen, Destino = destino, Paradas = paradas.Select(p => p.Texto).ToList(),
            DiasSemana = dto.DiasSemana, HoraSalida = dto.HoraSalida, HoraLlegada = dto.HoraLlegada,
            CantMaxPaquetes = dto.CantMaxPaquetes, PesoMax = dto.PesoMax, DesvioMaxKm = dto.DesvioMaxKm };
        var error = Validar(canonico);
        if (error != null) return (false, error, null);
        var asignaciones = new List<AsignacionUbicacionDto>();
        if (!string.IsNullOrEmpty(datos.Origen.Comprobante)) asignaciones.Add(new(primero, datos.Origen.Comprobante, Viaje: viaje));
        if (!string.IsNullOrEmpty(datos.Destino.Comprobante)) asignaciones.Add(new(datos.Destino.Contexto, datos.Destino.Comprobante, Viaje: viaje));
        foreach (var p in paradas.Where(p => !string.IsNullOrEmpty(p.Campo.Comprobante)))
            asignaciones.Add(new(p.Campo.Contexto, p.Campo.Comprobante!, Parada: p.Parada));
        if (!id.HasValue) _db.Viajes.Add(viaje);
        async Task Preparar(CancellationToken ct)
        {
            if (id.HasValue && viaje.Paradas.Count > 0)
            {
                // Reutiliza la estrategia del módulo para respetar el índice único de orden.
                foreach (var p in viaje.Paradas) p.Orden += 1000;
                await _db.SaveChangesAsync(ct);
            }
            _db.Paradas.RemoveRange(viaje.Paradas.Where(p => !paradas.Any(n => ReferenceEquals(n.Parada, p))).ToList());
            for (var i = 0; i < paradas.Count; i++) {
                var p = paradas[i].Parada; p.Viaje = viaje; p.Ubicacion = paradas[i].Texto; p.Orden = i + 1;
                if (p.Id == 0) { viaje.Paradas.Add(p); _db.Paradas.Add(p); }
            }
            viaje.Origen = origen; viaje.Destino = destino;
            viaje.HoraSalida = dto.HoraSalida; viaje.HoraLlegada = dto.HoraLlegada;
            viaje.CantMaxPaquetes = dto.CantMaxPaquetes; viaje.PesoMax = dto.PesoMax; viaje.DesvioMaxKm = dto.DesvioMaxKm;
            _db.DiasViaje.RemoveRange(viaje.Dias);
            viaje.Dias = dto.DiasSemana.Select(d => new DiaViaje { Viaje = viaje, DiaSemana = d }).ToList();
        }
        await ubicaciones.GuardarViajeAsync(usuarioId, viaje, primero, asignaciones, Preparar);
        return (true, id.HasValue ? "El viaje se modificó correctamente." : "El viaje se creó correctamente.", Convertir(viaje));
    }
}
