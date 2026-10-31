using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Reportes.CorteCaja;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// 86e3tfdvt (Millet, 30-oct): del voucher se capturan el número de
/// autorización y los últimos 4 dígitos de la tarjeta, uno por cada pago con
/// tarjeta. POST real del cobro + corte sobre PostgreSQL desechable. Datos ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CobroVoucherTarjetaHttpTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio voucher", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio voucher", "601", "97000");

    [Fact]
    public async Task Cobro_con_tarjeta_exige_los_ultimos_4_y_guarda_un_voucher_por_tarjeta()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var cajero = await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().Usuarios
            .AsNoTracking().FirstAsync(u => u.EntraOid == "dev-superadmin");

        var ahora = DateTimeOffset.UtcNow;
        var factura = Factura();
        var caja = Caja.Crear(Empresa, $"VOUCHER-{Guid.NewGuid():N}", null);
        var sesion = CajaSesion.Abrir(Empresa, caja.Id, TestComprasFixtures.SucursalMid,
            cajero.Id, 300m, DateOnly.FromDateTime(ahora.UtcDateTime), ahora, null);
        try
        {
            db.AddRange(factura, caja, sesion);
            await db.SaveChangesAsync();

            // Sin los últimos 4 dígitos: rechazo y nada guardado.
            var sinUltimos4 = await client.PostAsJsonAsync("/api/v1/facturacion/cobros",
                new RegistrarCobroMostradorCommand(factura.Id, [new CobroFormaPagoInput("04", 116m, "123456")]));
            await AssertCodigoAsync(sinUltimos4, "COBRO_TARJETA_SIN_ULTIMOS4");

            // Con 3 dígitos: rechazo.
            var tresDigitos = await client.PostAsJsonAsync("/api/v1/facturacion/cobros",
                new RegistrarCobroMostradorCommand(factura.Id,
                    [new CobroFormaPagoInput("04", 116m, "123456", TarjetaUltimos4: "123")]));
            await AssertCodigoAsync(tresDigitos, "COBRO_TARJETA_ULTIMOS4_INVALIDOS");
            Assert.False(await db.CobrosMostrador.AnyAsync(c => c.ComprobanteId == factura.Id));

            // Pago mixto con dos tarjetas: un voucher por cada una.
            var mixto = await client.PostAsJsonAsync("/api/v1/facturacion/cobros",
                new RegistrarCobroMostradorCommand(factura.Id,
                [
                    new CobroFormaPagoInput("01", 16m),
                    new CobroFormaPagoInput("04", 60m, "AUT-01", TarjetaUltimos4: "1234"),
                    new CobroFormaPagoInput("28", 40m, "AUT-02", TarjetaUltimos4: "5678"),
                ]));
            Assert.Equal(HttpStatusCode.Created, mixto.StatusCode);

            var detalle = (await client.GetFromJsonAsync<Millet.Facturacion.Application.Facturas.Queries.ComprobanteDetalleResponse>(
                $"/api/v1/facturacion/facturas/{factura.Id}"))!;
            Assert.Contains(detalle.CobroMostrador!.FormasPago,
                p => p.FormaPago == "04" && p.Referencia == "AUT-01" && p.TarjetaUltimos4 == "1234");
            Assert.Contains(detalle.CobroMostrador.FormasPago,
                p => p.FormaPago == "28" && p.Referencia == "AUT-02" && p.TarjetaUltimos4 == "5678");
            Assert.Contains(detalle.CobroMostrador.FormasPago, p => p.FormaPago == "01" && p.TarjetaUltimos4 == null);

            var corte = (await client.GetFromJsonAsync<CorteCajaResponse>(
                $"/api/v1/facturacion/cajas/sesiones/{sesion.Id}/corte"))!;
            Assert.Contains(corte.Reporte.Filas, f => f.Referencia == "AUT-01 · **** 1234" && f.Importe == 60m);
            Assert.Contains(corte.Reporte.Filas, f => f.Referencia == "AUT-02 · **** 5678" && f.Importe == 40m);
        }
        finally
        {
            // Se limpian cobro, movimientos, sesión, caja y factura: no alteran conteos de otras suites.
            db.ChangeTracker.Clear();
            var cobros = db.CobrosMostrador.Where(c => c.ComprobanteId == factura.Id).Select(c => c.Id);
            await db.CajaMovimientos.Where(m => m.CajaSesionId == sesion.Id).ExecuteDeleteAsync();
            await db.Set<CobroMostradorFormaPago>().Where(f => cobros.Contains(f.CobroMostradorId)).ExecuteDeleteAsync();
            await db.CobrosMostrador.Where(c => c.ComprobanteId == factura.Id).ExecuteDeleteAsync();
            await db.CajaSesiones.Where(s => s.Id == sesion.Id).ExecuteDeleteAsync();
            await db.Cajas.Where(c => c.Id == caja.Id).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            db.Remove(await db.FacturasVenta.SingleAsync(f => f.Id == factura.Id));
            await db.SaveChangesAsync();
        }
    }

    private static async Task AssertCodigoAsync(HttpResponseMessage response, string codigo)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(codigo, body.GetProperty("code").GetString());
    }

    private async Task<HttpClient> Login()
    {
        var client = factory.CreateClientWithIdempotency();
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
        var f = FacturaVenta.CrearBorrador(Empresa, $"VOU-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "PUE", "01", "MXN", null,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Prueba ficticia del voucher", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        return f;
    }
}
