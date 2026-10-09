using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Compras;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;

public sealed record ResultadoConciliacion(decimal BaseEsperada, decimal Diferencia, string? Motivo);

/// <summary>D18/D19: cantidades pendientes y precios por línea, sin compensaciones entre líneas.</summary>
public static class ConciliacionFacturaOc
{
    public static ResultadoConciliacion Conciliar(
        CapturarFacturaConOcCommand factura, OrdenCompraDto oc, Tolerancia tolerancia,
        IReadOnlyDictionary<Guid, decimal> yaFacturado, DatosCfdiParseados? xml = null)
    {
        ResultadoConciliacion Rechazar(string motivo, decimal diferencia = 0) => new(0, diferencia, motivo);
        bool Cuadra(decimal a, decimal b) => tolerancia.Pasa(a - b, b);
        if (!Cuadra(factura.Total, factura.Subtotal - factura.Descuentos + factura.ImpuestosTrasladados - factura.Retenciones))
            return Rechazar("El total no cuadra con subtotal − descuentos + traslados − retenciones.");
        if (!Cuadra(factura.Subtotal, factura.Lineas.Sum(l => l.Importe)))
            return Rechazar("El subtotal no cuadra con la suma de los importes de las líneas.");
        if (factura.Descuentos > factura.Subtotal || factura.Lineas.Any(l => l.Descuento < 0 || l.Descuento > l.Importe) ||
            factura.Lineas.Sum(l => l.Descuento ?? 0) > factura.Descuentos)
            return Rechazar("El descuento de las líneas no cuadra con el descuento de cabecera.");
        if (xml is not null)
        {
            var importes = new[] {
                ("subtotal", factura.Subtotal, xml.Subtotal), ("descuento", factura.Descuentos, xml.Descuentos),
                ("traslados", factura.ImpuestosTrasladados, xml.ImpuestosTrasladados),
                ("retenciones", factura.Retenciones, xml.Retenciones), ("total", factura.Total, xml.Total) };
            foreach (var (nombre, capturado, fiscal) in importes)
                if (!Cuadra(capturado, fiscal)) return Rechazar($"El {nombre} capturado ({capturado:N2}) no coincide con el XML ({fiscal:N2}).");
            if (xml.Lineas.Count != factura.Lineas.Count)
                return Rechazar("La cantidad de líneas no coincide con el XML ligado.");
            for (var i = 0; i < factura.Lineas.Count; i++)
            {
                var l = factura.Lineas[i]; var x = xml.Lineas[i];
                if (l.Cantidad != x.Cantidad || l.ClaveUnidad != x.ClaveUnidad ||
                    !Cuadra(l.Importe, x.Importe) || !Cuadra(l.PrecioUnitario * l.Cantidad, x.ValorUnitario * x.Cantidad) ||
                    !Cuadra(l.Descuento ?? 0, x.Descuento ?? 0))
                    return Rechazar($"La línea {i + 1}: cantidad, unidad, precio, importe o descuento no coincide con el XML.");
            }
        }
        for (var i = 0; i < factura.Lineas.Count; i++)
        {
            var l = factura.Lineas[i];
            if (l.LineaOcId is null) return Rechazar($"La línea {i + 1} no tiene una línea de OC vinculada.");
            if (!Cuadra(l.Importe, l.Cantidad * l.PrecioUnitario))
                return Rechazar($"La línea {i + 1}: el importe no cuadra con cantidad × precio unitario.");
        }
        var compensaciones = (factura.NotasCredito ?? []).SelectMany(n => n.Lineas)
            .GroupBy(l => l.LineaOcId).ToDictionary(g => g.Key, g => g.Sum(l => l.Base));
        if (compensaciones.Keys.Any(id => !factura.Lineas.Any(l => l.LineaOcId == id)))
            return Rechazar("La NC compensa una línea de OC que no está en esta factura.");
        decimal esperado = 0, diferenciaPrecio = 0, diferenciaBase = 0;
        var descuentoGlobal = factura.Descuentos - factura.Lineas.Sum(l => l.Descuento ?? 0);
        foreach (var grupo in factura.Lineas.GroupBy(l => l.LineaOcId!.Value))
        {
            var lineaOc = oc.Lineas.Single(l => l.Id == grupo.Key);
            var posicion = factura.Lineas.ToList().FindIndex(l => l.LineaOcId == grupo.Key) + 1;
            var cantidad = grupo.Sum(l => l.Cantidad);
            var pendiente = lineaOc.Cantidad - yaFacturado.GetValueOrDefault(grupo.Key);
            if (cantidad > pendiente)
                return Rechazar($"Línea {posicion}: cantidad facturada {cantidad:N4} mayor a la pendiente {Math.Max(0, pendiente):N4} de la OC {oc.Folio}.");
            if (grupo.Any(l => l.ArticuloId.HasValue && l.ArticuloId != lineaOc.ArticuloId))
                return Rechazar($"Línea {posicion}: el artículo no coincide con la línea de OC.");
            var nc = compensaciones.GetValueOrDefault(grupo.Key);
            var diferencias = grupo.Select(l => l.Cantidad * (l.PrecioUnitario - lineaOc.PrecioUnitario)).ToList();
            var precioDiff = Math.Abs(diferencias.Where(d => d >= 0).Sum() - nc) + Math.Abs(diferencias.Where(d => d < 0).Sum());
            diferenciaPrecio += Math.Abs(precioDiff);
            var baseEsperada = cantidad * (lineaOc.BaseNetaUnitaria ?? lineaOc.PrecioUnitario);
            esperado += baseEsperada;
            var bruto = grupo.Sum(l => l.Importe);
            var baseFactura = bruto - grupo.Sum(l => l.Descuento ?? 0) -
                (factura.Subtotal == 0 ? 0 : descuentoGlobal * bruto / factura.Subtotal) - nc;
            diferenciaBase += Math.Abs(baseFactura - baseEsperada);
            if (!tolerancia.Pasa(diferenciaPrecio, esperado))
            {
                var distinta = grupo.FirstOrDefault(l => l.PrecioUnitario != lineaOc.PrecioUnitario) ?? grupo.First();
                var posicionPrecio = factura.Lineas.ToList().IndexOf(distinta) + 1;
                return new(esperado, diferenciaPrecio, $"Precio distinto en la línea {posicionPrecio}: OC {lineaOc.PrecioUnitario:N4}; factura {distinta.PrecioUnitario:N4}. Factura − NC difiere {precioDiff:N2}; corrige o cancela la OC.");
            }
            if (!tolerancia.Pasa(diferenciaBase, esperado))
                return new(esperado, diferenciaBase, $"Línea {posicion}: base después de descuentos y NC {baseFactura:N2} distinta de lo pactado {baseEsperada:N2}.");
        }
        return new(esperado, Math.Max(diferenciaPrecio, diferenciaBase), null);
    }
}
