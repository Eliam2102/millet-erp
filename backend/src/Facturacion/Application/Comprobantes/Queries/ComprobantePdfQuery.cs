using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Comprobantes.Pdf;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Comprobantes.Queries;

/// <summary>
/// PDF de un <see cref="Comprobante"/> (13-B/13-C — generaliza la query de
/// facturas de venta): factura de venta (bilingüe o térmica), factura de
/// anticipo y nota de crédito (bilingüe de concepto único; el formato se
/// ignora). REPP y carta porte no tienen representación PDF propia →
/// 422 <c>PDF_NO_SOPORTADO</c>. Aplica alcance de cajas y familia esperada,
/// igual que <see cref="ComprobanteXmlQuery"/>.
/// </summary>
public sealed record ComprobantePdfQuery(
    Guid ComprobanteId,
    FormatoPdfFactura Formato,
    FamiliaComprobante? FamiliaEsperada = null) : IRequest<PdfFacturaGenerado>;

public sealed class ComprobantePdfHandler : IRequestHandler<ComprobantePdfQuery, PdfFacturaGenerado>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly IGenerarPdfFacturaPort _pdf;
    private readonly IDatosImpresionCfdiReadPort _datosImpresion;

    public ComprobantePdfHandler(
        FacturacionDbContext db, IAlcanceCajaEvaluator alcance, IGenerarPdfFacturaPort pdf,
        IDatosImpresionCfdiReadPort datosImpresion)
    {
        _db = db;
        _alcance = alcance;
        _pdf = pdf;
        _datosImpresion = datosImpresion;
    }

    public async Task<PdfFacturaGenerado> Handle(ComprobantePdfQuery query, CancellationToken cancellationToken)
    {
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var c = await alcance.AplicarA(_db.Comprobantes.AsNoTracking())
            .FirstOrDefaultAsync(x => x.Id == query.ComprobanteId, cancellationToken);

        if (c is null || !ComprobanteXmlHandler.EsDeFamiliaEsperada(c, query.FamiliaEsperada))
            throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante '{query.ComprobanteId}'.");

        switch (c)
        {
            case FacturaVenta:
                var fv = await _db.FacturasVenta.AsNoTracking()
                    .Include(f => f.Lineas)
                    .Include(f => f.Relaciones)
                    .FirstAsync(f => f.Id == query.ComprobanteId, cancellationToken);
                if (query.Formato == FormatoPdfFactura.TermicaSimplificada)
                    return _pdf.Generar(fv, query.Formato);
                return _pdf.Generar(CfdiPdfModelBuilder.DesdeFactura(fv, await DatosAsync(fv, cancellationToken),
                    await _datosImpresion.CodigosArticuloAsync(ProductosDe(fv), cancellationToken)));

            case FacturaAnticipo:
                var fa = await _db.FacturasAnticipo.AsNoTracking()
                    .Include(a => a.Relaciones)
                    .FirstAsync(a => a.Id == query.ComprobanteId, cancellationToken);
                return _pdf.Generar(CfdiPdfModelBuilder.DesdeAnticipo(fa, await DatosAsync(fa, cancellationToken)));

            case NotaCredito:
                var nc = await _db.NotasCredito.AsNoTracking()
                    .Include(n => n.Relaciones)
                    .FirstAsync(n => n.Id == query.ComprobanteId, cancellationToken);
                return _pdf.Generar(CfdiPdfModelBuilder.DesdeNotaCredito(nc, await DatosAsync(nc, cancellationToken)));

            default:
                throw new BusinessRuleException(
                    "PDF_NO_SOPORTADO",
                    $"El tipo de comprobante '{c.GetType().Name}' no tiene representación PDF.");
        }
    }

    /// <summary>Domicilio, logo y catálogos del emisor para el formato de Millet (FMT-4).</summary>
    internal static Guid[] ProductosDe(FacturaVenta f) =>
        f.Lineas.Where(l => l.ProductoId is not null).Select(l => l.ProductoId!.Value).Distinct().ToArray();

    private Task<DatosImpresionCfdi> DatosAsync(Comprobante c, CancellationToken cancellationToken) =>
        _datosImpresion.ObtenerAsync(c.EmpresaId, cancellationToken);
}
