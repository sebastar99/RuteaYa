using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public class ViajesLogica
{
    private readonly RuteaYaDbContext _db;
    public ViajesLogica(RuteaYaDbContext db)
    {
        _db = db;
    }

    // ============== Lista viajes por usuario (filtra: activos/inactivos) ==============
    public async Task<List<ViajeDto>> ListarAsync(int usuarioId, string estado = "todos")
    {
        var consulta = _db.Viajes
            .AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId);

        if(estado.Equals("activo", StringComparison.OrdinalIgnoreCase))
        consulta = consulta.Where(v => v.Activo);
        if(estado.Equals("inactivo", StringComparison.OrdinalIgnoreCase))
        consulta = consulta.Where(v => !v.Activo);

        var viajes = await consulta
            .Include(v => v.Dias)
            .Include(v => v.Paradas)
            .OrderByDescending(v => v.Id)
            .ToListAsync();

        return viajes.Select(Convertir).ToList();
    }

    // ================================ Busca viaje ==========================
    public async Task<ViajeDto?> ObtenerAsync(int usuarioId, int id)
    {
        var viaje = await _db.Viajes
            .Include(v => v.Dias)
            .Include(v => v.Paradas)
            .FirstOrDefaultAsync(v => v.Id == id && v.UsuarioId == usuarioId);
        
        return viaje == null ? null : Convertir(viaje);
    }

    // =========================== Valida y guarda nuevo viaje =======================
    public async Task<ViajeDto> CrearAsync(int usuarioId, CrearViajeDto dto)
    {
        Validar(dto);

        var viaje = new Viaje
        {
            UsuarioId = usuarioId,

            Origen = dto.Origen.Trim(),
            Destino = dto.Destino.Trim(),

            HoraSalida = dto.HoraSalida,
            HoraLlegada = dto.HoraLlegada,

            Dias = dto.DiasSemana.Distinct().Select(d => new DiaViaje
            {
                DiaSemana = d
            }).ToList(),

            CantMaxPaquetes =dto.CantMaxPaquetes,
            PesoMax = dto.PesoMax,
            DesvioMaxKm = dto.DesvioMaxKm,

            Activo = true,

            Paradas = dto.Paradas.Select((p, i) => new Parada
            {
                Ubicacion = p.Trim(),
                Orden = i + 1
            }).ToList()
        };

        _db.Viajes.Add(viaje);
        await _db.SaveChangesAsync();

        return Convertir(viaje);
    }

    // Modifica viaje
    // Desactiva viaje sin borrarlo de BD
    // Agrega parada (en x posicion)
    // Elimina parada (reorganiza posicion)

    // ================================== METODOS ============================
    private static ViajeDto Convertir(Viaje v) => new()
    {
        Id = v.Id,
        UsuarioId = v.UsuarioId,

        Origen = v.Origen,
        Destino = v.Destino,

        HoraSalida = v.HoraSalida,
        HoraLlegada = v.HoraLlegada,
        DiasSemana = v.Dias.OrderBy(d => d.DiaSemana).Select(d => d.DiaSemana).ToList(),

        CantMaxPaquetes = v.CantMaxPaquetes,
        PesoMax = v.PesoMax,
        DesvioMaxKm = v.DesvioMaxKm,

        Activo = v.Activo,

        Paradas = v.Paradas.OrderBy(p => p.Orden).Select(p => new ParadaDto
        {
            Id = p.Id,
            Ubicacion = p.Ubicacion,
            Orden = p.Orden
        }).ToList()
    };

    private static void Validar(CrearViajeDto dto)
    {
        if(string.IsNullOrWhiteSpace(dto.Origen) || string.IsNullOrWhiteSpace(dto.Destino))
        throw new ArgumentException("El origen y destino son obligatorios.");

        if(dto.Origen.Trim()
            .Equals(dto.Destino.Trim(), StringComparison.OrdinalIgnoreCase)
        )
        throw new ArgumentException("El origen y destino deben ser diferentes.");

        if(dto.DiasSemana == null || 
            dto.DiasSemana.Count == 0 ||
            dto.DiasSemana.Any(d => d<1 || d>7 )
        )
        throw new ArgumentException("Debes seleccionar dias validos para el viaje.");

        if(dto.CantMaxPaquetes <= 0)
        throw new ArgumentException("No se pueden elegir numeros negativos.");

        if(dto.PesoMax <= 0 || dto.DesvioMaxKm < 0)
        throw new ArgumentException("Revisa que el peso y desvio maximo sean validos.");

        if(dto.HoraSalida < TimeSpan.Zero ||
            dto.HoraSalida >= TimeSpan.FromDays(1) ||
            dto.HoraLlegada < TimeSpan.Zero ||
            dto.HoraLlegada >= TimeSpan.FromDays(1)
        )
        throw new ArgumentException("Los horarios no son validos.");

        if(dto.Paradas == null)
        throw new Exception("La lista de paradas no es valida.");

        var paradas = dto.Paradas.Select(p => p?.Trim() ?? "").ToList();

        if(paradas.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Las paradas no pueden estar vacias.");
        if(paradas.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paradas.Count())
            throw new ArgumentException("No se pueden repetir paradas.");
        if(paradas.Any(p => 
            p.Equals(dto.Origen.Trim(), StringComparison.OrdinalIgnoreCase) ||
            p.Equals(dto.Destino.Trim(), StringComparison.OrdinalIgnoreCase)
        ))
        throw new ArgumentException("La parada no puede ser igual al origen o destino.");
    }
}