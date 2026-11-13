using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Cobros;

/// <summary>
/// M-177 · factura timbrada de un albarán de Producto Terminado (la salida facturable con <c>Referencia</c> = folio del albarán, FAC-08) y
/// lo que la caja cobra de ella: total neto de NC timbradas, igual que <see cref="CobroMostradorRegistrador"/>. <c>CobroVigenteId</c>: la
/// factura ya tiene un cobro de caja vigente (Caja la cobró en mostrador o una liquidación anterior ya la registró).
/// </summary>
public sealed record FacturaAlbaranPt(string AlbaranFolio, Guid ComprobanteId, string Folio, decimal PorCobrar, Guid? CobroVigenteId);

public sealed record FacturasAlbaranesPtQuery(IReadOnlyList<string> AlbaranFolios) : IRequest<IReadOnlyList<FacturaAlbaranPt>>;

public sealed class FacturasAlbaranesPtHandler(FacturacionDbContext db) : IRequestHandler<FacturasAlbaranesPtQuery, IReadOnlyList<FacturaAlbaranPt>>
{
    public async Task<IReadOnlyList<FacturaAlbaranPt>> Handle(FacturasAlbaranesPtQuery q, CancellationToken cancellationToken) =>
        await FacturasAlbaranPt.ResolverAsync(db, q.AlbaranFolios, cancellationToken);
}

internal static class FacturasAlbaranPt
{
    public static async Task<IReadOnlyList<FacturaAlbaranPt>> ResolverAsync(FacturacionDbContext db, IReadOnlyCollection<string> folios,
        CancellationToken ct)
    {
        var buscados = folios.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct().ToList();
        if (buscados.Count == 0) return [];
        var facturas = await (
            from s in db.SalidasFacturables.AsNoTracking()
            join f in db.FacturasVenta.AsNoTracking() on s.Id equals f.SalidaFacturableId
            where s.Origen == OrigenSalidaFacturable.ProductoTerminado && s.DescartadaEn == null && buscados.Contains(s.Referencia)
                  && f.Estado == EstadoTimbrado.Timbrado
            select new { s.Referencia, f.Id, f.Folio, f.Total }).ToListAsync(ct);
        if (facturas.Count == 0) return [];

        var ids = facturas.Select(f => f.Id).ToList();
        var acreditado = await SaldoPorCobrar.AcreditadoPorFacturaAsync(db, ids, ct);
        var cobros = await db.CobrosMostrador.AsNoTracking()
            .Where(c => ids.Contains(c.ComprobanteId) && c.Estado == EstadoCobroMostrador.Registrado)
            .Select(c => new { c.ComprobanteId, c.Id }).ToListAsync(ct);
        return [.. facturas.GroupBy(f => f.Referencia).Select(g => g.First()).Select(f => new FacturaAlbaranPt(f.Referencia, f.Id, f.Folio,
            f.Total - acreditado.GetValueOrDefault(f.Id), cobros.FirstOrDefault(c => c.ComprobanteId == f.Id)?.Id))];
    }
}

/// <summary>Lo cobrado en ruta de un albarán. <c>TipoTarjeta</c>: <c>Credito</c> (forma 04) o <c>Debito</c> (forma 28).</summary>
public sealed record CobroRutaAlbaranPtInput(string AlbaranFolio, decimal Efectivo, decimal Voucher, string? VoucherReferencia,
    string? TipoTarjeta, string? TarjetaUltimos4);

public sealed record CobroRutaAlbaranPtResultado(string AlbaranFolio, Guid CobroId, bool YaExistia);

/// <summary>
/// M-177 · el chofer entrega en caja lo cobrado contra entrega: cada albarán se cobra sobre su factura timbrada en la sesión de caja
/// abierta del usuario, que debe ser de la sucursal de la hoja, con <c>Origen = LiquidacionRuta</c> (<see cref="LiquidarRutaCommand"/>, todo
/// o nada). Idempotente por factura: si ya tiene cobro vigente se devuelve ese y no se cobra otra vez.
/// </summary>
public sealed record RegistrarCobrosRutaPtCommand(Guid SucursalId, IReadOnlyList<CobroRutaAlbaranPtInput> Cobros)
    : IRequest<IReadOnlyList<CobroRutaAlbaranPtResultado>>;

public sealed class RegistrarCobrosRutaPtHandler(FacturacionDbContext db, ISender sender, ICurrentEmpresaContext empresa,
    ICurrentUserContext user, ISucursalesReadPort sucursales) : IRequestHandler<RegistrarCobrosRutaPtCommand, IReadOnlyList<CobroRutaAlbaranPtResultado>>
{
    public const string FormaEfectivo = "01", FormaCredito = "04", FormaDebito = "28";

    public async Task<IReadOnlyList<CobroRutaAlbaranPtResultado>> Handle(RegistrarCobrosRutaPtCommand c, CancellationToken cancellationToken)
    {
        var cobros = c.Cobros.Where(x => x.Efectivo + x.Voucher > 0m).ToList();
        if (cobros.Count == 0) return [];
        var (_, usuarioId) = ContextoRequerido.De(empresa, user);

        var facturas = (await FacturasAlbaranPt.ResolverAsync(db, [.. cobros.Select(x => x.AlbaranFolio)], cancellationToken))
            .ToDictionary(f => f.AlbaranFolio);
        var sinFactura = cobros.FirstOrDefault(x => !facturas.ContainsKey(x.AlbaranFolio.Trim()));
        if (sinFactura is not null)
            throw new BusinessRuleException("PT_ALBARAN_SIN_FACTURA",
                $"El albarán {sinFactura.AlbaranFolio} no tiene factura timbrada; factúralo antes de liquidar la ruta.");

        var resultado = cobros.Select(x => facturas[x.AlbaranFolio.Trim()])
            .Where(f => f.CobroVigenteId is not null)
            .Select(f => new CobroRutaAlbaranPtResultado(f.AlbaranFolio, f.CobroVigenteId!.Value, YaExistia: true)).ToList();
        var porCobrar = cobros.Where(x => facturas[x.AlbaranFolio.Trim()].CobroVigenteId is null).ToList();
        if (porCobrar.Count == 0) return resultado;

        var sesion = await db.CajaSesiones.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ResponsableUsuarioId == usuarioId && s.Estado == EstadoCajaSesion.Abierta, cancellationToken);
        if (sesion is null || sesion.SucursalId != c.SucursalId)
        {
            var nombre = await sucursales.ObtenerNombreAsync(c.SucursalId, cancellationToken) ?? "la sucursal de la hoja";
            throw new BusinessRuleException("COBRO_SIN_SESION_SUCURSAL",
                $"No tiene sesión de caja abierta en {nombre}; abra su sesión en esa sucursal para liquidar la ruta.");
        }

        var registrados = await sender.Send(new LiquidarRutaCommand([.. porCobrar.Select(x =>
            new LiquidacionRutaCobroInput(facturas[x.AlbaranFolio.Trim()].ComprobanteId, FormasPago(x)))]), cancellationToken);
        var porComprobante = registrados.Cobros.ToDictionary(r => r.ComprobanteId, r => r.Id);
        resultado.AddRange(porCobrar.Select(x =>
        {
            var f = facturas[x.AlbaranFolio.Trim()];
            return new CobroRutaAlbaranPtResultado(f.AlbaranFolio, porComprobante[f.ComprobanteId], YaExistia: false);
        }));
        return resultado;
    }

    internal static List<CobroFormaPagoInput> FormasPago(CobroRutaAlbaranPtInput x)
    {
        var formas = new List<CobroFormaPagoInput>();
        if (x.Efectivo > 0m) formas.Add(new CobroFormaPagoInput(FormaEfectivo, x.Efectivo));
        if (x.Voucher > 0m)
        {
            var forma = x.TipoTarjeta switch
            {
                "Credito" => FormaCredito,
                "Debito" => FormaDebito,
                _ => throw new BusinessRuleException("PT_LIQUIDACION_VOUCHER_TARJETA",
                    $"El voucher del albarán {x.AlbaranFolio} lleva el tipo de tarjeta (crédito o débito) para registrarlo en caja."),
            };
            formas.Add(new CobroFormaPagoInput(forma, x.Voucher, x.VoucherReferencia?.Trim(), TarjetaUltimos4: x.TarjetaUltimos4?.Trim()));
        }
        return formas;
    }
}
