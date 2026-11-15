using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>Persistencia compartida. La API solo necesita EF para consumir; no necesita una clave ni un cliente HERE.</summary>
public sealed class UbicacionesConfirmadasServicio(RuteaYaDbContext db,
    IFuenteSeleccionUbicacionVerificada? fuente = null, TimeProvider? reloj = null) : IUbicacionesConfirmadasServicio
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private static ComprobanteUbicacionException Error(string codigo) => new(codigo, "La ubicación no se puede utilizar: " + codigo + ".");
    private static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
    private static bool TokenValido(string? token) => token?.Length == 64 && token.All(Uri.IsHexDigit);

    public async Task<ContextoUbicacionDto> CrearContextoAsync(int usuarioId, TipoRecursoUbicacion tipo, int? recursoId,
        CampoUbicacion campo, string claveCampo, Guid? formularioId = null, CancellationToken ct = default)
    {
        var contexto = new ContextoUbicacionDto(formularioId ?? Guid.NewGuid(), tipo, recursoId, campo, claveCampo, null);
        ValidarContexto(usuarioId, contexto);
        return contexto with { RevisionRecurso = await RevisionAsync(usuarioId, contexto, ct) };
    }

    private static void ValidarContexto(int usuarioId, ContextoUbicacionDto c)
    {
        if (usuarioId <= 0 || c.FormularioId == Guid.Empty || c.RecursoId <= 0 ||
            string.IsNullOrWhiteSpace(c.ClaveCampo) || c.ClaveCampo.Length > 64 || c.ClaveCampo.Any(char.IsControl) ||
            !Enum.IsDefined(c.TipoRecurso) || !Enum.IsDefined(c.Campo) ||
            (c.TipoRecurso == TipoRecursoUbicacion.Viaje) != (c.Campo is CampoUbicacion.OrigenViaje or CampoUbicacion.DestinoViaje or CampoUbicacion.Parada))
            throw Error("contexto_invalido");
        if (c.Campo == CampoUbicacion.Parada && !Guid.TryParseExact(c.ClaveCampo, "N", out _)) throw Error("campo_invalido");
        if (c.Campo != CampoUbicacion.Parada && c.ClaveCampo != c.Campo.ToString()) throw Error("campo_invalido");
    }

    private async Task<string?> RevisionAsync(int usuarioId, ContextoUbicacionDto c, CancellationToken ct)
    {
        if (!c.RecursoId.HasValue) return null;
        if (c.TipoRecurso == TipoRecursoUbicacion.Viaje)
        {
            var v = await db.Viajes.AsNoTracking().Include(v => v.Paradas)
                .SingleOrDefaultAsync(v => v.Id == c.RecursoId && v.UsuarioId == usuarioId, ct);
            if (v == null) throw Error("recurso_no_disponible");
            return Hash(JsonSerializer.Serialize(new { v.Id, v.UsuarioId, v.Origen, v.Destino, v.OrigenUbicacionId, v.DestinoUbicacionId,
                Paradas = v.Paradas.OrderBy(p => p.Orden).Select(p => new { p.Id, p.Ubicacion, p.Orden, p.UbicacionConfirmadaId }) }));
        }
        var e = await db.Envios.AsNoTracking().SingleOrDefaultAsync(e => e.Id == c.RecursoId && e.UsuarioId == usuarioId, ct);
        if (e == null) throw Error("recurso_no_disponible");
        return Hash(JsonSerializer.Serialize(new { e.Id, e.UsuarioId, e.OrigenDir, e.DestinoDir, e.RetiroUbicacionId, e.EntregaUbicacionId, e.RowVersion }));
    }

    private async Task ValidarRevisionAsync(int usuarioId, ContextoUbicacionDto contexto, CancellationToken ct)
    {
        ValidarContexto(usuarioId, contexto);
        if (await RevisionAsync(usuarioId, contexto, ct) != contexto.RevisionRecurso) throw Error("recurso_modificado");
    }

    public async Task<ComprobanteUbicacionDto> EmitirAsync(int usuarioId, ContextoUbicacionDto contexto, string tokenSeleccion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (db.ChangeTracker.HasChanges()) throw Error("unidad_de_trabajo_ocupada");
        if (!TokenValido(tokenSeleccion)) throw Error("seleccion_invalida");
        await ValidarRevisionAsync(usuarioId, contexto, ct);
        if (fuente == null) throw Error("fuente_no_configurada");
        // Nunca recibe coordenadas de un formulario: la fuente recupera un snapshot verificado del servidor.
        var seleccion = fuente.Obtener(usuarioId, contexto, tokenSeleccion);
        var ahora = _reloj.GetUtcNow();
        var dato = seleccion.Ubicacion;
        bool PuntoValido(PuntoRutaDto? p) => p != null && p.Latitud is >= -90 and <= 90 && p.Longitud is >= -180 and <= 180;
        if (seleccion.ConfirmadaEnUtc == default || seleccion.ConfirmadaEnUtc > ahora || seleccion.VenceEnUtc <= ahora ||
            seleccion.VenceEnUtc <= seleccion.ConfirmadaEnUtc || !PuntoValido(dato.Posicion) ||
            string.IsNullOrWhiteSpace(dato.IdHere) || !dato.IdHere.StartsWith("here:", StringComparison.Ordinal) || dato.IdHere.Length > 512 ||
            string.IsNullOrWhiteSpace(dato.DireccionNormalizada) || dato.DireccionNormalizada.Length > 512 ||
            dato.DireccionNormalizada.Any(char.IsControl) || string.IsNullOrWhiteSpace(dato.TipoResultado) || dato.TipoResultado.Length > 80 ||
            dato.TipoNumeroPuerta?.Length > 80 || seleccion.AccesoElegido < -1 || seleccion.AccesoElegido >= dato.Accesos.Count)
            throw Error("seleccion_invalida");
        var navegacion = seleccion.AccesoElegido == -1 ? dato.Posicion : dato.Accesos[seleccion.AccesoElegido];
        if (!PuntoValido(navegacion)) throw Error("seleccion_invalida");
        var seleccionHash = Hash(tokenSeleccion);
        if (await db.UbicacionesConfirmadas.AnyAsync(u => u.SeleccionHash == seleccionHash, ct)) throw Error("seleccion_ya_emitida");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var ubicacion = new UbicacionConfirmada {
            TokenHash = Hash(token), SeleccionHash = seleccionHash, UsuarioId = usuarioId,
            FormularioId = contexto.FormularioId, TipoRecurso = contexto.TipoRecurso, RecursoId = contexto.RecursoId,
            Campo = contexto.Campo, ClaveCampo = contexto.ClaveCampo, RevisionRecurso = contexto.RevisionRecurso,
            IdHere = dato.IdHere, DireccionNormalizada = dato.DireccionNormalizada,
            PosicionLatitud = dato.Posicion.Latitud, PosicionLongitud = dato.Posicion.Longitud,
            NavegacionLatitud = navegacion.Latitud, NavegacionLongitud = navegacion.Longitud, AccesoElegido = seleccion.AccesoElegido,
            TipoResultado = dato.TipoResultado, TipoNumeroPuerta = dato.TipoNumeroPuerta,
            PosicionEstimada = dato.PosicionEstimada, NumeroPuertaCorregido = dato.NumeroPuertaCorregido,
            ConfirmadaEnUtc = seleccion.ConfirmadaEnUtc,
            VenceEnUtc = seleccion.VenceEnUtc < ahora.AddMinutes(10) ? seleccion.VenceEnUtc : ahora.AddMinutes(10)
        };
        db.UbicacionesConfirmadas.Add(ubicacion);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(ubicacion).State = EntityState.Detached;
            if (await db.UbicacionesConfirmadas.AnyAsync(u => u.SeleccionHash == seleccionHash, ct)) throw Error("emision_en_conflicto");
            throw;
        }
        return new(token, ubicacion.VenceEnUtc);
    }

    public async Task GuardarAsync(int usuarioId, IReadOnlyList<AsignacionUbicacionDto> asignaciones, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (asignaciones == null || asignaciones.Count is < 1 or > 52 || db.Database.CurrentTransaction != null) throw Error("guardado_invalido");
        if (db.ChangeTracker.Entries<UbicacionConfirmada>().Any(e => e.State != EntityState.Unchanged)) throw Error("snapshot_modificado");
        var primera = asignaciones[0].Contexto;
        var destinos = asignaciones.Select(a => (Destino: (object?)a.Viaje ?? (object?)a.Parada ?? a.Envio, a.Contexto.Campo)).ToArray();
        if (destinos.Distinct().Count() != destinos.Length) throw Error("destino_repetido");
        var raices = asignaciones.Select(a => (object?)a.Viaje ?? (object?)a.Parada?.Viaje ?? a.Envio).ToArray();
        if (raices.Any(r => r == null) || raices.Any(r => !ReferenceEquals(r, raices[0]))) throw Error("recursos_mezclados");
        if (asignaciones.Select(a => a.Token).Distinct().Count() != asignaciones.Count ||
            asignaciones.Select(a => (a.Contexto.Campo, a.Contexto.ClaveCampo)).Distinct().Count() != asignaciones.Count ||
            asignaciones.Any(a => a.Contexto.FormularioId != primera.FormularioId || a.Contexto.TipoRecurso != primera.TipoRecurso ||
                a.Contexto.RecursoId != primera.RecursoId || a.Contexto.RevisionRecurso != primera.RevisionRecurso)) throw Error("contextos_mezclados");
        // Restaurar también el estado en memoria si SQL revierte; no descartar cambios ajenos con ChangeTracker.Clear().
        var anteriores = db.ChangeTracker.Entries().Select(e => new {
            Entry = e, Estado = e.State, Valores = e.CurrentValues.Clone(), Originales = e.OriginalValues.Clone(),
            Navegaciones = e.Navigations.Where(n => n.Metadata.TargetEntityType.ClrType == typeof(UbicacionConfirmada))
                .ToDictionary(n => n.Metadata.Name, n => n.CurrentValue)
        }).ToArray();
        // Mantiene estable la revisión del recurso durante validación, vínculo y consumo.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var usadas = new List<UbicacionConfirmada>();
            foreach (var a in asignaciones)
            {
                await ValidarRevisionAsync(usuarioId, a.Contexto, ct);
                if (!TokenValido(a.Token)) throw Error("comprobante_invalido");
                var hash = Hash(a.Token);
                var u = await db.UbicacionesConfirmadas.SingleOrDefaultAsync(u => u.TokenHash == hash, ct);
                if (u == null || u.UsuarioId != usuarioId || u.FormularioId != a.Contexto.FormularioId || u.TipoRecurso != a.Contexto.TipoRecurso ||
                    u.RecursoId != a.Contexto.RecursoId || u.Campo != a.Contexto.Campo || u.ClaveCampo != a.Contexto.ClaveCampo ||
                    u.RevisionRecurso != a.Contexto.RevisionRecurso) throw Error("comprobante_no_disponible");
                if (u.ConsumidaEnUtc.HasValue) throw Error("comprobante_consumido");
                if (u.VenceEnUtc <= _reloj.GetUtcNow()) throw Error("comprobante_vencido");
                if (a.Parada is Parada parada && db.Entry(parada).State != EntityState.Added &&
                    !await db.Paradas.AsNoTracking().AnyAsync(p => p.Id == parada.Id && p.ViajeId == a.Contexto.RecursoId && p.Viaje!.UsuarioId == usuarioId, ct))
                    throw Error("parada_no_disponible");
                Vincular(usuarioId, a, u);
                u.ConsumidaEnUtc = _reloj.GetUtcNow(); u.Version = Guid.NewGuid(); usadas.Add(u);
            }
            ValidarUnidadDeTrabajo(usuarioId, asignaciones, usadas);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception error)
        {
            await tx.RollbackAsync(CancellationToken.None);
            foreach (var e in db.ChangeTracker.Entries().ToArray())
                if (!anteriores.Any(a => ReferenceEquals(a.Entry.Entity, e.Entity))) e.State = EntityState.Detached;
            foreach (var a in anteriores)
            {
                a.Entry.CurrentValues.SetValues(a.Valores); a.Entry.OriginalValues.SetValues(a.Originales);
                foreach (var nav in a.Navegaciones) a.Entry.Navigation(nav.Key).CurrentValue = nav.Value;
                a.Entry.State = a.Estado;
            }
            if (error is DbUpdateConcurrencyException) throw Error("consumo_en_conflicto");
            throw;
        }
    }

    private void Vincular(int usuarioId, AsignacionUbicacionDto a, UbicacionConfirmada u)
    {
        if (new object?[] { a.Viaje, a.Parada, a.Envio }.Count(x => x != null) != 1) throw Error("destino_invalido");
        object destino = (object?)a.Viaje ?? (object?)a.Parada ?? a.Envio!;
        if (db.Entry(destino).State is EntityState.Detached or EntityState.Deleted) throw Error("destino_no_preparado");
        if (a.Viaje is Viaje v && u.Campo is CampoUbicacion.OrigenViaje or CampoUbicacion.DestinoViaje)
        {
            ValidarDestino(v.UsuarioId, v.Id, db.Entry(v).State, usuarioId, a.Contexto);
            if (u.DireccionNormalizada.Length > 150) throw Error("direccion_excede_contrato_actual");
            if (u.Campo == CampoUbicacion.OrigenViaje) { v.OrigenUbicacion = u; v.Origen = u.DireccionNormalizada; }
            else { v.DestinoUbicacion = u; v.Destino = u.DireccionNormalizada; }
        }
        else if (a.Parada is Parada p && u.Campo == CampoUbicacion.Parada)
        {
            var padre = p.Viaje ?? db.Viajes.Local.SingleOrDefault(v => v.Id == p.ViajeId);
            if (padre == null) throw Error("viaje_no_preparado");
            ValidarDestino(padre.UsuarioId, padre.Id, db.Entry(padre).State, usuarioId, a.Contexto);
            if (p.ViajeId != 0 && p.ViajeId != padre.Id) throw Error("destino_invalido");
            if (u.DireccionNormalizada.Length > 150) throw Error("direccion_excede_contrato_actual");
            p.UbicacionConfirmada = u; p.Ubicacion = u.DireccionNormalizada;
        }
        else if (a.Envio is Envio e && u.Campo is CampoUbicacion.RetiroEnvio or CampoUbicacion.EntregaEnvio)
        {
            ValidarDestino(e.UsuarioId, e.Id, db.Entry(e).State, usuarioId, a.Contexto);
            if (u.DireccionNormalizada.Length > 200) throw Error("direccion_excede_contrato_actual");
            if (u.Campo == CampoUbicacion.RetiroEnvio) {
                e.RetiroUbicacion = u; e.OrigenDir = u.DireccionNormalizada;
                e.OrigenLat = u.PosicionLatitud; e.OrigenLng = u.PosicionLongitud;
            } else {
                e.EntregaUbicacion = u; e.DestinoDir = u.DireccionNormalizada;
                e.DestinoLat = u.PosicionLatitud; e.DestinoLng = u.PosicionLongitud;
            }
        }
        else throw Error("campo_destino_invalido");
    }

    private static void ValidarDestino(int propietario, int id, EntityState estado, int usuarioId, ContextoUbicacionDto contexto)
    {
        if (propietario != usuarioId || (contexto.RecursoId.HasValue ? id != contexto.RecursoId : estado != EntityState.Added)) throw Error("destino_invalido");
    }

    private void ValidarUnidadDeTrabajo(int usuarioId, IReadOnlyList<AsignacionUbicacionDto> asignaciones, List<UbicacionConfirmada> usadas)
    {
        var viajes = asignaciones.Select(a => a.Viaje ?? a.Parada?.Viaje).Where(v => v != null).ToArray();
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            bool permitido = entry.Entity switch {
                UbicacionConfirmada u => usadas.Contains(u) && entry.State == EntityState.Modified,
                Viaje v => v.UsuarioId == usuarioId && viajes.Contains(v),
                Envio e => e.UsuarioId == usuarioId && asignaciones.Any(a => ReferenceEquals(a.Envio, e)),
                Parada p => viajes.Any(v => ReferenceEquals(p.Viaje, v) || v!.Id > 0 && p.ViajeId == v.Id),
                DiaViaje d => viajes.Any(v => v!.Id > 0 && d.ViajeId == v.Id || v.Dias.Contains(d)),
                _ => false
            };
            if (!permitido) throw Error("cambios_ajenos_en_unidad_de_trabajo");
        }
    }
}
