using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Comprobantes.Pdf;
using Millet.Facturacion.Domain.Anticipos;
using DominioCartaPorte = Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Comprobantes.Queries;

/// <summary>
/// PDF de un <see cref="Comprobante"/> (13-B/13-C — generaliza la query de
/// facturas de venta): factura de venta (bilingüe o térmica), factura de
/// anticipo, nota de crédito (bilingüe de concepto único; el formato se
/// ignora), complemento de pago (REPP) y Carta Porte, ambos con el diseño de la
/// factura (86e3tfdx0). Aplica alcance de cajas y familia esperada,
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
                    .Include(f => f.ComplementoCce!).ThenInclude(cce => cce.Lineas)
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

            case ReciboPago:
                var rp = await _db.RecibosPago.AsNoTracking()
                    .Include(r => r.FacturasPagadas)
                    .Include(r => r.Relaciones)
                    .FirstAsync(r => r.Id == query.ComprobanteId, cancellationToken);
                var facturaIds = rp.FacturasPagadas.Select(f => f.FacturaVentaId).Distinct().ToList();
                var folios = await _db.FacturasVenta.AsNoTracking()
                    .Where(f => facturaIds.Contains(f.Id))
                    .Select(f => new { f.Id, f.Folio })
                    .ToDictionaryAsync(f => f.Id, f => f.Folio, cancellationToken);
                return _pdf.Generar(CfdiPdfModelBuilder.DesdeReciboPago(rp, await DatosAsync(rp, cancellationToken), folios));
            case DominioCartaPorte.CartaPorte:
                var cp = await _db.CartasPorte.AsNoTracking()
                    .Include(x => x.Mercancias)
                    .Include(x => x.Relaciones)
                    .FirstAsync(x => x.Id == query.ComprobanteId, cancellationToken);
                // Catálogos sin filtro de activos: un vehículo u operador dado de baja sigue en su Carta Porte.
                var vehiculo = await _db.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.Id == cp.VehiculoId, cancellationToken);
                var operador = await _db.Operadores.AsNoTracking().FirstOrDefaultAsync(o => o.Id == cp.OperadorId, cancellationToken);
                var previa = cp.CartaPortePreviaId is { } previaId
                    ? await _db.CartasPorte.AsNoTracking().Where(x => x.Id == previaId).Select(x => x.Folio).FirstOrDefaultAsync(cancellationToken)
                    : null;
                return _pdf.Generar(CfdiPdfModelBuilder.DesdeCartaPorte(cp, vehiculo, operador, await DatosAsync(cp, cancellationToken), previa));

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
