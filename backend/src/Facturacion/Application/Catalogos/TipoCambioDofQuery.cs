using MediatR;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Catalogos;

/// <summary>
/// <c>TipoCambioUSD</c> del Complemento de Comercio Exterior para una factura emitida hoy:
/// el valor del catálogo USD (<c>compartido.tipos_cambio</c>) para la fecha fiscal de hoy,
/// que por la escalera K10.6 es el publicado en el DOF el día hábil bancario anterior
/// (art. 20 CFF). El formulario de emisión lo muestra de solo lectura; la emisión lo vuelve
/// a tomar del catálogo. <c>Valor = null</c> si el catálogo aún no lo tiene.
/// </summary>
public sealed record TipoCambioDofQuery() : IRequest<TipoCambioDofResponse>;

public sealed record TipoCambioDofResponse(string Moneda, DateOnly Fecha, decimal? Valor);

public sealed class TipoCambioDofHandler(ICatalogosSatReadPort catalogos, IClock clock)
    : IRequestHandler<TipoCambioDofQuery, TipoCambioDofResponse>
{
    public async Task<TipoCambioDofResponse> Handle(TipoCambioDofQuery query, CancellationToken cancellationToken)
    {
        var fecha = Timbrado.CfdiEmisionBuilder.DiaFiscal(clock.UtcNow);
        var valor = await catalogos.TipoCambioAsync("USD", fecha, cancellationToken);
        return new TipoCambioDofResponse("USD", fecha, valor is > 0m ? valor : null);
    }
}
