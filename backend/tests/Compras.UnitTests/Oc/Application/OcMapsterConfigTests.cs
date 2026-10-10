using System.Text.Json;
using Mapster;
using MapsterMapper;
using Millet.Compras.Application.Oc;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.UnitTests.Oc.Domain;

namespace Millet.Compras.UnitTests.Oc.Application;

public sealed class OcMapsterConfigTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Detalle_ConPropiedadesP2_ConservaContratoYMapeaLineas(bool conHistorial)
    {
        var oc = conHistorial ? P2FirmasYSaldoTests.ConRecepcion() : P2FirmasYSaldoTests.Crear();
        if (conHistorial)
            oc.SolicitarCancelacionConRecepciones(Guid.NewGuid(), DateTimeOffset.UtcNow,
                Guid.NewGuid(), "Cancelar faltante");
        if (!conHistorial) oc.ActualizarReferenciaProveedor("Contrato P2");
        var config = new TypeAdapterConfig();
        new OcMapsterConfig().Register(config);
        var response = new Mapper(config).Map<OrdenCompraResponse>(oc);

        Assert.Equal(oc.Id, response.Id);
        Assert.Equal(oc.Folio.Valor, response.Folio);
        Assert.Equal(oc.Estado, response.Estado);
        Assert.Equal(oc.CicloAutorizacion, response.CicloAutorizacion);
        var linea = Assert.Single(response.Lineas);
        Assert.Equal(oc.Lineas.Single().Id, linea.Id);
        Assert.Equal(10m, linea.Cantidad);
        Assert.Equal(1000m, linea.SubtotalLinea);
        Assert.Empty(response.Autorizaciones); // Los enriquece el handler.
        Assert.Empty(response.SolicitudesCancelacion);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response,
            JsonSerializerOptions.Web));
        foreach (var propiedad in new[] { "id", "folio", "estado", "version", "lineas", "adjuntos",
            "referenciaProveedor", "descuentoGlobalTipo", "gastosAdicionales", "redondeo",
            "cicloAutorizacion", "autorizaciones", "solicitudesCancelacion" })
            Assert.True(json.RootElement.TryGetProperty(propiedad, out _), propiedad);
    }
}
