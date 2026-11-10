using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado.Efectos;

/// <summary>Un efecto posterior al timbrado con su estado, para el detalle del comprobante (M-285).</summary>
public sealed record EfectoComprobanteItem(
    Guid Id, string Tipo, string Descripcion, string Estado, int Intentos,
    string? UltimoErrorCodigo, string? UltimoError, DateTimeOffset? UltimoIntentoAt, DateTimeOffset? AplicadoAt);

/// <summary>Factura timbrada con efectos pendientes (bandeja, M-285).</summary>
public sealed record ComprobanteConEfectosPendientesItem(
    Guid ComprobanteId, string Folio, string? Uuid, DateTimeOffset? FechaTimbrado, IReadOnlyList<EfectoComprobanteItem> Pendientes);

public sealed record EfectosComprobanteQuery(Guid ComprobanteId) : IRequest<IReadOnlyList<EfectoComprobanteItem>>;

public sealed record ComprobantesConEfectosPendientesQuery : IRequest<IReadOnlyList<ComprobanteConEfectosPendientesItem>>;

/// <summary>Aplica solo los efectos pendientes de un comprobante timbrado; no vuelve a timbrar.</summary>
public sealed record ReintentarEfectosComprobanteCommand(Guid ComprobanteId) : IRequest<IReadOnlyList<EfectoComprobanteItem>>;

internal static class EfectosTexto
{
    public static string Descripcion(TipoEfectoComprobante tipo) => tipo switch
    {
        TipoEfectoComprobante.EventoContable => "Póliza y cartera (evento de timbrado)",
        TipoEfectoComprobante.EstadoPedido => "Pedido facturado y aviso a A+W",
        TipoEfectoComprobante.NcRanura => "Nota de crédito de ranura",
        TipoEfectoComprobante.CancelacionSustituida => "Cancelación de la factura sustituida",
        _ => tipo.ToString(),
    };

    public static EfectoComprobanteItem Item(EfectoComprobante e) => new(
        e.Id, e.Tipo.ToString(), Descripcion(e.Tipo), e.Estado.ToString(), e.Intentos,
        e.UltimoErrorCodigo, e.UltimoError, e.UltimoIntentoAt, e.AplicadoAt);
}

public sealed class EfectosComprobanteHandler(FacturacionDbContext db)
    : IRequestHandler<EfectosComprobanteQuery, IReadOnlyList<EfectoComprobanteItem>>,
      IRequestHandler<ComprobantesConEfectosPendientesQuery, IReadOnlyList<ComprobanteConEfectosPendientesItem>>
{
    public async Task<IReadOnlyList<EfectoComprobanteItem>> Handle(EfectosComprobanteQuery query, CancellationToken cancellationToken) =>
        (await db.EfectosComprobante.AsNoTracking().Where(e => e.ComprobanteId == query.ComprobanteId)
            .OrderBy(e => e.Tipo).ToListAsync(cancellationToken))
        .Select(EfectosTexto.Item).ToList();

    public async Task<IReadOnlyList<ComprobanteConEfectosPendientesItem>> Handle(
        ComprobantesConEfectosPendientesQuery query, CancellationToken cancellationToken)
    {
        var pendientes = await db.EfectosComprobante.AsNoTracking()
            .Where(e => e.Estado == EstadoEfectoComprobante.Pendiente)
            .OrderBy(e => e.CreatedAt).Take(500).ToListAsync(cancellationToken);
        var ids = pendientes.Select(e => e.ComprobanteId).Distinct().ToList();
        var comprobantes = await db.Comprobantes.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Folio, c.Uuid, c.FechaTimbrado }).ToListAsync(cancellationToken);
        return comprobantes
            .Select(c => new ComprobanteConEfectosPendientesItem(c.Id, c.Folio, c.Uuid, c.FechaTimbrado,
                pendientes.Where(e => e.ComprobanteId == c.Id).OrderBy(e => e.Tipo).Select(EfectosTexto.Item).ToList()))
            .OrderBy(c => c.FechaTimbrado).ToList();
    }
}

public sealed class ReintentarEfectosComprobanteHandler(
    FacturacionDbContext db,
    ISender sender,
    ICfdiTimbradoPort fiscal,
    ICfdiRepositorioPort cfdiRepo,
    IIntegrationEventPublisher eventos,
    ValidadorReceptorFiscal receptorFiscal,
    IClock clock,
    IProductosReadPort? productos = null)
    : IRequestHandler<ReintentarEfectosComprobanteCommand, IReadOnlyList<EfectoComprobanteItem>>
{
    public async Task<IReadOnlyList<EfectoComprobanteItem>> Handle(
        ReintentarEfectosComprobanteCommand command, CancellationToken cancellationToken)
    {
        var factura = await db.FacturasVenta
            .Include(f => f.Lineas).Include(f => f.Relaciones)
            .Include(f => f.ComplementoCce!).ThenInclude(c => c.Lineas)
            .FirstOrDefaultAsync(f => f.Id == command.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO",
                $"No existe la factura de venta {command.ComprobanteId}.");
        if (factura.Estado is not (EstadoTimbrado.Timbrado or EstadoTimbrado.CancelacionPendiente))
            throw new BusinessRuleException("EFECTOS_SIN_TIMBRE",
                "Solo se reintentan los efectos de un comprobante timbrado. Si no timbró, reintenta el timbrado.");

        try
        {
            await new EfectosPostTimbrado(db, sender, fiscal, cfdiRepo, eventos, receptorFiscal, productos)
                .EjecutarPendientesAsync(factura, clock.UtcNow, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("EFECTOS_EN_CURSO",
                "Otro reintento de estos efectos ya está en curso. Recarga para ver el resultado.", ex);
        }

        return (await db.EfectosComprobante.AsNoTracking().Where(e => e.ComprobanteId == factura.Id)
                .OrderBy(e => e.Tipo).ToListAsync(cancellationToken))
            .Select(EfectosTexto.Item).ToList();
    }
}
