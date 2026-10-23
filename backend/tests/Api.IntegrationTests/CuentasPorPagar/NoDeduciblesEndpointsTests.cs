using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>86e3kdwng · P8.6 no deducibles e IVA no acreditable.</summary>
public sealed class NoDeduciblesEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Cxp = "/api/v1/cuentas-por-pagar";
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Cliente, Guid TarjetaId, Guid TitularId)> AmbienteAsync()
    {
        var amb = new AdjuntosProveedorAmbiente(factory, s =>
        {
            s.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort>();
            s.AddSingleton<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        var (cliente, usuarioId) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento, PermisosCanonicos.CuentasPorPagarTcLeer,
            PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales, PermisosCanonicos.CuentasPorPagarDocumentosLeerTodasSucursales);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var titular = Guid.NewGuid();
        var t = Tarjeta.Crear(Empresa, "Amex", "AMEX_MX", NumeroTarjetaEnmascarado.FromUltimosCuatro("4321"), "P8.6 " + Guid.NewGuid().ToString("N")[..6],
            titular, Guid.NewGuid(), 100000m, "MXN", 15, 20, new DateOnly(2026, 1, 1));
        db.TarjetasCredito.Add(t);
        await db.SaveChangesAsync();
        return (cliente, t.Id, titular);
    }

    private async Task<Guid> SembrarCfdiAsync(string uuid, decimal subtotal, decimal iva)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var cfdi = CfdiRecibido.Ingresar(Empresa, UuidCfdi.Parse(uuid), RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"),
            TipoCfdi.Ingreso, "ND-1", "ND", ahora, subtotal + iva, subtotal, iva, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, ahora, "nd-blob", null, "nd-hash");
        db.CfdisRecibidos.Add(cfdi);
        await db.SaveChangesAsync();
        return cfdi.Id;
    }

    private static object SinCfdi(Guid tarjetaId, Guid titular, string? clasificacion) => new
    {
        tarjetaId, usuarioQueUsoId = titular, fechaMovimiento = new DateOnly(2026, 10, 20), montoOriginal = 350m, monedaOriginal = "MXN",
        merchantRaw = "Restaurante Demo", conceptoContable = "Comidas", clasificacionNoDeducible = clasificacion,
    };

    private static object ConCfdi(Guid tarjetaId, Guid titular, Guid cfdiId, string uuid, string? clasificacion) => new
    {
        tarjetaId, usuarioQueUsoId = titular, fechaMovimiento = new DateOnly(2026, 10, 20), montoOriginal = 1160m, monedaOriginal = "MXN",
        merchantRaw = "Papeleria Demo", conceptoContable = "Papeleria", cfdiRecibidoId = cfdiId, proveedorId = Guid.NewGuid(), uuidCfdi = uuid,
        fechaCfdi = DateTimeOffset.UtcNow, subtotal = 1000m, descuentos = 0m, impuestosTrasladados = 160m, retenciones = 0m, totalFactura = 1160m,
        fechaVencimiento = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), clasificacionNoDeducible = clasificacion,
    };

    [Fact]
    public async Task Catalogo_clasificaciones_lista_DEMO_con_permiso_de_captura_y_403_sin_el()
    {
        var amb = new AdjuntosProveedorAmbiente(factory, _ => { });
        var (conPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento);
        var (sinPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarFacturasLeer);

        var ok = await conPermiso.GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var filas = (await Json(ok)).EnumerateArray().ToList();
        Assert.Contains(filas, f => f.GetProperty("clave").GetString() == "COMIDAS" && f.GetProperty("descripcion").GetString() == "DEMO · Comidas");
        Assert.True(filas.Count >= 6);

        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles")).StatusCode);
    }

    [Fact]
    public async Task Tarjeta_sin_CFDI_exige_clasificacion_valida_y_la_guarda()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();

        foreach (var invalida in new string?[] { null, "", "NO_EXISTE" })
        {
            var r = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/sin-cfdi", SinCfdi(tarjeta, titular, invalida));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("TC_MOV_CLASIFICACION_INVALIDA", (await Json(r)).GetProperty("code").GetString());
        }

        var ok = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/sin-cfdi", SinCfdi(tarjeta, titular, "comidas"));
        Assert.True(ok.StatusCode == HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var m = await Json(ok);
        Assert.Equal("COMIDAS", m.GetProperty("clasificacionNoDeducible").GetString());
        Assert.Equal(0m, m.GetProperty("ivaNoAcreditable").GetDecimal());

        var lista = await Json(await c.GetAsync(Cxp + $"/movimientos-tc?tarjetaId={tarjeta}"));
        Assert.Contains(lista.GetProperty("items").EnumerateArray(), x => x.GetProperty("clasificacionNoDeducible").GetString() == "COMIDAS");
    }

    [Fact]
    public async Task Tarjeta_con_CFDI_no_deducible_manda_el_IVA_a_no_acreditable_y_sin_marca_es_deducible()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();

        var uuidND = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiND = await SembrarCfdiAsync(uuidND, 1000m, 160m);
        var nd = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiND, uuidND, "PROPINAS"));
        Assert.True(nd.StatusCode == HttpStatusCode.Created, await nd.Content.ReadAsStringAsync());
        var mND = await Json(nd);
        Assert.Equal("PROPINAS", mND.GetProperty("clasificacionNoDeducible").GetString());
        Assert.Equal(160m, mND.GetProperty("ivaNoAcreditable").GetDecimal());

        var uuidD = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiD = await SembrarCfdiAsync(uuidD, 1000m, 160m);
        var d = await Json(await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiD, uuidD, null)));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("clasificacionNoDeducible").ValueKind);
        Assert.Equal(0m, d.GetProperty("ivaNoAcreditable").GetDecimal());

        var lista = (await Json(await c.GetAsync(Cxp + $"/movimientos-tc?tarjetaId={tarjeta}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(160m, lista.Single(x => x.GetProperty("id").GetGuid() == mND.GetProperty("id").GetGuid()).GetProperty("ivaNoAcreditable").GetDecimal());

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var f = await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.AsNoTracking()
            .SingleAsync(x => x.UuidCfdi == uuidND);
        Assert.Equal("PROPINAS", f.ClasificacionNoDeducible);
        Assert.Equal(160m, f.IvaNoAcreditable);
    }

    [Fact]
    public async Task Tarjeta_con_CFDI_rechaza_clasificacion_inexistente_sin_consumir_el_CFDI()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdi = await SembrarCfdiAsync(uuid, 1000m, 160m);

        var r = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdi, uuid, "NO_EXISTE"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("TC_MOV_CLASIFICACION_INVALIDA", (await Json(r)).GetProperty("code").GetString());

        var ok = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdi, uuid, "COMIDAS"));
        Assert.True(ok.StatusCode == HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
    }

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
