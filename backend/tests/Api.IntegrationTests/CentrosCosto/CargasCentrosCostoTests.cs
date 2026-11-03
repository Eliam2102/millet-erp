using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.DatosMaestros;

namespace Millet.Api.IntegrationTests.CentrosCosto;

[Collection(ApiCompartida.Nombre)]
public sealed class CargasCentrosCostoTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public async Task Dim1_previa_no_escribe_confirmar_omite_invalido_y_repetir_no_cambia()
    {
        using var client = factory.CreateClientWithIdempotency();
        using var login = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin", EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        var clave = "CC" + Guid.NewGuid().ToString("N")[..8]; var csv = $"Clave,Nombre\n{clave},Dimensión ficticia\n{clave}E,";
        var previa = await CargasMaestrosEndpointsTests.CargarAsync(client, "centros-costo-dim1", csv, false);
        Assert.Equal(1, previa.Nuevos); Assert.Equal(1, previa.Errores);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();
        Assert.False(await db.Dim1s.AnyAsync(x => x.Clave == clave));
        var resultado = await CargasMaestrosEndpointsTests.CargarAsync(client, "centros-costo-dim1", csv, true, previa.Huella);
        Assert.Equal(1, resultado.Nuevos); Assert.Equal(1, resultado.Errores); Assert.True(await db.Dim1s.AnyAsync(x => x.Clave == clave));
        var repetida = await CargasMaestrosEndpointsTests.CargarAsync(client, "centros-costo-dim1", csv, true, previa.Huella);
        Assert.Equal(1, repetida.SinCambio); Assert.Equal(0, repetida.Nuevos);
    }
}
