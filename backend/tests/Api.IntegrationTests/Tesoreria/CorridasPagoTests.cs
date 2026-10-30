using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Tesoreria;

/// <summary>
/// T9.1-A (86e3ke5bt) por HTTP contra la BD real: armar la corrida con bruto/retenciones/neto, «un pasivo en una sola
/// corrida viva» (índice único parcial), «quien arma no autoriza» y el parámetro de firmas. Las reglas finas (umbral T-1/T/T+1,
/// segunda firma del rol) están en Tesoreria.UnitTests; aquí se prueba el cableado.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CorridasPagoTests(WebApplicationFactory<Program> factory)
{
    private const string Clabe = "012180000000001234";
    private const string Base = "/api/v1/tesoreria/corridas";
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;

    private sealed record Escenario(AdjuntosProveedorAmbiente Amb, Guid ProveedorId, Guid CuentaId, Guid Pasivo1, Guid Pasivo2, HttpClient Admin) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Amb.DisposeAsync();
    }

    private async Task<Escenario> SembrarAsync()
    {
        var amb = new AdjuntosProveedorAmbiente(factory);
        try
        {
            var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
            Guid cuenta, p1, p2;
            using (var scope = amb.Factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var ahora = DateTimeOffset.UtcNow;
                var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                compartido.CuentasBancariasProveedor.Add(CuentaBancariaProveedor.Heredada(provId, "BBVA", Clabe, "Proveedor SA", ahora));
                await compartido.SaveChangesAsync();
                var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
                var c = new CuentaBancaria(Empresa, "Egreso MXN", Guid.NewGuid().ToString("N")[..12], null, "MXN");
                // 1,000 + 160 IVA - 106.67 retenciones = 1,053.33 neto.
                var a = new PasivoPendientePago(Empresa, Guid.NewGuid(), provId, null, 1_053.33m, 1_053.33m, "MXN", null,
                    DateOnly.FromDateTime(DateTime.Today.AddDays(10)), null, "CORR-1", ahora, "PUE", 1_000m, 160m, 106.67m);
                var b = new PasivoPendientePago(Empresa, Guid.NewGuid(), provId, null, 500m, 500m, "MXN", null,
                    DateOnly.FromDateTime(DateTime.Today.AddDays(12)), null, "CORR-2", ahora, "PUE");
                tes.AddRange(c, a, b);
                await tes.SaveChangesAsync();
                cuenta = c.Id; p1 = a.FacturaProveedorId; p2 = b.FacturaProveedorId;
            }
            return new(amb, provId, cuenta, p1, p2, await amb.SuperAdminAsync());
        }
        catch
        {
            await amb.DisposeAsync();
            throw;
        }
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, string url, object? body = null, int? version = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        if (version is int v) req.Headers.Add("X-Expected-Version", v.ToString());
        return c.SendAsync(req);
    }

    private static Task<HttpResponseMessage> Crear(Escenario e, params Guid[] facturas) =>
        Post(e.Admin, Base, new { cuentaBancariaId = e.CuentaId, facturas });

    [Fact]
    public async Task Arma_la_corrida_con_bruto_retenciones_y_neto_por_proveedor()
    {
        await using var e = await SembrarAsync();

        var r = await Crear(e, e.Pasivo1, e.Pasivo2);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var c = await AdjuntosProveedorAmbiente.LeerAsync(r);
        Assert.Equal("Borrador", c.GetProperty("estado").GetString());
        Assert.Equal(1_553.33m, c.GetProperty("total").GetDecimal());
        Assert.Equal(106.67m, c.GetProperty("retenciones").GetDecimal());
        var proveedor = Assert.Single(c.GetProperty("proveedores").EnumerateArray());
        Assert.Equal(1_660m, proveedor.GetProperty("bruto").GetDecimal());
        Assert.Equal(1_553.33m, proveedor.GetProperty("neto").GetDecimal());
    }

    [Fact]
    public async Task Un_pasivo_en_corrida_viva_no_se_agrega_a_otra_y_vuelve_a_estar_libre_al_cancelar()
    {
        await using var e = await SembrarAsync();
        var primera = await AdjuntosProveedorAmbiente.LeerAsync(await Post(e.Admin, Base, new { cuentaBancariaId = e.CuentaId, facturas = new[] { e.Pasivo1 }, nombre = "Corrida semana 44" }));

        var duplicada = await Crear(e, e.Pasivo1, e.Pasivo2);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicada.StatusCode);
        Assert.Contains("Corrida semana 44", await duplicada.Content.ReadAsStringAsync());

        var cancelada = await Post(e.Admin, $"{Base}/{primera.GetProperty("id").GetGuid()}/cancelar", new { motivo = "error de captura" }, primera.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.OK, cancelada.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Crear(e, e.Pasivo1, e.Pasivo2)).StatusCode);
    }

    [Fact]
    public async Task Quien_arma_la_corrida_no_puede_autorizarla()
    {
        await using var e = await SembrarAsync();
        var c = await AdjuntosProveedorAmbiente.LeerAsync(await Crear(e, e.Pasivo1));
        var id = c.GetProperty("id").GetGuid();
        var enviada = await AdjuntosProveedorAmbiente.LeerAsync(await Post(e.Admin, $"{Base}/{id}/enviar-autorizacion", null, c.GetProperty("version").GetInt32()));
        Assert.Equal("EnAutorizacion", enviada.GetProperty("estado").GetString());

        var r = await Post(e.Admin, $"{Base}/{id}/autorizar", null, enviada.GetProperty("version").GetInt32());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("CORRIDA_MISMO_USUARIO", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task T91B_el_oficio_y_la_ejecucion_exigen_corrida_autorizada()
    {
        await using var e = await SembrarAsync();
        var c = await AdjuntosProveedorAmbiente.LeerAsync(await Crear(e, e.Pasivo1));
        var id = c.GetProperty("id").GetGuid();
        var version = c.GetProperty("version").GetInt32();

        var oficio = await Post(e.Admin, $"{Base}/{id}/oficio");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oficio.StatusCode);
        Assert.Contains("CORRIDA_OFICIO_NO_AUTORIZADA", await oficio.Content.ReadAsStringAsync());

        var ejecucion = await Post(e.Admin, $"{Base}/{id}/ejecucion",
            new { pagos = new[] { new { facturas = new[] { e.Pasivo1 }, referenciaBancaria = "REF-1", fechaValor = DateOnly.FromDateTime(DateTime.Today) } } }, version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ejecucion.StatusCode);
        Assert.Contains("CORRIDA_ESTADO_INVALIDO", await ejecucion.Content.ReadAsStringAsync());

        // Autorizada (estado forzado: la segunda firma se prueba en las pruebas unitarias) → el oficio sale con el desglose.
        using (var scope = e.Amb.Factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().CorridasPago.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, Millet.Tesoreria.Domain.Corridas.EstadoCorridaPago.Autorizada));
        }
        var ok = await Post(e.Admin, $"{Base}/{id}/oficio");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var reporte = await AdjuntosProveedorAmbiente.LeerAsync(ok);
        Assert.Equal(1_053.33m, reporte.GetProperty("totales").GetProperty("neto").GetDecimal());
        Assert.Equal(106.67m, reporte.GetProperty("totales").GetProperty("retenciones").GetDecimal());
    }

    [Fact]
    public async Task Parametro_de_firmas_demo_y_pasivos_candidatos_libres()
    {
        await using var e = await SembrarAsync();

        var politica = await AdjuntosProveedorAmbiente.LeerAsync(await e.Admin.GetAsync($"{Base}/politica-firmas"));
        var escalon = Assert.Single(politica.EnumerateArray());
        Assert.Equal((500_000m, 2, "gerente"), (escalon.GetProperty("montoDesde").GetDecimal(), escalon.GetProperty("firmasRequeridas").GetInt32(), escalon.GetProperty("rolRequerido").GetString()));

        await Crear(e, e.Pasivo1);
        var candidatos = await AdjuntosProveedorAmbiente.LeerAsync(await e.Admin.GetAsync($"{Base}/candidatos?moneda=MXN"));
        var items = candidatos.GetProperty("items").EnumerateArray().ToList();   // BD compartida: no se asume que sean los únicos
        Assert.DoesNotContain(items, i => i.GetProperty("facturaProveedorId").GetGuid() == e.Pasivo1);
        var libre = Assert.Single(items, i => i.GetProperty("facturaProveedorId").GetGuid() == e.Pasivo2);
        Assert.True(libre.GetProperty("cuentaValidada").GetBoolean());
    }
}
