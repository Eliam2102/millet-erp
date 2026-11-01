using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// C7.1 (86e3kdxx4): el liberado sin facturar (pedidos a crédito en firme
/// sin CFDI vigente, leídos de Facturación por puerto) resta del crédito
/// disponible; la consulta y la liberación dan el mismo número (CA7.1) y
/// un pedido facturado no se cuenta dos veces (C7.1-a).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class LiberadoSinFacturarTests
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Liberaciones = "/api/v1/cuentas-por-cobrar/liberaciones";
    private readonly WebApplicationFactory<Program> _factory;

    public LiberadoSinFacturarTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task CA7_1_disponible_resta_liberado_sin_facturar_y_el_pedido_se_libera()
    {
        using var client = await Login();
        var cliente = Guid.NewGuid();
        var sufijo = Random.Shared.Next(100_000, 999_999).ToString();
        await Sembrar(cliente, sufijo);

        var credito = await client.GetFromJsonAsync<JsonElement>($"/api/v1/cuentas-por-cobrar/credito-disponible/{cliente}");
        var linea = credito.GetProperty("lineas")[0];
        Assert.False(credito.GetProperty("datoIncompleto").GetBoolean());
        Assert.Equal(40_000m, linea.GetProperty("facturado").GetDecimal());
        Assert.Equal(20_000m, linea.GetProperty("liberadoSinFactura").GetDecimal());   // C7.1-a: el facturado no suma aquí
        Assert.Equal(40_000m, linea.GetProperty("disponible").GetDecimal());

        var request = new HttpRequestMessage(HttpMethod.Post, Liberaciones)
        {
            Content = JsonContent.Create(new
            {
                pedidoRef = $"6100{sufijo}",
                clienteId = cliente,
                moneda = "MXN",
                montoPedido = 30_000m,
                overrideId = (Guid?)null,
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var decision = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, decision.StatusCode);
        var cuerpo = await decision.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40_000m, cuerpo.GetProperty("creditoDisponibleSnapshot").GetDecimal());
        Assert.Equal(1, cuerpo.GetProperty("resultado").GetInt32());       // Liberado
        Assert.Equal(2, cuerpo.GetProperty("reglaAplicada").GetInt32());   // Crédito
    }

    /// <summary>
    /// Línea MXN de 100,000; factura abierta de 40,000; un pedido a crédito
    /// de 20,000 en firme sin factura; uno ya facturado y uno de contado
    /// (ninguno de los dos cuenta).
    /// </summary>
    private async Task Sembrar(Guid cliente, string sufijo)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        cxc.LineasCredito.Add(LineaCredito.Crear(Empresa, cliente, "MXN", 100_000m, OrigenLineaCredito.Solunion, plazoDias: 45));
        var ahora = DateTimeOffset.UtcNow;
        cxc.FacturasCartera.Add(FacturaCartera.Crear(
            Empresa, Guid.NewGuid(), cliente, "XAXX010101000", "Cliente C7.1", Guid.NewGuid().ToString(),
            $"C71-{sufijo}", 40_000m, "MXN", "PPD", ahora, ahora.AddDays(45)));
        await cxc.SaveChangesAsync();

        var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        fac.PedidosFacturables.Add(Pedido(cliente, $"6101{sufijo}", 20_000m, "30D"));
        var facturado = Pedido(cliente, $"6102{sufijo}", 40_000m, "30D");
        facturado.MarcarFacturado(Guid.NewGuid());
        fac.PedidosFacturables.Add(facturado);
        fac.PedidosFacturables.Add(Pedido(cliente, $"6103{sufijo}", 15_000m, "CONTADO"));
        await fac.SaveChangesAsync();
    }

    private static PedidoFacturable Pedido(Guid cliente, string numero, decimal importe, string condicion)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(Empresa, numero, Guid.NewGuid(), cliente, "Cliente C7.1", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "42", condicionPagoOrigen: condicion);
        pedido.AgregarLinea(null, "Vidrio", null, "H87", 1m, importe, 0m, false);
        pedido.RecalcularTotal();
        return pedido;
    }

    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba C7.1", EmpresaId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
