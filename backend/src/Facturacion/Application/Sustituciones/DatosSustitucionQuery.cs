using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Sustituciones;

/// <summary>
/// Borrador de la factura sustituta (FAC-08b): los datos de la original para corregirlos
/// en el formulario de emisión. Valida que la original se pueda sustituir.
/// </summary>
public sealed record DatosSustitucionQuery(Guid FacturaId, string Motivo = "01") : IRequest<DatosSustitucionResponse>;

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
    IReadOnlyList<DatosSustitucionLinea> Lineas,
    string Motivo = "01",
    bool LlevaRelacion04 = true,
    IReadOnlyList<string>? Pasos = null,
    bool RequiereAceptacion = true,
    IReadOnlyList<string>? RazonesSinAceptacion = null);

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
    bool RequierePedimento,
    int? DecimalesCantidad = null);

public sealed class DatosSustitucionHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance, IClock clock, IProductosReadPort? productos = null)
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

        var guia = GuiaMotivoCancelacion.Para(query.Motivo);
        if (!guia.EmiteNueva)
            throw new BusinessRuleException(
                "SUSTITUCION_MOTIVO_SIN_NUEVA", $"El motivo {guia.Motivo} no lleva factura nueva: solo se cancela.");
        var f = guia.CancelaPrimero
            ? await SustitucionCfdi.CargarCanceladaParaMotivoAsync(db, query.FacturaId, guia.Motivo, cancellationToken)
            : await SustitucionCfdi.CargarSustituibleAsync(db, query.FacturaId, cancellationToken);
        var aceptacion = PoliticaAceptacionCancelacion.Evaluar(f, clock.UtcNow);
        await db.Entry(f).Collection(x => x.Lineas).LoadAsync(cancellationToken);

        // El snapshot histórico permanece intacto; la precisión rige sólo el nuevo borrador.
        var precisiones = new Dictionary<Guid, int?>();
        if (productos is not null)
            foreach (var id in f.Lineas.Where(l => l.ProductoId.HasValue).Select(l => l.ProductoId!.Value).Distinct())
                precisiones[id] = (await productos.ObtenerAsync(id, cancellationToken))?.DecimalesCantidad;

        return new DatosSustitucionResponse(
            f.Id, f.Folio, f.Uuid!, f.SucursalId,
            await ClienteContableFacturacion.ResolverAsync(db, f, cancellationToken),
            f.ReceptorRfc, f.ReceptorNombre, f.ReceptorRegimenFiscal, f.ReceptorCodigoPostal, f.ReceptorUsoCfdi,
            f.MetodoPago, f.FormaPago, f.Moneda, f.TipoCambio, f.CanalVentaId, (int)f.ComportamientoFiscal,
            f.ObraNombre, f.OrdenCompraCliente, f.ReferenciaCliente, f.CondicionesPago, f.Proforma, f.Observaciones,
            f.Lineas.OrderBy(l => l.Posicion)
                .Select(l => new DatosSustitucionLinea(
                    l.ProductoId, l.ClaveProdServSat, l.Descripcion, l.ClaveUnidadSat, l.Cantidad, l.ValorUnitario,
                    l.Descuento, l.ObjetoImp, l.TasaIvaTraslado, l.TasaRetencionIva, l.TasaRetencionIsr, l.RequierePedimento,
                    l.ProductoId is Guid id ? precisiones.GetValueOrDefault(id) : null))
                .ToList(),
            guia.Motivo, guia.LlevaRelacion04, guia.Pasos, aceptacion.RequiereAceptacion, aceptacion.RazonesSinAceptacion);
    }
}
