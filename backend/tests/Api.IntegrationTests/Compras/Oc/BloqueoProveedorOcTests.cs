using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// 86e3tactf: bloqueo total del proveedor — escrita, corre en CI (no se corre en local por regla del owner del 30-oct).
public partial class OrdenesCompraEndpointsTests
{
    private const string ProveedoresDatosMaestros = "/api/v1/datos-maestros/proveedores";

    private async Task<Guid> SembrarProveedorActivoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var sufijo = Guid.NewGuid().ToString("N");
        var proveedor = new Proveedor(Guid.NewGuid(), $"BQ{sufijo}"[..12].ToUpperInvariant(), $"Herrajes DEMO {sufijo[..6]}",
            $"BQ{sufijo}"[..12].ToUpperInvariant(), TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        db.Proveedores.Add(proveedor);
        await db.SaveChangesAsync();
        return proveedor.Id;
    }

    // Criterios 2 y 3: OC nueva rechazada con PROVEEDOR_BLOQUEADO; desbloqueado vuelve a crear; el estatus de alta no cambia.
    [Fact]
    public async Task Oc_nueva_con_proveedor_bloqueado_se_rechaza_y_desbloqueado_se_crea()
    {
        var client = await CreateSuperAdminClientAsync();
        var proveedorId = await SembrarProveedorActivoAsync();

        var bloqueo = await client.PostAsJsonAsync($"{ProveedoresDatosMaestros}/{proveedorId}/bloquear", new { motivo = "Dirección revisa su cuenta" });
        Assert.Equal(HttpStatusCode.OK, bloqueo.StatusCode);

        var rechazada = await client.PostAsJsonAsync(EndpointBase, ValidBody() with { ProveedorId = proveedorId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazada.StatusCode);
        Assert.Equal("PROVEEDOR_BLOQUEADO", (await ReadJsonAsync(rechazada)).GetProperty("code").GetString());

        var detalle = await ReadJsonAsync(await client.GetAsync($"{ProveedoresDatosMaestros}/{proveedorId}"));
        Assert.True(detalle.GetProperty("bloqueado").GetBoolean());

        var desbloqueo = await client.PostAsJsonAsync($"{ProveedoresDatosMaestros}/{proveedorId}/desbloquear", new { motivo = "Ya se aclaró con el proveedor" });
        Assert.Equal(HttpStatusCode.OK, desbloqueo.StatusCode);

        var creada = await client.PostAsJsonAsync(EndpointBase, ValidBody() with { ProveedorId = proveedorId });
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
    }

    // Criterio B5: sin motivo no se bloquea. Y sin el permiso `datos_maestros.proveedores.bloquear` responde 403.
    [Fact]
    public async Task Bloquear_sin_motivo_es_error_de_validacion_y_sin_permiso_es_403()
    {
        var client = await CreateSuperAdminClientAsync();
        var proveedorId = await SembrarProveedorActivoAsync();

        var sinMotivo = await client.PostAsJsonAsync($"{ProveedoresDatosMaestros}/{proveedorId}/bloquear", new { motivo = "" });
        Assert.True(sinMotivo.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
        var lista = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"{ProveedoresDatosMaestros}?bloqueado=true&q=BQ");
        Assert.DoesNotContain(lista.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == proveedorId);

        var sinPermiso = _factory.CreateClient();
        var token = await FakeLoginAsync(sinPermiso, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        sinPermiso.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var respuesta = await sinPermiso.PostAsJsonAsync($"{ProveedoresDatosMaestros}/{proveedorId}/bloquear", new { motivo = "Dirección revisa su cuenta" });
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
