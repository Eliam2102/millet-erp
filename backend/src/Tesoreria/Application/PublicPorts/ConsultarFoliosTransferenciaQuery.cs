using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Domain.FoliosTransferencia;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.PublicPorts;

/// <summary>Folio de transferencia visto desde Caja (F6.6). <c>Estado</c>: Vigente, Usado o Cancelado.</summary>
public sealed record FolioTransferenciaPublicoDto(
    Guid Id, string Folio, Guid ClienteId, decimal Importe, string Estado, Guid? CobroMostradorId, string? Pedido);

/// <summary>
/// Puerto público de lectura (F6.6): Caja valida los folios que trae un cobro
/// con forma 03. Solo lectura; el consumo (Usado) llega por el evento del cobro.
/// </summary>
public sealed record ConsultarFoliosTransferenciaQuery(IReadOnlyCollection<string> Folios)
    : IRequest<IReadOnlyList<FolioTransferenciaPublicoDto>>;

/// <summary>Folios vigentes de un cliente, para el selector de Caja.</summary>
public sealed record FoliosTransferenciaVigentesClienteQuery(Guid ClienteId)
    : IRequest<IReadOnlyList<FolioTransferenciaPublicoDto>>;

public sealed class ConsultarFoliosTransferenciaHandler(TesoreriaDbContext db) :
    IRequestHandler<ConsultarFoliosTransferenciaQuery, IReadOnlyList<FolioTransferenciaPublicoDto>>,
    IRequestHandler<FoliosTransferenciaVigentesClienteQuery, IReadOnlyList<FolioTransferenciaPublicoDto>>
{
    public async Task<IReadOnlyList<FolioTransferenciaPublicoDto>> Handle(
        ConsultarFoliosTransferenciaQuery q, CancellationToken cancellationToken)
    {
        var folios = q.Folios.Select(f => f.Trim().ToUpperInvariant()).Distinct().ToArray();
        if (folios.Length == 0) return [];
        return await Proyectar(db.FoliosTransferencia.AsNoTracking().Where(f => folios.Contains(f.Folio)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FolioTransferenciaPublicoDto>> Handle(
        FoliosTransferenciaVigentesClienteQuery q, CancellationToken cancellationToken) =>
        await Proyectar(db.FoliosTransferencia.AsNoTracking()
                .Where(f => f.ClienteId == q.ClienteId && f.Estado == EstadoFolioTransferencia.Vigente)
                .OrderBy(f => f.EmitidoEn))
            .ToListAsync(cancellationToken);

    private static IQueryable<FolioTransferenciaPublicoDto> Proyectar(IQueryable<FolioTransferencia> q) =>
        q.Select(f => new FolioTransferenciaPublicoDto(
            f.Id, f.Folio, f.ClienteId, f.Importe,
            f.Estado == EstadoFolioTransferencia.Vigente ? "Vigente"
                : f.Estado == EstadoFolioTransferencia.Usado ? "Usado" : "Cancelado",
            f.CobroMostradorId, f.Pedido));
}
