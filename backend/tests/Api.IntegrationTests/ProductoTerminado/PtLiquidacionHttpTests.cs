using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>Liquidación del chofer en caja y aviso de la contra entrega no liquidada (PT5.7), contra el host real.</summary>
[Collection(ApiCompartida.Nombre)]
public class PtLiquidacionHttpTests
{
    private const string Base = "/api/v1/producto-terminado";
    private static readonly string[] Operativo = [PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoExcepcionesGestionar];

    private readonly WebApplicationFactory<Program> _factory;
    private readonly PtDatos _d;

    public PtLiquidacionHttpTests(WebApplicationFactory<Program> factory) => (_factory, _d) = (factory, new PtDatos(factory));

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    /// <summary>Hoja emitida de un pedido «Crédito 24 horas» de $8,500 (85 piezas de $100 en Facturación; 2 salen en la ruta).</summary>
    private async Task<(Guid Sucursal, Guid SalidaId, Guid AlbaranId, HttpClient Cliente)> HojaEmitidaAsync()
    {
        var (a, _) = await _d.SucursalesAsync();
        var numero = PtDatos.Nuevo();
        await _d.FacturacionAsync(numero, a, 85m, "Crédito 24 horas");
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var facturable = await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().PedidosFacturables.AsNoTracking()
                .SingleAsync(x => x.EmpresaId == PtDatos.Empresa && x.NumeroPedido == numero);
            var db = scope.ServiceProvider.GetRequiredService<ProductoTerminadoDbContext>();
            var p = PedidoPt.Crear(PtDatos.Empresa, facturable.Id, numero, a, "Crédito 24 horas", 1);
            p.ActualizarPosiciones(1, [(1, null, "PT-INT vidrio", 2, "H87")]);
            p.Liberar(MotivosPt.SaldoCubierto, DateTimeOffset.UtcNow);
            db.PedidosPt.Add(p);
            await db.SaveChangesAsync();
        }
        var etiquetas = new[] { await _d.PiezaAsync(numero, a, 1), await _d.PiezaAsync(numero, a, 2) };
        var cliente = await _d.ClienteAsync(Operativo, a);
        var hoja = await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida",
            new { sucursalId = a, destino = "Obra Mérida norte", foranea = false, pedidos = new[] { new { numeroPedido = numero, etiquetas } } }));
        var id = hoja.GetProperty("id").GetGuid();
        var emitida = await Json(await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null));
        return (a, id, emitida.GetProperty("albaranes")[0].GetProperty("id").GetGuid(), cliente);
    }

    private static object Liquidar(Guid albaran, bool firmado, decimal efectivo, decimal voucher, string? referencia) =>
        new
        {
            albaranes = new[] { new { albaranId = albaran, firmado, efectivo, voucher, voucherReferencia = referencia,
                tipoTarjeta = voucher > 0 ? "Debito" : null, tarjetaUltimos4 = voucher > 0 ? "4821" : null } },
            imprevistos = new[] { new { descripcion = "Maniobra extra de descarga", importe = 350m } },
        };

    [Fact]
    public async Task CA5_6_y_PT5_7_a_el_voucher_que_no_coincide_deja_la_diferencia_y_no_cierra_hasta_corregirlo()
    {
        var (_, salida, albaran, cliente) = await HojaEmitidaAsync();
        using var _ = cliente;

        var pendiente = await Json(await cliente.GetAsync($"{Base}/liquidaciones/{salida}"));
        Assert.Equal("Pendiente", pendiente.GetProperty("estado").GetString());
        Assert.Equal(8500m, pendiente.GetProperty("totalEsperado").GetDecimal());

        var conDiferencia = await Json(await cliente.PostAsJsonAsync($"{Base}/liquidaciones/{salida}", Liquidar(albaran, true, 0m, 8000m, "TPV-004821")));
        Assert.Equal("ConDiferencia", conDiferencia.GetProperty("estado").GetString());
        Assert.Equal(500m, conDiferencia.GetProperty("diferencia").GetDecimal());

        // M-177: al cuadrar, el cobro entra a la caja sobre la factura del albarán. Sin factura timbrada la caja lo rechaza con el folio y la
        // liquidación no se cierra (el cierre con factura y sesión de caja está en las unitarias de PT y Facturación).
        var sinFactura = await cliente.PostAsJsonAsync($"{Base}/liquidaciones/{salida}", Liquidar(albaran, true, 500m, 8000m, "TPV-004821"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinFactura.StatusCode);
        Assert.Contains("PT_ALBARAN_SIN_FACTURA", await sinFactura.Content.ReadAsStringAsync());
        var sigue = await Json(await cliente.GetAsync($"{Base}/liquidaciones/{salida}"));
        Assert.Equal("ConDiferencia", sigue.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task CA5_7_la_contra_entrega_sin_liquidar_se_avisa_una_sola_vez_al_cierre_del_dia()
    {
        var (_, salida, _, cliente) = await HojaEmitidaAsync();
        using var _ = cliente;

        var cierre = await Json(await cliente.PostAsync($"{Base}/liquidaciones/cierre-dia", null));
        Assert.True(cierre.GetProperty("avisos").GetInt32() >= 1);
        var detalle = await Json(await cliente.GetAsync($"{Base}/liquidaciones/{salida}"));
        Assert.True(detalle.GetProperty("albaranes")[0].GetProperty("avisado").GetBoolean());

        var segundo = await Json(await cliente.PostAsync($"{Base}/liquidaciones/cierre-dia", null));
        Assert.Equal(0, segundo.GetProperty("avisos").GetInt32());
    }

    [Fact]
    public async Task Sin_permiso_de_operar_no_se_liquida_ni_se_cierra_el_dia()
    {
        var (a, salida, albaran, _) = await HojaEmitidaAsync();
        using var lector = await _d.ClienteAsync([PermisosCanonicos.ProductoTerminadoPiezasVer], a);
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"{Base}/liquidaciones/{salida}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.PostAsJsonAsync($"{Base}/liquidaciones/{salida}", Liquidar(albaran, true, 8500m, 0m, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.PostAsync($"{Base}/liquidaciones/cierre-dia", null)).StatusCode);
    }
}
