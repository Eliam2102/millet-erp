using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Npgsql;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>Escritura idempotente de la bitácora de cambios de maestros A+W (clientes y productos).</summary>
internal static class BitacoraMaestroAw
{
    private const string PgUniqueViolation = "23505";
    private const string PrefijoIndiceUnico = "ix_cambios_maestros_aw_";

    /// <summary>
    /// Agrega al contexto solo lo no registrado aún: la clave es (campo, tipo, versión del evento, huella previa),
    /// de modo que un reenvío idéntico no duplica y la misma transición repetida más tarde sí se registra.
    /// </summary>
    public static async Task AgregarAsync(
        CompartidoDbContext db, IReadOnlyList<CambioMaestroAw> cambios, string tipoEntidad, string referencia, string hash, CancellationToken ct)
    {
        if (cambios.Count == 0) return;
        var existentes = await db.CambiosMaestrosAw.AsNoTracking()
            .Where(c => c.TipoEntidad == tipoEntidad && c.ReferenciaExterna == referencia && c.VersionEvento == hash)
            .Select(c => new { c.Campo, c.Tipo, c.HuellaPrevia }).ToListAsync(ct);
        foreach (var c in cambios.Where(c => !existentes.Any(e => e.Campo == c.Campo && e.Tipo == c.Tipo && e.HuellaPrevia == c.HuellaPrevia)))
            db.Add(c);
    }

    /// <summary>
    /// SaveChanges que trata la violación del índice único de la bitácora (carrera con otro escritor del mismo evento)
    /// como «ya registrado»: suelta las filas de bitácora pendientes, reevalúa contra la BD y reintenta una vez.
    /// </summary>
    public static async Task GuardarAsync(
        CompartidoDbContext db, IReadOnlyList<CambioMaestroAw> cambios, string tipoEntidad, string referencia, string hash, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PgUniqueViolation, ConstraintName: { } ix }
            && ix.StartsWith(PrefijoIndiceUnico, StringComparison.Ordinal))
        {
            foreach (var e in db.ChangeTracker.Entries<CambioMaestroAw>().Where(e => e.State == EntityState.Added).ToList())
                e.State = EntityState.Detached;
            await AgregarAsync(db, cambios, tipoEntidad, referencia, hash, ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
