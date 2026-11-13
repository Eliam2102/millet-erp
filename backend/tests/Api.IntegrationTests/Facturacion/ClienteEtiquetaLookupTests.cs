using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Facturacion;

[Collection(ApiCompartida.Nombre)]
public sealed class ClienteEtiquetaLookupTests(WebApplicationFactory<Program> factory)
{
    [Theory]
    [InlineData(EstatusCatalogo.Activo)]
    [InlineData(EstatusCatalogo.Inactivo)]
    public async Task Etiqueta_por_ID_solo_requiere_emitir_y_no_agrega_inactivos_al_listado(EstatusCatalogo estatus)
    {
        await using var app = factory.ConEmpresa(EmpresaBootstrapId);
        using var emisor = await ClienteConPermisosAsync(app, PermisosCanonicos.FacturacionFacturasEmitir);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var clave = "FRIO" + Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente(Guid.NewGuid(), clave, "CLIENTE FICTICIO DE ETIQUETA", estatus: estatus);
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        try
        {
            using var response = await emisor.GetAsync($"/api/v1/facturacion/catalogos/clientes/{cliente.Id}");
            response.EnsureSuccessStatusCode();
            var json = await Json(response);
            Assert.Equal(cliente.Id, json.GetProperty("id").GetGuid());
            Assert.Equal(clave, json.GetProperty("clave").GetString());
            Assert.Equal(cliente.RazonSocial, json.GetProperty("razonSocial").GetString());
            Assert.Equal(3, json.EnumerateObject().Count());
            using var busqueda = await emisor.GetAsync($"/api/v1/facturacion/catalogos/clientes?q={clave}");
            busqueda.EnsureSuccessStatusCode();
            Assert.Equal(estatus == EstatusCatalogo.Activo ? 1 : 0, (await Json(busqueda)).GetArrayLength());
            Assert.Equal(estatus, (await db.Clientes.FindAsync(cliente.Id))!.Estatus);
        }
        finally { db.Clientes.Remove(cliente); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Identidad_ausente_es_404_y_sin_permiso_de_emitir_es_403()
    {
        await using var app = factory.ConEmpresa(EmpresaBootstrapId);
        using var emisor = await ClienteConPermisosAsync(app, PermisosCanonicos.FacturacionFacturasEmitir);
        using var lector = await ClienteConPermisosAsync(app, PermisosCanonicos.FacturacionFacturasLeer);
        using var anonimo = app.CreateClient();
        var ruta = $"/api/v1/facturacion/catalogos/clientes/{Guid.NewGuid()}";
        using var ausente = await emisor.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.NotFound, ausente.StatusCode);
        Assert.Equal("CLIENTE_NO_ENCONTRADO", (await Json(ausente)).GetProperty("code").GetString());
        using var prohibida = await lector.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.Forbidden, prohibida.StatusCode);
        using var noAutenticada = await anonimo.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.Unauthorized, noAutenticada.StatusCode);
    }
}
