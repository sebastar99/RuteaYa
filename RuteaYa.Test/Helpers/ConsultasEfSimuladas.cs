using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RuteaYa.Entidades;

namespace RuteaYa.Test.Helpers;

/// <summary>
/// Usa el traductor SQL Server real de EF, pero suprime la apertura de conexiones
/// y reemplaza los lectores por respuestas en memoria. No conecta ni crea bases.
/// Cualquier operación que no sea SELECT o cualquier guardado hace fallar la prueba.
/// </summary>
internal sealed class ConsultasEfSimuladas : DbCommandInterceptor
{
    private readonly Queue<DataTable> _respuestas = new();
    public List<ConsultaSql> Consultas { get; } = new();

    public void Responder(DataTable tabla) => _respuestas.Enqueue(tabla);
    public int RespuestasPendientes => _respuestas.Count;

    public RuteaYaDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<RuteaYaDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=PruebaNoConectar;Integrated Security=true;TrustServerCertificate=true")
            .AddInterceptors(new SinConexionReal(), this)
            .Options;
        return new ContextoSinGuardados(opciones);
    }

    private InterceptionResult<DbDataReader> Leer(DbCommand comando)
    {
        if (!comando.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La prueba solo permite consultas SELECT.");
        Consultas.Add(new ConsultaSql(comando.CommandText,
            comando.Parameters.Cast<DbParameter>().ToDictionary(p => p.ParameterName, p => p.Value)));
        if (_respuestas.Count == 0)
            throw new InvalidOperationException("Se ejecutó una consulta no prevista en la prueba.");
        return InterceptionResult<DbDataReader>.SuppressWithResult(_respuestas.Dequeue().CreateDataReader());
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) => Leer(command);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Leer(command));
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
        throw new InvalidOperationException("La prueba prohíbe escrituras.");

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("La prueba prohíbe escrituras.");

    internal sealed record ConsultaSql(string Sql, Dictionary<string, object?> Parametros);

    private sealed class SinConexionReal : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }

    private sealed class ContextoSinGuardados : RuteaYaDbContext
    {
        public ContextoSinGuardados(DbContextOptions<RuteaYaDbContext> opciones) : base(opciones) { }

        public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
            throw new InvalidOperationException("La prueba prohíbe SaveChanges.");

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("La prueba prohíbe SaveChangesAsync.");
    }
}
