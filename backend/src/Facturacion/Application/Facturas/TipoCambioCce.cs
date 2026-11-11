using System.Globalization;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas;

/// <summary>
/// <c>TipoCambioUSD</c> del Complemento de Comercio Exterior. El SAT lo valida (CCE121)
/// contra el tipo de cambio publicado en el DOF conforme al art. 20 del CFF: una factura
/// emitida el día X lleva el valor publicado en el DOF el día hábil bancario anterior a X
/// (el FIX que Banxico determinó un día hábil antes de esa publicación).
///
/// <para>
/// Esa regla es la misma «escalera» con la que se llena el catálogo
/// <c>compartido.tipos_cambio</c> (<c>EscaleraTipoCambio</c>, K10.6): el registro USD con
/// <c>Fecha</c> = X ya es el valor del DOF que rige X. Por eso el complemento toma el
/// valor del catálogo para la fecha de emisión (o del reintento) y no acepta otro:
/// un valor capturado a mano se rechaza antes de llamar al PAC.
/// </para>
/// </summary>
internal static class TipoCambioCce
{
    private const string Usd = "USD";

    /// <summary>Valor del catálogo USD para la fecha fiscal de <paramref name="fechaEmision"/>.</summary>
    public static async Task<decimal> DelDofAsync(
        ICatalogosSatReadPort catalogos, DateTimeOffset fechaEmision, CancellationToken ct)
    {
        var fecha = Timbrado.CfdiEmisionBuilder.DiaFiscal(fechaEmision);
        var valor = await catalogos.TipoCambioAsync(Usd, fecha, ct);
        if (valor is null or <= 0m)
            throw new BusinessRuleException("CCE_TC_DOF_NO_REGISTRADO",
                $"Falta el tipo de cambio USD del {fecha:dd/MM/yyyy} en el catálogo. " +
                "Regístralo en Catálogos (el publicado en el DOF el día hábil anterior) antes de timbrar la factura de exportación.");
        return valor.Value;
    }

    /// <summary>
    /// Timbrado en una fecha distinta de la emisión (reintento, pedimento): el CFDI lleva la
    /// fecha del intento, así que el <c>TipoCambioUSD</c> del complemento y el <c>TipoCambio</c>
    /// del comprobante salen del catálogo de ESE día. En USD ambos son el mismo valor. La factura
    /// debe estar en Borrador (ya reabierta) y con su complemento cargado.
    /// </summary>
    public static async Task AlinearConFechaDeTimbradoAsync(
        ICatalogosSatReadPort catalogos, Domain.Facturas.FacturaVenta factura, DateTimeOffset fechaTimbrado, CancellationToken ct)
    {
        if (factura.ComplementoCce is not { } cce) return;
        cce.ActualizarTcDof(await DelDofAsync(catalogos, fechaTimbrado, ct));
        if (factura.Moneda != "MXN")
            factura.ActualizarTipoCambioParaTimbrar(
                (await TipoCambioFactura.DelDiaAsync(catalogos, factura.Moneda, fechaTimbrado, ct))!.Value);
    }

    /// <summary>
    /// Resuelve el <c>TipoCambioUSD</c> de una emisión: sin captura (0) devuelve el del catálogo;
    /// con captura distinta del catálogo rechaza (el SAT no acepta otro valor).
    /// </summary>
    public static async Task<decimal> ResolverAsync(
        ICatalogosSatReadPort catalogos, decimal capturado, DateTimeOffset fechaEmision, CancellationToken ct)
    {
        var dof = await DelDofAsync(catalogos, fechaEmision, ct);
        if (capturado > 0m && capturado != dof)
        {
            var fecha = Timbrado.CfdiEmisionBuilder.DiaFiscal(fechaEmision);
            throw new BusinessRuleException("CCE_TC_DOF_NO_CORRESPONDE",
                $"El tipo de cambio DOF para el {fecha:dd/MM/yyyy} es {dof.ToString("0.0000##", CultureInfo.InvariantCulture)} " +
                $"y la factura trae {capturado.ToString("0.0000##", CultureInfo.InvariantCulture)}. " +
                "El complemento de comercio exterior usa el del catálogo (art. 20 CFF); actualiza el formulario y vuelve a emitir.");
        }
        return dof;
    }
}
