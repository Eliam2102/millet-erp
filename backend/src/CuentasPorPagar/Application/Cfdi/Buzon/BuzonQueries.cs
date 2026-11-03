using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.Buzon;

// P8.1: bandeja de revisión del buzón y bitácora de corridas. Las filas con
// EmpresaId nulo (receptor desconocido) las ve cualquier empresa con permiso.

public sealed record BuzonRevisionItemResponse(
    Guid Id, string Asunto, string Remitente, DateTimeOffset RecibidoEn, string Motivo,
    DateTimeOffset RegistradoEn, bool Descartado, string? MotivoDescarte);

public sealed record ListarBuzonRevisionQuery(bool IncluirDescartados = false, int Offset = 0, int Limit = 50)
    : IRequest<PagedResponse<BuzonRevisionItemResponse>>;

public sealed class ListarBuzonRevisionHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext empresa)
    : IRequestHandler<ListarBuzonRevisionQuery, PagedResponse<BuzonRevisionItemResponse>>
{
    public async Task<PagedResponse<BuzonRevisionItemResponse>> Handle(ListarBuzonRevisionQuery q, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(q.Limit, 1, 200);
        var offset = Math.Max(0, q.Offset);
        var actual = empresa.Current;
        var query = db.BuzonRevisionCfdi.AsNoTracking().Where(b => b.EmpresaId == null || b.EmpresaId == actual);
        if (!q.IncluirDescartados) query = query.Where(b => !b.Descartado);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(b => b.RegistradoEn).Skip(offset).Take(limit)
            .Select(b => new BuzonRevisionItemResponse(b.Id, b.Asunto, b.Remitente, b.RecibidoEn, b.Motivo,
                b.RegistradoEn, b.Descartado, b.MotivoDescarte))
            .ToListAsync(cancellationToken);
        return new PagedResponse<BuzonRevisionItemResponse>(items, offset, limit, total);
    }
}

public sealed record DescartarBuzonRevisionCommand(Guid Id, string Motivo) : IRequest<Unit>;

public sealed class DescartarBuzonRevisionValidator : AbstractValidator<DescartarBuzonRevisionCommand>
{
    public DescartarBuzonRevisionValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class DescartarBuzonRevisionHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext empresa)
    : IRequestHandler<DescartarBuzonRevisionCommand, Unit>
{
    public async Task<Unit> Handle(DescartarBuzonRevisionCommand c, CancellationToken cancellationToken)
    {
        var actual = empresa.Current;
        var fila = await db.BuzonRevisionCfdi.FirstOrDefaultAsync(b => b.Id == c.Id && (b.EmpresaId == null || b.EmpresaId == actual), cancellationToken)
            ?? throw new EntityNotFoundException("BUZON_REVISION_NO_ENCONTRADA", $"No existe el correo en revisión '{c.Id}'.");
        fila.Descartar(c.Motivo);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record BuzonEjecucionResponse(
    Guid Id, DateTimeOffset InicioUtc, DateTimeOffset FinUtc, int Mensajes, int Ingresados, int Duplicados, int ARevision, string? Error);

public sealed record ListarBuzonEjecucionesQuery(int Limit = 20) : IRequest<IReadOnlyList<BuzonEjecucionResponse>>;

public sealed class ListarBuzonEjecucionesHandler(CuentasPorPagarDbContext db)
    : IRequestHandler<ListarBuzonEjecucionesQuery, IReadOnlyList<BuzonEjecucionResponse>>
{
    public async Task<IReadOnlyList<BuzonEjecucionResponse>> Handle(ListarBuzonEjecucionesQuery q, CancellationToken cancellationToken) =>
        await db.BuzonEjecuciones.AsNoTracking().OrderByDescending(e => e.InicioUtc).Take(Math.Clamp(q.Limit, 1, 100))
            .Select(e => new BuzonEjecucionResponse(e.Id, e.InicioUtc, e.FinUtc, e.Mensajes, e.Ingresados, e.Duplicados, e.ARevision, e.Error))
            .ToListAsync(cancellationToken);
}
