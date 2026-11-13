using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Facturacion;

[Collection(ApiCompartida.Nombre)]
public sealed class IdentidadTerceroDetalleTests(WebApplicationFactory<Program> factory)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A21_Detalle_conserva_nombre_fiscal_historico_y_clave_nullable_sin_cruzar_empresa(bool conMaestro)
    {
        await using var app = factory.ConEmpresa(EmpresaBootstrapId);
        using var client = await LoginAsync(app);
        var clienteId = Guid.NewGuid();
        var clave = "A21C" + Guid.NewGuid().ToString("N")[..8];
        var rfc = "ZZZ" + Random.Shared.Next(0, 1_000_000).ToString("D6") + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var maestros = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var cliente = new Cliente(clienteId, clave, "NOMBRE ACTUAL DEL MAESTRO", rfc: rfc);
        if (conMaestro) { maestros.Clientes.Add(cliente); await maestros.SaveChangesAsync(); }
        var sucursal = await maestros.Sucursales.Select(s => s.Id).FirstAsync();
        var pedido = PedidoFacturable.ImportarDesdeAw(EmpresaBootstrapId, clave, sucursal, clienteId, "Cliente pedido",
            1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        FacturaVenta Crear(Guid empresa, Guid? pedidoId) => FacturaVenta.CrearBorrador(empresa, clave, 1, sucursal, null, null,
            new DatosFiscalesReceptor(rfc, "NOMBRE FISCAL HISTORICO", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio", "601", "97000"), "PUE", "01", "MXN", null,
            2026, 11, 1, ComportamientoFiscal.MostradorInmediato, pedidoId, null, null, false);
        var propia = Crear(EmpresaBootstrapId, pedido.Id);
        var ajena = Crear(Guid.NewGuid(), null);
        var directa = Crear(EmpresaBootstrapId, null);
        db.Entry(directa).Property(f => f.Folio).CurrentValue = clave + "D";
        db.Entry(ajena).Property(f => f.Folio).CurrentValue = clave + "X";
        db.PedidosFacturables.Add(pedido);
        db.FacturasVenta.AddRange(propia, ajena, directa);
        // Sólo prepara la muestra ajena; las consultas HTTP se hacen después, con su filtro real.
        using (scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>().Bypass())
            await db.SaveChangesAsync();
        try
        {
            using var respuesta = await client.GetAsync($"/api/v1/facturacion/facturas/{propia.Id}");
            respuesta.EnsureSuccessStatusCode();
            var json = await Json(respuesta);
            Assert.Equal(clienteId, json.GetProperty("clienteId").GetGuid());
            Assert.Equal(conMaestro ? clave : null, json.GetProperty("clienteClave").GetString());
            Assert.Equal("NOMBRE FISCAL HISTORICO", json.GetProperty("receptorNombre").GetString());
            using var sinPedido = await client.GetAsync($"/api/v1/facturacion/facturas/{directa.Id}");
            sinPedido.EnsureSuccessStatusCode();
            var jsonDirecta = await Json(sinPedido);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, jsonDirecta.GetProperty("clienteId").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, jsonDirecta.GetProperty("clienteClave").ValueKind);
            Assert.Equal("NOMBRE FISCAL HISTORICO", jsonDirecta.GetProperty("receptorNombre").GetString());
            using var prohibida = await client.GetAsync($"/api/v1/facturacion/facturas/{ajena.Id}");
            Assert.Equal(HttpStatusCode.NotFound, prohibida.StatusCode);
            Assert.Equal("NOMBRE FISCAL HISTORICO", (await db.FacturasVenta.AsNoTracking().SingleAsync(f => f.Id == propia.Id)).ReceptorNombre);
        }
        finally
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>().Bypass();
            db.FacturasVenta.RemoveRange(propia, ajena, directa);
            db.PedidosFacturables.Remove(pedido);
            await db.SaveChangesAsync();
            if (conMaestro) { maestros.Clientes.Remove(cliente); await maestros.SaveChangesAsync(); }
        }
    }
}
