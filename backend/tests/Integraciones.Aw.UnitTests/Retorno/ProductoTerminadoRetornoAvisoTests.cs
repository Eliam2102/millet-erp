using Millet.Integraciones.Aw.Infrastructure.Retorno;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

public sealed class ProductoTerminadoRetornoAvisoTests
{
    // Sin el en-ruta en EsAviso el aplicador (M-160) descarta el 72 sin escribirlo.
    [Theory]
    [InlineData(ProductoTerminadoRetornoLocalConsumer.EventTypeEnRuta)]
    [InlineData(ProductoTerminadoRetornoLocalConsumer.EventTypeFacturadoEntregado)]
    [InlineData(ProductoTerminadoRetornoLocalConsumer.EventTypeEstadoCambiado)]
    public void Avisos_de_PT_que_van_a_aw_retorno(string eventType) =>
        AwRetornoAplicador.EsAviso(eventType).Should().BeTrue();

    [Fact]
    public void Otro_evento_de_PT_no_es_aviso() =>
        AwRetornoAplicador.EsAviso("producto_terminado.pedido.otro.v1").Should().BeFalse();
}
