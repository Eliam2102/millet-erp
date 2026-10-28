using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Reportes.CorteCaja;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// FIX-12: GET real del corte de caja + handler + adapters reales (nombre del
/// cajero desde identidad.usuarios, sucursal desde compartido.sucursales) +
/// PostgreSQL desechable. Todos los datos son ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CorteCajaEndpointsTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio FIX-12", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio FIX-12", "601", "97000");

    [Fact]
    public async Task Corte_resuelve_cajero_y_sucursal_y_cuadra_con_los_movimientos()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var cajero = await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().Usuarios
            .AsNoTracking().FirstAsync(u => u.EntraOid == "dev-superadmin");

        var ahora = DateTimeOffset.UtcNow;
        var factura = Factura();
        var caja = Caja.Crear(Empresa, $"FIX-12-{Guid.NewGuid():N}", null);
        var sesion = CajaSesion.Abrir(Empresa, caja.Id, TestComprasFixtures.SucursalMid,
            cajero.Id, 300m, DateOnly.FromDateTime(ahora.UtcDateTime), ahora, null);
        var cobro = CobroMostrador.Registrar(Empresa, sesion.Id, factura.SucursalId, 1, factura.Id,
            OrigenCobroMostrador.Mostrador, ahora, cajero.Id,
            [("01", 60m, null, null, null), ("04", 56m, "FIX-AUT-12", null, null)]);
        var fondo = CajaMovimiento.Crear(Empresa, sesion.Id, TipoCajaMovimiento.FondoApertura,
            CajaSesion.FormaPagoEfectivo, 300m, cajero.Id, "Fondo de apertura");
        var movimientos = cobro.FormasPago.Select(f => CajaMovimiento.Crear(
            Empresa, sesion.Id, TipoCajaMovimiento.CobroCliente, f.FormaPago, f.Importe,
            cajero.Id, $"Cobro {factura.Folio}", f.Referencia, cobro.Id)).ToList();
        try
        {
            db.AddRange(factura, caja, sesion, cobro, fondo);
            db.AddRange(movimientos);
            await db.SaveChangesAsync();

            var corte = (await client.GetFromJsonAsync<CorteCajaResponse>(
                $"/api/v1/facturacion/cajas/sesiones/{sesion.Id}/corte"))!;

            Assert.Equal(cajero.Nombre, corte.Encabezado.Cajero);
            Assert.StartsWith("MID", corte.Encabezado.Sucursal);
            Assert.Equal(caja.Nombre, corte.Encabezado.Caja);
            Assert.Equal(3, corte.Reporte.Filas.Count);
            Assert.Equal(416m, corte.Reporte.Totales!["importe"]);
            Assert.Contains(corte.PorFormaPago, f => f.FormaPago == "01" && f.MontoSistema == 360m);
            Assert.Contains(corte.Reporte.Filas, f => f.Referencia == "FIX-AUT-12" && f.Importe == 56m);
            Assert.Single(corte.Reporte.Filas, f => f.TotalComprobante == 116m);
        }
        finally
        {
            // Se limpian cobro, movimientos, sesión, caja y factura: no alteran conteos de otras suites.
            await db.CajaMovimientos.Where(m => m.CajaSesionId == sesion.Id).ExecuteDeleteAsync();
            await db.CobrosMostrador.Where(c => c.Id == cobro.Id).ExecuteDeleteAsync();
            await db.CajaSesiones.Where(s => s.Id == sesion.Id).ExecuteDeleteAsync();
            await db.Cajas.Where(c => c.Id == caja.Id).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            db.Remove(await db.FacturasVenta.SingleAsync(f => f.Id == factura.Id));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Corte_de_sesion_inexistente_responde_404()
    {
        using var client = await Login();

        var response = await client.GetAsync($"/api/v1/facturacion/cajas/sesiones/{Guid.NewGuid()}/corte");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> Login()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    private static FacturaVenta Factura()
    {
        var f = FacturaVenta.CrearBorrador(Empresa, $"FIX12-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "PUE", "01", "MXN", null,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "FIX prueba ficticia FIX-12", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        return f;
    }
}
