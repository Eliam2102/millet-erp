using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas;

/// <summary>
/// U1.6 / Cuestionario 04: el TC es el del día de emisión de la factura.
/// Se valida antes de reservar folio o llamar al PAC y queda guardado en el
/// comprobante: reintentos, cobros y NC publican ese valor histórico.
/// </summary>
internal static class TipoCambioFactura
{
    private static readonly TimeZoneInfo ZonaFactura = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");

    public static async Task ValidarAsync(ICatalogosSatReadPort catalogos,
        string moneda, decimal? capturado, DateTimeOffset fechaEmision, CancellationToken ct)
    {
        if (string.Equals(moneda, "MXN", StringComparison.OrdinalIgnoreCase)) return;

        var fecha = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fechaEmision, ZonaFactura).DateTime);
        var diario = await catalogos.TipoCambioAsync(moneda, fecha, ct);
        if (diario is null or <= 0m)
            throw new BusinessRuleException("TIPO_CAMBIO_NO_REGISTRADO",
                $"Registra en Catálogos el tipo de cambio de {moneda} para {fecha:yyyy-MM-dd} antes de emitir.");
        if (capturado != diario)
            throw new BusinessRuleException("TIPO_CAMBIO_NO_CORRESPONDE_FECHA",
                $"El tipo de cambio de {moneda} debe coincidir con el registrado para {fecha:yyyy-MM-dd}.");
    }
}
