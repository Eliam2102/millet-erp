using FluentValidation;
using MediatR;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;

/// <summary>
/// Comando de captura de una factura de proveedor conciliada con una
/// OC (§7.1 del 01-diseno, F3-PR1). El handler:
/// <list type="number">
///   <item>Resuelve la OC vía <c>IComprasOcReadPort</c>.</item>
///   <item>Resuelve la tolerancia del proveedor (master + default
///         global del módulo).</item>
///   <item>Concilia cantidades pendientes y precios por línea con la OC; si
///         pasa la tolerancia, persiste como
///         <see cref="EstadoPasivo.Capturada"/>; si no pasa, cancela
///         con motivo <c>RechazadaPorTolerancia</c>.</item>
///   <item>Persiste snapshot de tolerancia + diferencia +
///         redondeo aplicado.</item>
/// </list>
///
/// <para>
/// **Sin EmpresaId del cliente**: lo resuelve el handler del JWT
/// (<c>ICurrentEmpresaContext.Current</c>).
/// </para>
/// </summary>
public sealed record CapturarFacturaConOcCommand(
    Guid OrdenCompraId,
    Guid ProveedorId,
    Guid SucursalId,
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaDocumento,
    DateTimeOffset FechaContabilizacion,
    DateOnly FechaVencimiento,
    string Moneda,
    decimal? TipoCambio,
    decimal Subtotal,
    decimal Descuentos,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    IReadOnlyList<CapturarFacturaConOcLinea> Lineas,
    // G1.6 (P2): desglose de retenciones del CFDI (ObtenerCfdiParseado). Null en captura manual.
    IReadOnlyList<RetencionCfdi>? RetencionesDetalle = null,
    IReadOnlyList<NotaCreditoAdjunta>? NotasCredito = null) : IRequest<CapturarFacturaConOcResponse>;

public sealed record NotaCreditoAdjunta(Guid CfdiRecibidoId, IReadOnlyList<CompensacionNcLinea> Lineas);
public sealed record CompensacionNcLinea(Guid LineaOcId, decimal Base);

public sealed record CapturarFacturaConOcLinea(
    Guid? ArticuloId,
    string? ClaveProdServ,
    string Descripcion,
    decimal Cantidad,
    string ClaveUnidad,
    string? Unidad,
    decimal PrecioUnitario,
    decimal Importe,
    decimal? Descuento,
    Guid? LineaOcId,
    Guid? ConceptoContableId);

public sealed record CapturarFacturaConOcResponse(
    Guid Id,
    EstadoPasivo Estado,
    decimal DiferenciaContraOc,
    decimal SaldoPendiente,
    MotivoCancelacion? MotivoCancelacion,
    int Version,
    string? MotivoCancelacionTexto = null);

public sealed class CapturarFacturaConOcValidator : AbstractValidator<CapturarFacturaConOcCommand>
{
    public CapturarFacturaConOcValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Total).GreaterThan(0);
        RuleFor(c => c.Subtotal).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Descuentos).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ImpuestosTrasladados).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Retenciones).GreaterThanOrEqualTo(0);
        RuleForEach(c => c.RetencionesDetalle).ChildRules(r =>
        {
            r.RuleFor(x => x.Impuesto).NotEmpty().MaximumLength(3);
            r.RuleFor(x => x.Importe).GreaterThanOrEqualTo(0);
        });
        RuleFor(c => c.NotasCredito).Must(n => n is null || n.Select(x => x.CfdiRecibidoId).Distinct().Count() == n.Count)
            .WithMessage("No adjuntes la misma nota de crédito más de una vez.");
        RuleForEach(c => c.NotasCredito).ChildRules(n =>
        {
            n.RuleFor(x => x.CfdiRecibidoId).NotEmpty();
            n.RuleFor(x => x.Lineas).NotEmpty();
            n.RuleForEach(x => x.Lineas).ChildRules(l =>
            {
                l.RuleFor(x => x.LineaOcId).NotEmpty();
                l.RuleFor(x => x.Base).GreaterThan(0);
            });
        });
        RuleFor(c => c.Lineas).NotEmpty().WithMessage("La factura debe tener al menos una línea.");
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.Descripcion).NotEmpty().MaximumLength(1000);
            l.RuleFor(x => x.Cantidad).GreaterThan(0);
            l.RuleFor(x => x.Importe).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.PrecioUnitario).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.Descuento).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.ClaveUnidad).NotEmpty().MaximumLength(40);
        });
    }
}
