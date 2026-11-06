using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cce;
public sealed class DatosAduanalesCeTests
{
    [Fact]
    public void Partida_sin_revision_bloquea_y_datos_revisados_reemplazan_la_captura()
    {
        EmitirFacturaVentaCceLinea[] lineas = [new("CAPTURA", "OTRA", 10, 1, 10, true)];
        Assert.Throws<BusinessRuleException>(() => DatosAduanalesFactura.Aplicar(lineas, null));
        PartidaAduanalLectura[] revisadas = [new(1, "70071999", "06")];
        var salida = DatosAduanalesFactura.Aplicar(lineas, revisadas);
        Assert.Equal("70071999", salida[0].FraccionArancelaria); Assert.Equal("06", salida[0].UnidadAduana);
        Assert.Equal(10, salida[0].CantidadAduana);
    }
}
