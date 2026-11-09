using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Pedidos;

/// <summary>
/// FAC-08 (M7): IVA de la factura de una parcialidad (salida) del pedido.
/// </summary>
public static class IvaParcialidades
{
    /// <summary>
    /// IVA de una partida de la parcialidad, redondeado sobre el acumulado de la posición:
    /// <c>round((anterior + importe) × tasa) − round(anterior × tasa)</c>. Así la suma del IVA de
    /// las parcialidades es el IVA de la posición del pedido (sin el centavo de más que deja
    /// redondear cada parcialidad por separado). El resultado siempre es el piso o el techo de
    /// <c>importe × tasa</c>, dentro del rango que admite el SAT.
    /// </summary>
    public static decimal IvaAcumulado(decimal importeAnterior, decimal importe, decimal tasa)
        => Redondear((importeAnterior + importe) * tasa) - Redondear(importeAnterior * tasa);

    /// <summary>
    /// Ajusta el IVA de las partidas de la factura de una parcialidad con <see cref="IvaAcumulado"/>.
    /// Lo anterior son las salidas no descartadas con número de parcialidad menor (el mismo orden con
    /// que la salida reparte el importe: la que completa la posición se lleva el resto). Solo se ajusta
    /// la partida cuya base es el importe de la salida.
    /// </summary>
    public static async Task AjustarIvaAsync(
        FacturacionDbContext db, FacturaVenta factura, SalidaFacturable salida, CancellationToken cancellationToken)
    {
        var anteriores = (await db.SalidasFacturables.AsNoTracking()
                .Where(s => s.PedidoFacturableId == salida.PedidoFacturableId
                    && s.NumeroParcialidad < salida.NumeroParcialidad && s.DescartadaEn == null)
                .SelectMany(s => s.Lineas)
                .Select(l => new { l.Posicion, l.Importe })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.Posicion)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Importe));

        var lineasSalida = salida.Lineas.OrderBy(l => l.Posicion).ToArray();
        var lineasFactura = factura.Lineas.OrderBy(l => l.Posicion).ToArray();
        for (var i = 0; i < lineasSalida.Length && i < lineasFactura.Length; i++)
        {
            var ls = lineasSalida[i];
            var lf = lineasFactura[i];
            if (lf.TasaIvaTraslado is not { } tasa || tasa == 0m || lf.Importe - lf.Descuento != ls.Importe) continue;
            var iva = IvaAcumulado(anteriores.GetValueOrDefault(ls.Posicion), ls.Importe, tasa);
            if (iva != lf.ImpuestoTrasladadoImporte)
                factura.AjustarIvaParcialidad(lf.Posicion, iva);
        }
    }

    private static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
