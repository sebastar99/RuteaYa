using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RuteaYa.Entidades;

namespace RuteaYa.Test.Helpers;

/// <summary>SELECT simulados y guardado interceptado en memoria: nunca abre SQL ni ejecuta escrituras.</summary>
internal sealed class EnviosContextoPrueba : IDisposable
{
    public ConsultasEfSimuladas Consultas { get; } = new();
    private readonly Contexto _db;
    public RuteaYaDbContext Db => _db;
    public int Guardados => _db.Guardados;

    public EnviosContextoPrueba(DbException? errorLectura = null)
    {
        var opciones = new DbContextOptionsBuilder<RuteaYaDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=PruebaNoConectar;Integrated Security=true;TrustServerCertificate=true")
            .AddInterceptors(new SinConexion(errorLectura), Consultas).Options;
        _db = new Contexto(opciones);
    }

    // Mismo orden y tipos que SELECT de entidad completa de EF: PK y propiedades alfabéticas.
    public static DataTable Envios(params (int Id, int UsuarioId, int? ConductorId, EstadoEnvio Estado, string Descripcion)[] filas)
    {
        var tabla = new DataTable();
        foreach (var (nombre, tipo) in new[] {
            ("Id", typeof(int)), ("ConductorId", typeof(int)), ("CreadoEn", typeof(DateTime)),
            ("Descripcion", typeof(string)), ("DestinoDir", typeof(string)), ("DestinoLat", typeof(decimal)),
            ("DestinoLng", typeof(decimal)), ("EntregaLimiteConOffset", typeof(DateTimeOffset)), ("EntregaUbicacionId", typeof(Guid)), ("Estado", typeof(byte)), ("FechaLimite", typeof(DateTime)),
            ("Monto", typeof(decimal)), ("OrigenDir", typeof(string)), ("OrigenLat", typeof(decimal)),
            ("OrigenLng", typeof(decimal)), ("PesoKg", typeof(decimal)), ("RetiroDisponibleDesde", typeof(DateTimeOffset)),
            ("RetiroSinRestriccion", typeof(bool)), ("RetiroUbicacionId", typeof(Guid)), ("RowVersion", typeof(byte[])), ("SalidaViajeId", typeof(int)),
            ("Tamano", typeof(byte)), ("UsuarioId", typeof(int)) })
            tabla.Columns.Add(nombre, tipo);
        foreach (var f in filas)
            tabla.Rows.Add(f.Id, f.ConductorId.HasValue ? (object)f.ConductorId.Value : DBNull.Value,
                new DateTime(2026, 11, 1), f.Descripcion, "Illia 2849", -34.6m, -58.38m, DBNull.Value, DBNull.Value, (byte)f.Estado,
                DateTime.Today.AddDays(1), 4000m, "Edison 3436", -34.6037m, -58.3816m, 2.5m,
                DBNull.Value, false, DBNull.Value, new byte[8], DBNull.Value, (byte)TamanoEnvio.Mediano, f.UsuarioId);
        return tabla;
    }

    public static DataTable Existe(bool existe)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("Existe", typeof(bool));
        tabla.Rows.Add(existe);
        return tabla;
    }

    public void Dispose() => _db.Dispose();

    private sealed class SinConexion(DbException? error) : DbConnectionInterceptor
    {
        private InterceptionResult Abrir()
        {
            if (error != null) throw error;
            return InterceptionResult.Suppress();
        }
        public override InterceptionResult ConnectionOpening(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result) => Abrir();
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken ct = default) => ValueTask.FromResult(Abrir());
    }

    private sealed class Contexto(DbContextOptions<RuteaYaDbContext> opciones) : RuteaYaDbContext(opciones)
    {
        public int Guardados { get; private set; }
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            Guardados++;
            foreach (var entry in ChangeTracker.Entries<Envio>().Where(e => e.State == EntityState.Added))
                if (entry.Entity.Id == 0) entry.Entity.Id = 1;
            return 1;
        }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(SaveChanges(acceptAllChangesOnSuccess));
        }
    }
}
