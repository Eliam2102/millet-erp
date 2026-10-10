using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth.Models;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Facturas;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.P6;

public sealed class P6FormasPagoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Desactivar_02_audita_el_cambio_la_oculta_y_Caja_y_Facturacion_rechazan()
    {
        var client = factory.CreateClientWithIdempotency();
        var loginRes = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev" });
        loginRes.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await loginRes.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        Guid id; bool estadoOriginal;
        using (var scope = factory.Services.CreateScope())
        {
            var forma = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().FormasPago.SingleAsync(x => x.ClaveSat == "02");
            id = forma.Id; estadoOriginal = forma.Activa;
        }
        try
        {
            var cambiar = await client.PatchAsJsonAsync($"/api/v1/catalogos/formas-pago/{id}/estado", new { Activa = false });
            Assert.Equal(HttpStatusCode.NoContent, cambiar.StatusCode);
            var activas = await client.GetFromJsonAsync<JsonElement>("/api/v1/catalogos/formas-pago");
            Assert.DoesNotContain(activas.EnumerateArray(), x => x.GetProperty("claveSat").GetString() == "02");
            var todas = await client.GetFromJsonAsync<JsonElement>("/api/v1/catalogos/formas-pago/administracion");
            Assert.False(todas.EnumerateArray().Single(x => x.GetProperty("claveSat").GetString() == "02").GetProperty("activa").GetBoolean());
            var caja = await client.PostAsJsonAsync("/api/v1/facturacion/cobros", new RegistrarCobroMostradorCommand(Guid.NewGuid(), [new CobroFormaPagoInput("02", 100m)]));
            await AssertFormaInvalidaAsync(caja);
            await AssertFormaInvalidaAsync(await client.PostAsJsonAsync($"/api/v1/facturacion/cajas/sesiones/{Guid.NewGuid()}/movimientos",
                new { Tipo = 2, FormaPago = "02", Importe = 100m, Descripcion = "Movimiento de prueba" }));
            var factura = new EmitirFacturaVentaCommand(Guid.Parse("00000005-0003-0000-0000-000000000001"),
                "XAXX010101000", "Público en general", "616", "97000", "S01", "MEX", "MIL010101AAA", "601",
                "PUE", "02", "MXN", null, 1, ComportamientoFiscal.MostradorInmediato, null, null, false,
                [new EmitirFacturaVentaLinea(null, "01010101", "Producto de prueba", "H87", 1m, 100m, 0m, "02", 0.16m, null, null)]);
            await AssertFormaInvalidaAsync(await client.PostAsJsonAsync("/api/v1/facturacion/facturas", factura));
            using var verificar = factory.Services.CreateScope();
            var cambios = await verificar.ServiceProvider.GetRequiredService<CoreDbContext>().AuditLog.AsNoTracking()
                .Where(x => x.Entidad == "FormaPago" && x.EntidadId == id).ToListAsync();
            Assert.Contains(cambios, x => x.Cambios.Contains("Activa", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/v1/catalogos/formas-pago/{id}/estado", new { Activa = true })).StatusCode);
            var reactivadas = await client.GetFromJsonAsync<JsonElement>("/api/v1/catalogos/formas-pago");
            Assert.Contains(reactivadas.EnumerateArray(), x => x.GetProperty("claveSat").GetString() == "02");
        }
        finally
        {
            // La clave compartida vuelve al estado inicial: no contamina otras suites.
            var restaurar = await client.PatchAsJsonAsync($"/api/v1/catalogos/formas-pago/{id}/estado", new { Activa = estadoOriginal });
            restaurar.EnsureSuccessStatusCode();
        }
    }
    private static async Task AssertFormaInvalidaAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORMA_PAGO_INVALIDA", body.GetProperty("code").GetString());
    }
}
