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
    public async Task<List<ViajeDto>> ListarViajesAsync(int usuarioId, string estado = "todos")
    {
        var consulta = _db.Viajes
            .AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId);
        
        estado = (estado ?? "todos").Trim();

        if(estado.Equals("activo", StringComparison.OrdinalIgnoreCase))
        {
            consulta = consulta.Where(v => v.Activo);
        }
        else if(estado.Equals("inactivo", StringComparison.OrdinalIgnoreCase))
        {
            consulta = consulta.Where(v => !v.Activo);
        }
        else if(!estado.Equals("todos", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(estado))
        {
            throw new ArgumentException("El estado debe ser activo, inactivo o todos.");
        }

        var viajes = await consulta
            .Include(v => v.Dias)
            .Include(v => v.Paradas)
            .OrderByDescending(v => v.Id)
            .ToListAsync();

        return viajes.Select(Convertir).ToList();
    }

    // ================================ Busca viaje ==========================
    public async Task<ViajeDto?> ObtenerPorIdAsync(int usuarioId, int id)
    {
        var viaje = await _db.Viajes
            .Include(v => v.Dias)
            .Include(v => v.Paradas)
            .FirstOrDefaultAsync(v => v.Id == id && v.UsuarioId == usuarioId);
        
        return viaje == null ? null : Convertir(viaje);
    }

    // =========================== Valida y guarda nuevo viaje =======================
    public async Task<(bool Exito, string mensaje, ViajeDto? viaje)> CrearViajeAsync(int usuarioId, CrearViajeDto dto)
    {
        var error = Validar(dto);
        if(error != null)
        {
            return (false, error, null);
        }

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

            CreadoEn = DateTime.UtcNow,

            Paradas = dto.Paradas.Select((p, i) => new Parada
            {
                Ubicacion = p.Trim(),
                Orden = i + 1
            }).ToList()
        };

        _db.Viajes.Add(viaje);
        await _db.SaveChangesAsync();

        return (true, "Viaje crerado correctamente", Convertir(viaje));
    }

    // ============================= Modifica viaje ==============================
    public async Task<(bool exito, string mensaje, ViajeDto? viaje)> EditarViajeAsync(int usuarioId, int id, EditarViajeDto dto)
    {
        var error = Validar(dto);
        if(error != null)
        {
            return (false, error, null);
        }

        var viaje = await _db.Viajes
            .Include(v => v.Dias)
            .Include(v => v.Paradas)
            .FirstOrDefaultAsync(v => v.Id == id && v.UsuarioId == usuarioId);

        if(viaje == null)
        {
            return (false, "No se encontro el viaje.", null);
        }
        if (!viaje.Activo)
        {
            return (false, "No se puede editar un viaje inactivo.", null);
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.DiasViaje.RemoveRange(viaje.Dias);
        _db.Paradas.RemoveRange(viaje.Paradas);

        viaje.Origen = dto.Origen.Trim();
        viaje.Destino = dto.Destino.Trim();

        viaje.HoraSalida = dto.HoraSalida;
        viaje.HoraLlegada = dto.HoraLlegada;
        viaje.Dias = dto.DiasSemana.Distinct().Select(d => new DiaViaje
        {
            ViajeId = id,
            DiaSemana = d
        }).ToList();

        viaje.CantMaxPaquetes = dto.CantMaxPaquetes;
        viaje.PesoMax = dto.PesoMax;
        viaje.DesvioMaxKm = dto.DesvioMaxKm;

        viaje.Paradas = dto.Paradas.Select((p, i) => new Parada
        {
            ViajeId = id,
            Ubicacion = p.Trim(),
            Orden = i + 1
        }).ToList();

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return (true, "El viaje se modifico correctamente.", Convertir(viaje));
    }

    // ============================ Desactiva viaje sin borrarlo de BD =============================
    public async Task<(bool exito, string mensaje)> DesactivarViajeAsync(int usuarioId, int id)
    {
        var viaje = await _db.Viajes
            .FirstOrDefaultAsync(v => v.Id == id && v.UsuarioId == usuarioId);
        
        if(viaje == null)
        {
            return (false, "No se encontro el viaje.");
        }
        if (!viaje.Activo)
        {
            return (true, "El viaje ya esta desactivo.");
        }

        viaje.Activo = false;

        await _db.SaveChangesAsync();
        return (true, "El viaje se dasactivo.");
    }

    // ============================ Agrega parada (en x posicion) ===================================
    public async Task<(bool exito, string mensaje, ParadaDto? parada)> AgregarParadaAsync(
        int usuarioId,
        int viajeId,
        string ubicacion,
        int Orden
    ) {
        var viaje = await _db.Viajes
            .Include(v => v.Paradas)
            .FirstOrDefaultAsync(v => v.Id == viajeId && v.UsuarioId == usuarioId);
        
        if(viaje == null)
        {
            return(false, "No se encontro el viaje.", null);
        }
        if (!viaje.Activo)
        {
            return (false, "No se puede modificar las paradas de un viaje inactivo.", null);
        }

        var existente = viaje.Paradas.OrderBy(p => p.Orden).ToList();

        var error = ValidarParada(
            ubicacion, 
            viaje.Origen, 
            viaje.Destino, 
            existente.Select(p => p.Ubicacion)
        );

        if(error != null)
        {
            return (false, error, null);
        }
        if(Orden < 1 || Orden > existente.Count + 1)
        {
            return (false, "El orden debe estar entre 1 y la cantidad de paradas mas uno.", null);
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Guardar orden original
        var ordenOriginal = existente.ToDictionary(p => p.Id, p => p.Orden);

        // Se mueven las paradas para evitar conflicto
        foreach(var parada in existente)
        {
            parada.Orden +=10000;
        }
        await _db.SaveChangesAsync();

        // Agrega espacio para nueva parada
        foreach(var parada in existente)
        {
            var ordenAnterior = ordenOriginal[parada.Id];

            parada.Orden = ordenAnterior >= Orden ? ordenAnterior + 1 : ordenAnterior;
        }

        var nuevaParada = new Parada
        {
            ViajeId = viajeId,
            Ubicacion = ubicacion.Trim(),
            Orden = Orden
        };
        _db.Paradas.Add(nuevaParada);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var ParadaDto = new ParadaDto
        {
            Id = nuevaParada.Id,
            Ubicacion = nuevaParada.Ubicacion,
            Orden = nuevaParada.Orden
        };
        return (true, "Parada agregada correctamente.", ParadaDto);
    }

    // ======================== Elimina parada (reorganiza posicion) ============================
    public async Task<(bool exito, string mensaje)> EliminarParadaAsync(int usuarioId, int viajeId, int paradaId)
    {
        var viaje = await _db.Viajes
            .Include(v => v.Paradas)
            .FirstOrDefaultAsync(v => v.Id == viajeId && v.UsuarioId == usuarioId);
        
        if(viaje == null)
        {
            return (false, "No se encontro el viaje.");
        }
        if (!viaje.Activo)
        {
            return (false, "No se puede modificar un viaje inactivo.");
        }

        var parada = viaje.Paradas.FirstOrDefault(parada => parada.Id == paradaId);

        if(parada == null)
        {
            return (false, "No se encontro esa parada en el viaje");
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.Paradas.Remove(parada);

        await _db.SaveChangesAsync();

        // ======== reordena orden ======
        var restantes = viaje.Paradas
            .Where(p => p.Id != paradaId)
            .OrderBy(p => p.Orden)
            .ToList();

        // orden temporal
        for (var i = 0; i < restantes.Count; i++)
        {
            restantes[i].Orden = 10000 + i;
        }
        await _db.SaveChangesAsync();

        // asignar verdadero orden
        for(var i = 0; i < restantes.Count; i++)
        {
            restantes[i].Orden = i + 1;
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return (true, "Parada eliminada.");
    }



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

        CreadoEn = v.CreadoEn,

        Paradas = v.Paradas.OrderBy(p => p.Orden).Select(p => new ParadaDto
        {
            Id = p.Id,
            Ubicacion = p.Ubicacion,
            Orden = p.Orden
        }).ToList()
    };

    private static string? Validar(CrearViajeDto dto)
    {
        if(dto == null) {
            return "Los datos del viaje son obligatorios.";
        }

        if(string.IsNullOrWhiteSpace(dto.Origen) || string.IsNullOrWhiteSpace(dto.Destino))
        {
            return "El origen y destino son obligatorios.";   
        }

        if(dto.Origen.Trim()
            .Equals(dto.Destino.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "El origen y destino deben ser diferentes.";
        } 
        if(dto.Origen.Trim().Length > 150 || dto.Destino.Trim().Length > 150)
        {
            return "El origen y destino no pueden superar los 150 caracteres.";
        }

        if(dto.DiasSemana == null || dto.DiasSemana.Count == 0 ||
            dto.DiasSemana.Any(d => d<1 || d>7 )
        ) {
            return "Debes seleccionar un dia valido para el viaje.";
        }
        
        if(dto.CantMaxPaquetes <= 0)
        {
            return "No se pueden elegir numeros negativos.";
        }
        
        if(dto.PesoMax <= 0 )
        {
            return "Revisa que el peso sea valido.";
        }
        if(dto.DesvioMaxKm < 0)
        {
            return "Revisa que el desvio maximo sea valido.";
        }

        if(dto.HoraSalida < TimeSpan.Zero || dto.HoraSalida >= TimeSpan.FromDays(1) ||
            dto.HoraLlegada < TimeSpan.Zero || dto.HoraLlegada >= TimeSpan.FromDays(1)
        ) {
            return "Los horarios no son validos.";
        }
        
        if(dto.Paradas == null)
        {
            return "La lista de paradas no es valida.";
        }

        var paradas = dto.Paradas
            .Select(p => p?.Trim() ?? "")
            .ToList();

        if (paradas.Any(string.IsNullOrWhiteSpace))
        {
            return "Las paradas no pueden estar vacias.";
        }
        if (paradas.Any(p => p.Length > 150))
        {
            return "Cada parada puede tener hasta 150 caracteres.";
        }    
        if(paradas.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paradas.Count())
        {
            return "No se pueden repetir paradas.";
        }
        if(paradas.Any(p => 
            p.Equals(dto.Origen.Trim(), StringComparison.OrdinalIgnoreCase) ||
            p.Equals(dto.Destino.Trim(), StringComparison.OrdinalIgnoreCase)
        )) {
            return "La parada no puede ser igual al origen o destino.";
        }
        
        return null;
    }

    public static string? ValidarParada(
        string Ubicacion,
        string origen,
        string destino,
        IEnumerable<string> existente
    ) {
        if (string.IsNullOrWhiteSpace(Ubicacion))
        {
            return "La ubicacion de la parada es obligatoria.";
        }

        var lugar = Ubicacion.Trim();
        if(lugar.Length > 150)
        {
            return "La ubicacion no puede superar los 150 caracteres";
        }
        if(lugar.Equals(origen, StringComparison.OrdinalIgnoreCase) ||
            lugar.Equals(destino, StringComparison.OrdinalIgnoreCase))
        {
            return "La parada no debe coincidir con el origen o destino";
        }

        if(existente.Any(p =>
            p.Equals(lugar, StringComparison.OrdinalIgnoreCase)))
        {
            return "Ya existe esa parada";
        }

        return null;
    }
}