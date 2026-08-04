using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Evidencias;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Evidencias.Queries;

/// <summary>
/// Lista las evidencias adjuntas a un documento (factura por ahora).
/// </summary>
public sealed record ListarEvidenciasQuery(
    TipoDocumentoEvidencia TipoDocumento,
    Guid DocumentoId) : IRequest<IReadOnlyList<EvidenciaResponse>>;

public sealed record EvidenciaResponse(
    Guid Id,
    TipoEvidencia Tipo,
    string ArchivoBlobRef,
    string NombreArchivo,
    string ContentType,
    long? TamanioBytes,
    string Comentario,
    EstadoFirmaFisica EstadoFirmaFisica,
    DateOnly? FechaLimiteFirmaFisica,
    DateTimeOffset? FechaRecepcionFirmaFisica,
    Guid? CapturadoPor,
    DateTimeOffset FechaCaptura);

public sealed class ListarEvidenciasHandler : IRequestHandler<ListarEvidenciasQuery, IReadOnlyList<EvidenciaResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarEvidenciasHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<EvidenciaResponse>> Handle(ListarEvidenciasQuery query, CancellationToken cancellationToken)
    {
        return await _db.EvidenciasAutorizacion
            .AsNoTracking()
            .Where(e => e.TipoDocumento == query.TipoDocumento && e.DocumentoId == query.DocumentoId)
            .OrderByDescending(e => e.FechaCaptura)
            .Select(e => new EvidenciaResponse(
                e.Id, e.Tipo, e.ArchivoBlobRef, e.NombreArchivo, e.ContentType,
                e.TamanioBytes, e.Comentario, e.EstadoFirmaFisica,
                e.FechaLimiteFirmaFisica, e.FechaRecepcionFirmaFisica,
                e.CapturadoPor, e.FechaCaptura))
            .ToListAsync(cancellationToken);
    }
}
