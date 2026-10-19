using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
// EF traduce ToUpper/Trim/Replace a SQL; las sobrecargas con StringComparison no se traducen.
#pragma warning disable CA1304, CA1311, CA1862, CA1307, CA1310
namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>Clientes que ya ocupan una clave de ligado: por su RFC del ERP o por el UST_ID de su registro de origen A+W (M2.2).</summary>
internal static class ClientesPorRfc
{
    public static async Task<List<(string Clave, ClienteExistente Cliente)>> BuscarAsync(
        CompartidoDbContext db, IReadOnlyCollection<string> claves, Guid? excluirId, CancellationToken ct)
    {
        if (claves.Count == 0) return [];

        var porRfc = await db.Clientes.AsNoTracking()
            .Where(c => (excluirId == null || c.Id != excluirId.Value) && c.Rfc != null && claves.Contains(c.Rfc.Trim().ToUpper()))
            .Select(c => new { Llave = c.Rfc!.Trim().ToUpper(), c.Id, c.Clave, c.RazonSocial })
            .ToListAsync(ct);

        var porUst = await db.Set<ClienteSincronizacionAw>().AsNoTracking()
            .Join(db.Clientes.AsNoTracking(), r => r.ClienteId, c => c.Id, (r, c) => new { r, c })
            .Where(x => (excluirId == null || x.c.Id != excluirId.Value) && x.r.CandidatoFiscalUstId != null
                && claves.Contains(x.r.CandidatoFiscalUstId.Trim().ToUpper().Replace("-", "").Replace(" ", "").Replace(".", "")))
            .Select(x => new { Llave = x.r.CandidatoFiscalUstId!.Trim().ToUpper().Replace("-", "").Replace(" ", "").Replace(".", ""), x.c.Id, x.c.Clave, x.c.RazonSocial })
            .ToListAsync(ct);

        return porRfc.Concat(porUst)
            .DistinctBy(x => (x.Llave, x.Id))
            .Select(x => (x.Llave, new ClienteExistente(x.Id, x.Clave, x.RazonSocial)))
            .ToList();
    }
}
