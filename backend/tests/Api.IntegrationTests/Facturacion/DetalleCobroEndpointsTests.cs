using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>GET real + handler + PostgreSQL desechable. Todos los datos son ficticios.</summary>
public sealed class DetalleCobroEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio FAC-05", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio FAC-05", "601", "97000");

    [Fact]
    public async Task Pue_detalle_antes_despues_y_cancelacion_restaura_saldo()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var factura = Factura("PUE");
        var caja = Caja.Crear(Empresa, $"FIX-FAC05-{Guid.NewGuid():N}", null);
        var usuario = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;
        var sesion = CajaSesion.Abrir(Empresa, caja.Id, TestComprasFixtures.SucursalMid,
            usuario, 0, DateOnly.FromDateTime(ahora.UtcDateTime), ahora, null);
        var cobro = CobroMostrador.Registrar(Empresa, sesion.Id, factura.SucursalId, 1, factura.Id,
            OrigenCobroMostrador.Mostrador, ahora, usuario,
            [("01", 60m, null, null, null), ("04", 56m, "FIX-AUT", null, null)]);
        try
        {
            db.AddRange(factura, caja, sesion);
            await db.SaveChangesAsync();
            var antes = await Detalle(client, factura.Id);
            Assert.Equal("PUE", antes.MetodoPago);
            Assert.Null(antes.CobroMostrador);
            Assert.Equal(116m, antes.TotalPorCobrar);

            db.Add(cobro);
            await db.SaveChangesAsync();
            var despues = await Detalle(client, factura.Id);
            Assert.Equal(0m, despues.TotalPorCobrar);
            Assert.NotNull(despues.CobroMostrador);
            Assert.Equal(cobro.Id, despues.CobroMostrador.Id);
            Assert.Equal(usuario, despues.CobroMostrador.UsuarioCobradorId);
            Assert.Equal(ahora.ToUnixTimeMilliseconds(), despues.CobroMostrador.FechaCobro.ToUnixTimeMilliseconds());
            Assert.Equal(116m, despues.CobroMostrador.Total);
            Assert.Equal(sesion.Id, despues.CobroMostrador.Sesion.Id);
            Assert.Equal(caja.Nombre, despues.CobroMostrador.Sesion.CajaNombre);
            Assert.Contains(despues.CobroMostrador.FormasPago,
                p => p.FormaPago == "04" && p.Importe == 56m && p.Referencia == "FIX-AUT");

            cobro.Cancelar();
            await db.SaveChangesAsync();
            var cancelado = await Detalle(client, factura.Id);
            Assert.Null(cancelado.CobroMostrador);
            Assert.Equal(116m, cancelado.TotalPorCobrar);
        }
        finally
        {
            // También se limpian caja y sesión: no alteran conteos de otras suites.
            await db.CobrosMostrador.Where(c => c.Id == cobro.Id).ExecuteDeleteAsync();
            await db.CajaSesiones.Where(s => s.Id == sesion.Id).ExecuteDeleteAsync();
            await db.Cajas.Where(c => c.Id == caja.Id).ExecuteDeleteAsync();
            db.Remove(factura);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Ppd_con_rep_parcial_solo_descuenta_timbrados_y_recupera_saldo_al_cancelar_rep()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var factura = Factura("PPD");
        var parcial = Rep(factura, 40m, "Timbrado");
        var fallido = Rep(factura, 20m, "TimbradoFallido");
        var borrador = Rep(factura, 10m, "Borrador");
        try
        {
            db.AddRange(factura, parcial, fallido, borrador);
            await db.SaveChangesAsync();
            var detalle = await Detalle(client, factura.Id);
            Assert.Equal("PPD", detalle.MetodoPago);
            Assert.Null(detalle.CobroMostrador);
            Assert.Equal(40m, detalle.PagadoPorRep);
            Assert.Equal(76m, detalle.TotalPorCobrar);

            parcial.MarcarCancelacionPendiente();
            await db.SaveChangesAsync();
            var pendiente = await Detalle(client, factura.Id);
            Assert.Equal(40m, pendiente.PagadoPorRep);
            Assert.Equal(76m, pendiente.TotalPorCobrar);
            parcial.MarcarCancelado();
            await db.SaveChangesAsync();
            var cancelado = await Detalle(client, factura.Id);
            Assert.Equal(0m, cancelado.PagadoPorRep);
            Assert.Equal(116m, cancelado.TotalPorCobrar);
        }
        finally
        {
            db.RemoveRange(parcial, fallido, borrador);
            await db.SaveChangesAsync();
            db.Remove(factura);
            await db.SaveChangesAsync();
        }
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

    private static async Task<ComprobanteDetalleResponse> Detalle(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ComprobanteDetalleResponse>($"/api/v1/facturacion/facturas/{id}"))!;

    private static FacturaVenta Factura(string metodo)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, $"FIX-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, metodo, metodo == "PUE" ? "01" : "99", "MXN", null,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "FIX prueba ficticia FAC-05", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        return f;
    }

    private static ReciboPago Rep(FacturaVenta f, decimal importe, string estado)
    {
        var rep = ReciboPago.CrearBorrador(Empresa, $"FIX-REP-{Guid.NewGuid():N}", 2, f.SucursalId,
            null, null, Receptor, Emisor, 2026, 10, DateTimeOffset.UtcNow, "MXN", 1);
        rep.AgregarFacturaPagada(f.Id, f.Uuid!, 1, "MXN", importe, f.Total, "03", null, null,
            null, null, "FIX", "01", f.Total, []);
        rep.EstablecerImporteTotalPago(importe);
        if (estado != "Borrador") rep.MarcarTimbradoEnProceso();
        if (estado == "Timbrado") rep.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        if (estado == "TimbradoFallido") rep.MarcarTimbradoFallido("FIX", "Rechazo ficticio");
        return rep;
    }
}
