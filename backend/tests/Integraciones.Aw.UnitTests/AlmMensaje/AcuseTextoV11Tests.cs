using Millet.Integraciones.Aw.Infrastructure.AlmCompra;
using Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

namespace Millet.Integraciones.Aw.UnitTests.AlmMensaje;

/// <summary>
/// v11 §3: <c>acuse_texto</c> es NVARCHAR(1000) en las dos tablas del canal. Un texto más largo trona el UPDATE y dejaría el mensaje
/// sin acuse, que para Almacenes es un mensaje atorado. El recorte es la red: se prueba aquí porque el resto del adaptador es SQL.
/// </summary>
public sealed class AcuseTextoV11Tests
{
    [Fact]
    public void Un_error_mas_largo_que_la_columna_se_recorta_a_1000()
    {
        var largo = new string('x', SqlAlmMensajeOrigen.MaxTexto + 500);

        var recortado = SqlAlmMensajeOrigen.TextoAcuse(largo);

        Assert.Equal(SqlAlmMensajeOrigen.MaxTexto, recortado!.Length);
        Assert.Equal(largo[..SqlAlmMensajeOrigen.MaxTexto], recortado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No existe la sucursal 'CONKAL'.")]
    public void Un_error_que_cabe_viaja_tal_cual(string? error)
    {
        Assert.Equal(error, SqlAlmMensajeOrigen.TextoAcuse(error));
    }

    [Fact]
    public void Justo_en_el_limite_no_se_toca()
    {
        var exacto = new string('y', SqlAlmMensajeOrigen.MaxTexto);

        Assert.Equal(exacto, SqlAlmMensajeOrigen.TextoAcuse(exacto));
    }

    [Fact]
    public void El_acuse_que_el_ERP_lee_de_alm_compra_usa_el_mismo_limite()
    {
        // alm_compra: el acuse lo escribe Almacenes y el ERP solo lo lee, pero lo guarda en su bandeja con el mismo tope.
        Assert.Equal(1000, SqlAlmMensajeOrigen.MaxTexto);
        Assert.Equal(SqlAlmMensajeOrigen.MaxTexto, SqlAlmCompraTransporte.MaxTexto);
    }
}
