using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Sustituciones;

/// <summary>
/// Borrador de la factura sustituta (FAC-08b): los datos de la original para corregirlos
/// en el formulario de emisión. Valida que la original se pueda sustituir.
/// </summary>
public sealed record DatosSustitucionQuery(Guid FacturaId) : IRequest<DatosSustitucionResponse>;

public sealed record DatosSustitucionResponse(
    Guid SustituyeComprobanteId,
    string SustituyeFolio,
    string SustituyeUuid,
    Guid SucursalId,
    Guid? ClienteId,
    string ReceptorRfc,
    string ReceptorNombre,
    string ReceptorRegimenFiscal,
    string ReceptorCodigoPostal,
    string ReceptorUsoCfdi,
    string MetodoPago,
    string FormaPago,
    string Moneda,
    decimal? TipoCambio,
    short? CanalVenta,
    int ComportamientoFiscal,
    string? ObraNombre,
    string? OrdenCompraCliente,
    string? ReferenciaCliente,
    string? CondicionesPago,
    string? Proforma,
    string? Observaciones,
    IReadOnlyList<DatosSustitucionLinea> Lineas);

public sealed record DatosSustitucionLinea(
    Guid? ProductoId,
    string ClaveProdServSat,
    string Descripcion,
    string ClaveUnidadSat,
    decimal Cantidad,
    decimal ValorUnitario,
    decimal Descuento,
    string ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    bool RequierePedimento);

public sealed class DatosSustitucionHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    : IRequestHandler<DatosSustitucionQuery, DatosSustitucionResponse>
{
    public async Task<DatosSustitucionResponse> Handle(DatosSustitucionQuery query, CancellationToken cancellationToken)
    {
        // Fuera de alcance → mismo 404 que inexistente (12-cajas.md §4.1).
        var visible = await (await alcance.ResolverAsync(cancellationToken))
            .AplicarA(db.FacturasVenta.AsNoTracking())
            .AnyAsync(f => f.Id == query.FacturaId, cancellationToken);
        if (!visible)
            throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura '{query.FacturaId}'.");

        var f = await SustitucionCfdi.CargarSustituibleAsync(db, query.FacturaId, cancellationToken);
        await db.Entry(f).Collection(x => x.Lineas).LoadAsync(cancellationToken);

        return new DatosSustitucionResponse(
            f.Id, f.Folio, f.Uuid!, f.SucursalId,
            await ClienteContableFacturacion.ResolverAsync(db, f, cancellationToken),
            f.ReceptorRfc, f.ReceptorNombre, f.ReceptorRegimenFiscal, f.ReceptorCodigoPostal, f.ReceptorUsoCfdi,
            f.MetodoPago, f.FormaPago, f.Moneda, f.TipoCambio, f.CanalVentaId, (int)f.ComportamientoFiscal,
            f.ObraNombre, f.OrdenCompraCliente, f.ReferenciaCliente, f.CondicionesPago, f.Proforma, f.Observaciones,
            f.Lineas.OrderBy(l => l.Posicion)
                .Select(l => new DatosSustitucionLinea(
                    l.ProductoId, l.ClaveProdServSat, l.Descripcion, l.ClaveUnidadSat, l.Cantidad, l.ValorUnitario,
                    l.Descuento, l.ObjetoImp, l.TasaIvaTraslado, l.TasaRetencionIva, l.TasaRetencionIsr, l.RequierePedimento))
                .ToList());
    }
}
