using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.NotaCreditoProveedor.CapturarNotaCredito;

/// <summary>
/// Captura una NC del proveedor (F6-PR1, §7.1 del 01-diseno). El
/// handler intenta resolver la factura origen por
/// <c>UuidRelacionCfdi</c>:
/// <list type="bullet">
///   <item>Si la factura ya existe en CxP → NC nace
///         <see cref="EstadoNotaCredito.Abierta"/>.</item>
///   <item>Si no existe (caso A19, raro) → NC nace
///         <see cref="EstadoNotaCredito.EnEspera"/>; el worker
///         <c>NotaCreditoEnEsperaMatchWorker</c> intenta el match
///         diariamente.</item>
/// </list>
/// </summary>
public sealed record CapturarNotaCreditoCommand(
    Guid? CfdiRecibidoId,
    string UuidCfdi,
    Guid ProveedorId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaCfdi,
    string Moneda,
    decimal? TipoCambio,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    TipoNotaCredito Tipo,
    TipoRelacionCfdi TipoRelacionCfdi,
    string UuidRelacionCfdi) : IRequest<CapturarNotaCreditoResponse>;

public sealed record CapturarNotaCreditoResponse(
    Guid Id,
    EstadoNotaCredito Estado,
    Guid? FacturaOrigenId,
    int Version);

public sealed class CapturarNotaCreditoValidator : AbstractValidator<CapturarNotaCreditoCommand>
{
    public CapturarNotaCreditoValidator()
    {
        RuleFor(c => c.UuidCfdi).NotEmpty().Length(36);
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.UuidRelacionCfdi).NotEmpty().Length(36);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Total).GreaterThan(0);
        RuleFor(c => c.Subtotal).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ImpuestosTrasladados).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Retenciones).GreaterThanOrEqualTo(0);
        RuleFor(c => c.FolioProveedor).MaximumLength(40);
        RuleFor(c => c.SerieProveedor).MaximumLength(25);
    }
}

public sealed class CapturarNotaCreditoHandler
    : IRequestHandler<CapturarNotaCreditoCommand, CapturarNotaCreditoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public CapturarNotaCreditoHandler(
        CuentasPorPagarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IMediator mediator,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _mediator = mediator; _clock = clock;
    }

    public async Task<CapturarNotaCreditoResponse> Handle(
        CapturarNotaCreditoCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // Dedup por UUID del CFDI — el SAT garantiza unicidad global.
        var uuidNormalizado = command.UuidCfdi.Trim().ToUpperInvariant();
        var existe = await _db.NotasCreditoProveedor
            .AsNoTracking()
            .AnyAsync(n => n.UuidCfdi == uuidNormalizado, cancellationToken);
        if (existe)
        {
            throw new BusinessRuleException(
                "NC_DUPLICADA",
                $"Ya existe una NC capturada con UUID '{uuidNormalizado}'.");
        }

        // Resolución de la factura origen por UUID del CFDI relacionado.
        // El UUID de relación apunta al CFDI de la factura origen → buscamos
        // en facturas_proveedor por UuidCfdi.
        var uuidRelacionNormalizado = command.UuidRelacionCfdi.Trim().ToUpperInvariant();
        var facturaOrigen = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.UuidCfdi == uuidRelacionNormalizado && f.ProveedorId == command.ProveedorId)
            .Select(f => new { f.Id })
            .FirstOrDefaultAsync(cancellationToken);

        var ahora = _clock.UtcNow;
        var nc = global::Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
            empresaId: empresaId,
            cfdiRecibidoId: command.CfdiRecibidoId,
            uuidCfdi: command.UuidCfdi,
            proveedorId: command.ProveedorId,
            folioProveedor: command.FolioProveedor,
            serieProveedor: command.SerieProveedor,
            fechaCfdi: command.FechaCfdi,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            subtotal: command.Subtotal,
            impuestosTrasladados: command.ImpuestosTrasladados,
            retenciones: command.Retenciones,
            total: command.Total,
            tipo: command.Tipo,
            tipoRelacionCfdi: command.TipoRelacionCfdi,
            uuidRelacionCfdi: command.UuidRelacionCfdi,
            facturaOrigenId: facturaOrigen?.Id,
            capturadoPor: _currentUser.UserId,
            ahora: ahora);

        _db.NotasCreditoProveedor.Add(nc);

        await _mediator.Publish(new NotaCreditoProveedorRegistradaDomainEvent(
            EmpresaId: empresaId,
            NotaCreditoId: nc.Id,
            ProveedorId: nc.ProveedorId,
            FacturaOrigenId: nc.FacturaOrigenId,
            TipoRelacionCfdi: (int)nc.TipoRelacionCfdi,
            Total: nc.Total,
            OcurridoEn: ahora), cancellationToken);

        // F6-PR3: ciclo bidireccional con Almacén — si la NC tiene
        // TipoRelacionCfdi=03 (Devolucion) y resolvimos la factura
        // origen, buscamos una NotaCargo creada por una devolución a
        // proveedor cuyo factura_origen_id sea el mismo. Si la
        // encontramos y está Aplicada, formalizamos; en cualquier caso,
        // publicamos el evento para que Almacén marque la devolución
        // como conciliada con NC fiscal (ConciliadaConNcFiscal=true).
        if (command.TipoRelacionCfdi == TipoRelacionCfdi.Devolucion
            && facturaOrigen is not null)
        {
            var notaCargoMatch = await _db.NotasCargo
                .Where(n =>
                    n.FacturaOrigenId == facturaOrigen.Id
                    && n.DevolucionAProveedorId != null
                    && n.NotaCreditoProveedorId == null
                    && n.Estado != EstadoNotaCargo.Cancelada)
                .OrderBy(n => n.FechaCreacion)
                .FirstOrDefaultAsync(cancellationToken);

            if (notaCargoMatch is not null)
            {
                if (notaCargoMatch.Estado == EstadoNotaCargo.Aplicada)
                {
                    notaCargoMatch.Formalizar(nc.Id, ahora);
                }

                await _mediator.Publish(new NotaCreditoFiscalDevolucionRecibidaDomainEvent(
                    EmpresaId: empresaId,
                    NotaCreditoProveedorId: nc.Id,
                    NotaCargoId: notaCargoMatch.Id,
                    DevolucionAProveedorId: notaCargoMatch.DevolucionAProveedorId!.Value,
                    ProveedorId: nc.ProveedorId,
                    Total: nc.Total,
                    UuidCfdi: nc.UuidCfdi,
                    OcurridoEn: ahora), cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CapturarNotaCreditoResponse(nc.Id, nc.Estado, nc.FacturaOrigenId, nc.Version);
    }
}
