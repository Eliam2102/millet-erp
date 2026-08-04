using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Folios;

namespace Millet.Compras.Infrastructure.Folios;

/// <summary>
/// Implementación de <see cref="IFolioSecuenciaService"/> (ADR-0047 PR5.C). Upsert
/// atómico sobre <c>compras.folio_secuencias</c> — extraído sin cambios de
/// <c>CrearRequisicionHandler.GetNextFolioSequenceAsync</c> para reuso entre el path
/// humano y el path de sistema. <c>FolioSecuencia</c> NO es <c>IAuditable</c>, así que
/// el SQL crudo es válido (cuidado §6.1: no bypass de interceptors).
/// </summary>
public sealed class FolioSecuenciaService : IFolioSecuenciaService
{
    private readonly ComprasDbContext _db;

    public FolioSecuenciaService(ComprasDbContext db) => _db = db;

    public async Task<string> SiguienteFolioRequisicionAsync(
        Guid empresaId,
        Guid sucursalId,
        string sucursalCodigo,
        short folioAnio,
        CancellationToken cancellationToken)
    {
        var result = await _db.Database
            .SqlQuery<int>($@"
                INSERT INTO compras.folio_secuencias (empresa_id, sucursal_id, anio, siguiente)
                VALUES ({empresaId}, {sucursalId}, {folioAnio}, 2)
                ON CONFLICT (empresa_id, sucursal_id, anio) DO UPDATE
                  SET siguiente = compras.folio_secuencias.siguiente + 1
                RETURNING (siguiente - 1)::int AS ""Value""
            ")
            .ToListAsync(cancellationToken);

        var siguiente = result.Single();
        return $"{sucursalCodigo}{folioAnio}-{siguiente:D6}";
    }
}
