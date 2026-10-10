using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Facturacion.Infrastructure.DatosMaestros;
public sealed class ValidacionAltaClienteFiscalAdapter(ICatalogosSatReadPort catalogos) : IValidacionAltaClienteFiscal
{
    public async Task ValidarAsync(AltaClienteFiscal datos, CancellationToken ct)
    {
        var receptor = new DatosFiscalesReceptor(datos.Rfc, datos.RazonSocial, datos.RegimenFiscal,
            datos.CodigoPostalFiscal, datos.UsoCfdi, datos.PaisResidencia ?? "MEX", DatosFiscalesReceptor.EsRfcGenerico(datos.Rfc));
        // El alta no tiene sucursal/operación. La igualdad del CP genérico con el emisor se verifica al emitir.
        var errores = ValidacionReceptorFiscal.Validar(receptor, datos.CodigoPostalFiscal,
            await catalogos.ExisteRegimenFiscalAsync(datos.RegimenFiscal, ct), await catalogos.ExisteUsoCfdiAsync(datos.UsoCfdi, ct)).ToList();
        if (!await catalogos.ExisteFormaPagoAsync(datos.FormaPago ?? "", ct))
            errores.Add(new("formaPagoDefault", "Elige una forma de pago activa del catálogo SAT."));
        if (datos.MetodoPago is not ("PUE" or "PPD"))
            errores.Add(new("metodoPagoDefault", "El método de pago debe ser PUE o PPD."));
        if (datos.MetodoPago == "PPD" && datos.FormaPago != "99")
            errores.Add(new("formaPagoDefault", "El método PPD requiere forma de pago 99 (Por definir)."));
        if (datos.MetodoPago == "PUE" && datos.FormaPago == "99")
            errores.Add(new("formaPagoDefault", "Con método PUE indica la forma de pago real; 99 solo aplica a PPD."));
        if (!await catalogos.ExisteMonedaAsync(datos.Moneda, ct))
            errores.Add(new("monedaDefault", "Elige una moneda activa del catálogo SAT."));
        if (errores.Count > 0) throw new BusinessRuleException("CLIENTE_FISCAL_INVALIDO", string.Join(" ", errores.Select(e => e.Motivo)));
    }
}
