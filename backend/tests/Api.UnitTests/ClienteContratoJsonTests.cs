using System.Text.Json;
using Millet.Api.Endpoints.DatosMaestros;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;

namespace Millet.Api.UnitTests;

public sealed class ClienteContratoJsonTests
{
    [Theory]
    [InlineData(EstatusCatalogo.Activo, "Activo")]
    [InlineData(EstatusCatalogo.Inactivo, "Inactivo")]
    [InlineData(EstatusCatalogo.EnRevision, "EnRevision")]
    public void Listado_y_detalle_serializan_estatus_como_texto_sin_cambiar_origen(EstatusCatalogo estatus, string texto)
    {
        var id = Guid.NewGuid();
        var item = new ClienteItem(id, "F1A", null, "CLIENTE FICTICIO", null, null, null, "MXN", false, OrigenMaster.Aw, false, estatus);
        var detalle = new DatosMaestrosEndpoints.ClienteDetalle(id, "F1A", null, "CLIENTE FICTICIO", null, null, null,
            null, null, null, "MXN", false, OrigenMaster.Aw, null, null, null, null, null, null, null, false, estatus, 1, null);
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var json in new[] { JsonSerializer.Serialize(item, opciones), JsonSerializer.Serialize(detalle, opciones) })
        {
            using var documento = JsonDocument.Parse(json);
            Assert.Equal(texto, documento.RootElement.GetProperty("estatus").GetString());
            Assert.Equal((int)OrigenMaster.Aw, documento.RootElement.GetProperty("origen").GetInt32());
        }
    }
}
