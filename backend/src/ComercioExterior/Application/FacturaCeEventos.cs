using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;
public sealed record FacturaCePayload(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid? ExpedienteCeId = null, int? VersionEmbarqueCe = null,
    Guid FacturaVentaId = default, Guid ComprobanteId = default, Guid? PedidoFacturableId = null, string? Uuid = null, string? Folio = null, DateTimeOffset? FechaTimbrado = null, string? UuidOriginal = null);
public sealed record RecibirFacturaCeCommand(FacturaCePayload Datos, bool Cancelada) : IRequest;
public sealed class RecibirFacturaCeHandler(ComercioExteriorDbContext db) : IRequestHandler<RecibirFacturaCeCommand>
{
    public async Task Handle(RecibirFacturaCeCommand request, CancellationToken cancellationToken)
    {
        var d = request.Datos;
        if (d.ExpedienteCeId is not Guid id) return;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var e = await db.Expedientes.FromSqlInterpolated($"SELECT * FROM comercio_exterior.expedientes WHERE id = {id} AND empresa_id = {d.EmpresaId} FOR UPDATE")
            .Include(e => e.Facturas).AsSplitQuery().SingleOrDefaultAsync(cancellationToken) ?? throw new EntityNotFoundException("CE_EXPEDIENTE_NO_ENCONTRADO", "El expediente de la factura no existe en la empresa.");
        e.RegistrarFactura(request.Cancelada ? d.ComprobanteId : d.FacturaVentaId, d.PedidoFacturableId ?? Guid.Empty, d.VersionEmbarqueCe ?? 0, d.Uuid ?? "", d.Folio ?? "", d.FechaTimbrado, request.Cancelada, d.UuidOriginal, d.OcurridoEn);
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
    }
}
