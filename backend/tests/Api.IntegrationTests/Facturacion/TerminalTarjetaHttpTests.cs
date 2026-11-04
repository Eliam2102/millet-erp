using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Cajas.Terminal;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// 86e3vr1tf / 86e3xt2q8: carga real del archivo de la terminal contra PostgreSQL. La hora del archivo es local
/// (Mérida, UTC−6) y se guarda en UTC; una venta de las 23:30 se empareja con la caja de ese mismo día. Cubre el
/// archivo OneGMR TRST y el «Transacciones por día» del portal. Datos FICTICIOS.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class TerminalTarjetaHttpTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DateOnly Dia = new(2026, 10, 1);

    [Fact]
    public async Task Carga_trst_y_portal_guarda_la_hora_en_utc_y_empareja_la_venta_de_la_noche_con_la_caja_del_dia()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var cajero = await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().Usuarios
            .AsNoTracking().FirstAsync(u => u.EntraOid == "dev-superadmin");

        var sufijo = Guid.NewGuid().ToString("N")[..10];
        var voucher = "0" + Random.Shared.Next(10_000, 99_999);  // el GMR lo manda sin el cero
        var opGmr = $"qa-gmr-{sufijo}";
        var opPortal = $"9{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        var folio = $"PRUEBA-LIQ-{sufijo}";
        var factura = Factura(sufijo);
        var caja = Caja.Crear(Empresa, $"TERMINAL-{sufijo}", null);
        var ahora = DateTimeOffset.UtcNow;
        var sesion = CajaSesion.Abrir(Empresa, caja.Id, TestComprasFixtures.SucursalMid, cajero.Id, 300m,
            DateOnly.FromDateTime(ahora.UtcDateTime), ahora, null);
        var cargas = new List<Guid>();
        try
        {
            db.AddRange(factura, caja, sesion);
            await db.SaveChangesAsync();
            var cobro = await client.PostAsJsonAsync("/api/v1/facturacion/cobros",
                new RegistrarCobroMostradorCommand(factura.Id, [new CobroFormaPagoInput("04", 116m, voucher, TarjetaUltimos4: "0987")]));
            Assert.Equal(HttpStatusCode.Created, cobro.StatusCode);
            // La caja se abre hoy (el cobro exige la sesión del día) y se pasa al día del archivo, como en QA.
            await db.CajaSesiones.Where(x => x.Id == sesion.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.DiaOperacion, Dia));

            // OneGMR TRST: venta de las 23:30 del 1-oct (hora local) = 05:30 UTC del 2-oct.
            var trst = "ID_TRANSACTION,COD_AUTHORIZATION,NUM_CARD,DES_FINANCIAL_MOVEMENTS_TYPE,DES_TRANSACTION_STATUS,DAT_TRANSACTION," +
                "TIM_TRANSACTION,AMT_GROSS,COD_CURRENCY,AMT_TOTAL_FEE,AMT_TOTAL_VAT_TAX,AMT_NET,ID_SETTLEMENT,DAT_SETTLED\n" +
                $"{opGmr},{voucher.TrimStart('0')},4321-56XX-XXXX-0987,Transaction,Captured,2026-10-01,23:30:00,116,484,2.90,0.46,112.64,{folio},2026-10-02\n" +
                "ENDOFFILE_000000001_000000001\n";
            var rGmr = await Cargar(client, $"GMR_MX_GETNET_9999999_20261002_R_TRST_{sufijo}.CSV", Encoding.UTF8.GetBytes(trst));
            cargas.Add(rGmr.Id);
            Assert.Equal((1, 1, 1), (rGmr.Nuevos, rGmr.Emparejados, rGmr.Liquidaciones));

            // Portal «Transacciones por día»: tabla HTML .xls, fecha día/mes y hora local.
            var portal = "<html><body><table><tr><th>No. Operación</th><th>Fecha</th><th>Importe</th><th>Moneda</th><th>TDC</th>" +
                "<th>Estatus</th><th># de Auth.</th></tr>" +
                $"<tr><td>{opPortal}</td><td>01/10 12:30</td><td>$ 50.00</td><td>MXN</td><td>1111</td><td>Aprobada</td><td>654321</td></tr>" +
                "</table></body></html>";
            var rPortal = await Cargar(client, $"terminal-portal-{sufijo}.xls", Encoding.Latin1.GetBytes(portal));
            cargas.Add(rPortal.Id);
            Assert.Equal((1, 0, 1), (rPortal.Nuevos, rPortal.Emparejados, rPortal.SinEmparejar));

            db.ChangeTracker.Clear();
            var gmr = await db.TransaccionesTerminal.AsNoTracking().SingleAsync(t => t.NoOperacion == opGmr);
            Assert.Equal(EstadoTransaccionTerminal.Emparejada, gmr.Estado);
            Assert.Equal(new DateTime(2026, 10, 2, 5, 30, 0, DateTimeKind.Utc), gmr.FechaHora.ToUniversalTime());
            var enPortal = await db.TransaccionesTerminal.AsNoTracking().SingleAsync(t => t.NoOperacion == opPortal);
            Assert.Equal(new DateTime(2026, 10, 1, 18, 30, 0, DateTimeKind.Utc), enPortal.FechaHora.ToUniversalTime());
            var liquidacion = await db.LiquidacionesTerminal.AsNoTracking().SingleAsync(l => l.Folio == folio);
            Assert.Equal((new DateOnly(2026, 10, 2), 2.90m, 0.46m), (liquidacion.Fecha, liquidacion.Comision, liquidacion.IvaComision));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.LiquidacionesTerminal.Where(l => l.Folio == folio).ExecuteDeleteAsync();
            await db.TransaccionesTerminal.Where(t => t.NoOperacion == opGmr || t.NoOperacion == opPortal).ExecuteDeleteAsync();
            await db.CargasTerminal.Where(c => cargas.Contains(c.Id)).ExecuteDeleteAsync();
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

    private static async Task<CargaTerminalResponse> Cargar(HttpClient client, string nombre, byte[] contenido)
    {
        using var form = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(archivo, "archivo", nombre);
        form.Add(new StringContent(Dia.ToString("yyyy-MM-dd")), "fechaArchivo");
        var r = await client.PostAsync("/api/v1/facturacion/terminal-tarjetas/cargas", form);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<CargaTerminalResponse>())!;
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

    private static FacturaVenta Factura(string sufijo)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, $"TERM-{sufijo}", 1, TestComprasFixtures.SucursalMid, null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio terminal", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio terminal", "601", "97000"), "PUE", "04", "MXN", null,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Prueba ficticia de la terminal", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        return f;
    }
}
