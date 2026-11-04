using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// 86e3xt2q8: Facturación (Caja) lee el buzón de CFDI recibidos de CxP por su
/// puerto público, sin tocar tablas ajenas, para elegir la factura mensual del banco.
/// </summary>
public sealed class CfdiRecibidoBancoReadAdapter(ICfdiRecibidoPublicReadPort cxp) : ICfdiRecibidoBancoReadPort
{
    public async Task<IReadOnlyList<CfdiRecibidoBanco>> ListarParaMesAsync(int anio, int mes, string? rfcEmisor, CancellationToken cancellationToken)
    {
        var desde = new DateTimeOffset(anio, mes, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-1);
        var hasta = new DateTimeOffset(anio, mes, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(2).AddDays(1);
        return (await cxp.ListarIngresosPorProcesarAsync(desde, hasta, rfcEmisor, 100, cancellationToken)).Select(Mapear).ToList();
    }

    public async Task<CfdiRecibidoBanco?> ObtenerAsync(Guid cfdiRecibidoId, CancellationToken cancellationToken) =>
        await cxp.ObtenerAsync(cfdiRecibidoId, cancellationToken) is { } c ? Mapear(c) : null;

    private static CfdiRecibidoBanco Mapear(CfdiRecibidoPublico c) => new(
        c.Id, c.Uuid, c.RfcEmisor, c.Serie, c.Folio, c.FechaCfdi, c.Subtotal, c.ImpuestosTrasladados, c.Total, c.Moneda, c.Tipo, c.Estado);
}
