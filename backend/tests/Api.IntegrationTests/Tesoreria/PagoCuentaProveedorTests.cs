using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.Tesoreria;

/// <summary>
/// 86e3pdmee · pago a la cuenta bancaria elegida del proveedor: moneda, congelado del destino al pagar,
/// permisos de lectura (enmascarado/completo) y acciones de administración de cuentas.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class PagoCuentaProveedorTests(WebApplicationFactory<Program> factory)
{
    private const string ClabeMxn = "012180000000001234";
    private const string ClabeUsd = "072180000000004569";
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;

    private sealed record Escenario(
        AdjuntosProveedorAmbiente Amb, Guid ProveedorId, Guid CuentaMxnId, Guid CuentaUsdId, Guid EgresoMxnId, Guid EgresoUsdId, HttpClient Admin) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Amb.DisposeAsync();
    }

    private static string Cuentas(Guid proveedorId) => $"{AdjuntosProveedorAmbiente.Base}/{proveedorId}/cuentas-bancarias";

    private async Task<Escenario> SembrarAsync()
    {
        var amb = new AdjuntosProveedorAmbiente(factory, s =>
        {
            s.RemoveAll<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort>();
            s.AddSingleton<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort, PeriodoAbierto>();
            s.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort>();
            s.AddSingleton<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        try
        {
            var provId = await amb.SeedProveedorAsync(TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
            Guid mxn, usd, egresoMxn, egresoUsd;
            using (var scope = amb.Factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var ahora = DateTimeOffset.UtcNow;
                var cm = CuentaBancariaProveedor.Heredada(provId, "BBVA", ClabeMxn, "Proveedor SA", ahora);
                var cu = CuentaBancariaProveedor.Capturar(provId, "Banorte", ClabeUsd, null, null, "USD", "Proveedor SA", Guid.NewGuid(), "Capturista", ahora);
                cu.Validar(Guid.NewGuid(), "Validador", Guid.NewGuid(), true, null, ahora);
                var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                compartido.CuentasBancariasProveedor.AddRange(cm, cu);
                await compartido.SaveChangesAsync();
                mxn = cm.Id; usd = cu.Id;
                var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
                var n = Guid.NewGuid().ToString("N");
                var em = new CuentaBancaria(Empresa, "Egreso MXN", n[..12], null, "MXN");
                var eu = new CuentaBancaria(Empresa, "Egreso USD", n[12..24], null, "USD");
                tes.AddRange(em, eu);
                await tes.SaveChangesAsync();
                egresoMxn = em.Id; egresoUsd = eu.Id;
            }
            return new(amb, provId, mxn, usd, egresoMxn, egresoUsd, await amb.SuperAdminAsync());
        }
        catch
        {
            await amb.DisposeAsync();
            throw;
        }
    }

    private static async Task<Guid> SembrarPasivoAsync(Escenario e, string moneda)
    {
        using var scope = e.Amb.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var ahora = DateTimeOffset.UtcNow;
        var uuid = Guid.NewGuid();
        var f = Factura.CapturarSinOc(Empresa, null, uuid.ToString(), e.ProveedorId, Guid.NewGuid(), "PCP-" + uuid.ToString("N")[..6], null,
            ahora, ahora, DateOnly.FromDateTime(DateTime.Today.AddDays(30)), moneda, moneda == "USD" ? 18m : null, 1000, 0, 0, 0, 1000, "PCP", ahora);
        f.Autorizar(null, ahora);
        scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().Add(f);
        await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().SaveChangesAsync();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        tes.Add(new PasivoPendientePago(Empresa, f.Id, e.ProveedorId, null, 1000, 1000, moneda, null,
            DateOnly.FromDateTime(DateTime.Today.AddDays(30)), uuid, "PCP", ahora, "PUE"));
        await tes.SaveChangesAsync();
        return f.Id;
    }

    private static Task<HttpResponseMessage> PagarAsync(HttpClient c, Guid egreso, Guid facturaId, Guid cuentaProveedorId) =>
        c.PostAsJsonAsync("/api/v1/tesoreria/pagos/", new
        {
            cuentaBancariaId = egreso, fechaValor = DateOnly.FromDateTime(DateTime.Today),
            cuentaProveedorId, aplicaciones = new[] { new { facturaProveedorId = facturaId, importe = 100m } },
        });

    private static async Task<Guid> MovimientoIdAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await AdjuntosProveedorAmbiente.LeerAsync(r)).GetProperty("movimientoId").GetGuid();
    }

    private static async Task<(string? Banco, string? Numero)> DestinoAsync(HttpClient c, Guid movimientoId)
    {
        var d = (await AdjuntosProveedorAmbiente.LeerAsync(await c.GetAsync($"/api/v1/tesoreria/movimientos/{movimientoId}"))).GetProperty("movimiento");
        return (d.GetProperty("destinoBanco").GetString(), d.GetProperty("destinoNumeroCuenta").GetString());
    }

    private static HttpRequestMessage Accion(Guid proveedorId, Guid cuentaId, string ruta, int version, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Cuentas(proveedorId)}/{cuentaId}/{ruta}") { Content = JsonContent.Create(body ?? new { }) };
        req.Headers.Add("X-Expected-Version", version.ToString());
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return req;
    }

    private static async Task<(JsonElement Cuenta, int Version)> CuentaAsync(HttpClient c, Guid proveedorId, Guid cuentaId)
    {
        var lista = await AdjuntosProveedorAmbiente.LeerAsync(await c.GetAsync(Cuentas(proveedorId)));
        var cuenta = lista.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == cuentaId);
        return (cuenta, cuenta.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Pago_desde_egreso_USD_exige_cuenta_USD_del_proveedor_y_guarda_el_destino()
    {
        await using var e = await SembrarAsync();
        var facturaUsd = await SembrarPasivoAsync(e, "USD");

        var otraMoneda = await PagarAsync(e.Admin, e.EgresoUsdId, facturaUsd, e.CuentaMxnId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otraMoneda.StatusCode);
        Assert.Equal("PROVEEDOR_CUENTA_OTRA_MONEDA", await AdjuntosProveedorAmbiente.CodigoAsync(otraMoneda));

        var ok = await PagarAsync(e.Admin, e.EgresoUsdId, facturaUsd, e.CuentaUsdId);
        var movimientoId = await MovimientoIdAsync(ok);
        using var scope = e.Amb.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var mov = await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().MovimientosBancarios.SingleAsync(m => m.Id == movimientoId);
        Assert.Equal("Banorte", mov.DestinoBanco);
        Assert.Equal(ClabeUsd, mov.DestinoNumeroCuenta);
    }

    [Fact]
    public async Task Inhabilitar_la_cuenta_no_cambia_pagos_hechos_y_bloquea_pagos_nuevos()
    {
        await using var e = await SembrarAsync();
        var f1 = await SembrarPasivoAsync(e, "MXN");
        var f2 = await SembrarPasivoAsync(e, "MXN");

        var movimientoId = await MovimientoIdAsync(await PagarAsync(e.Admin, e.EgresoMxnId, f1, e.CuentaMxnId));
        var antes = await DestinoAsync(e.Admin, movimientoId);
        Assert.Equal("BBVA", antes.Banco);

        var (_, version) = await CuentaAsync(e.Admin, e.ProveedorId, e.CuentaMxnId);
        var inhabilitar = await e.Admin.SendAsync(Accion(e.ProveedorId, e.CuentaMxnId, "inhabilitar", version, new { motivo = "Cuenta cerrada por el banco" }));
        Assert.Equal(HttpStatusCode.OK, inhabilitar.StatusCode);

        Assert.Equal(antes, await DestinoAsync(e.Admin, movimientoId));
        var segundo = await PagarAsync(e.Admin, e.EgresoMxnId, f2, e.CuentaMxnId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segundo.StatusCode);
        Assert.Equal("PROVEEDOR_CUENTA_NO_ACTIVA", await AdjuntosProveedorAmbiente.CodigoAsync(segundo));
    }

    [Fact]
    public async Task Cuentas_para_pago_enmascaradas_completas_con_permiso_y_403_sin_pagos_aplicar()
    {
        await using var e = await SembrarAsync();
        var url = $"/api/v1/tesoreria/proveedores/{e.ProveedorId}/cuentas";
        var (enmascarado, _) = await e.Amb.ClienteConPermisosAsync(PermisosCanonicos.TesoreriaPagosAplicar);
        var (completo, _) = await e.Amb.ClienteConPermisosAsync(PermisosCanonicos.TesoreriaPagosAplicar, PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta);
        var (sin, _) = await e.Amb.ClienteConPermisosAsync(PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta);

        var masked = await AdjuntosProveedorAmbiente.LeerAsync(await enmascarado.GetAsync(url));
        Assert.All(masked.EnumerateArray(), c => Assert.DoesNotContain("0121800000", c.GetProperty("numero").GetString()));
        Assert.Matches(@"^\*+1234$", masked.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == e.CuentaMxnId).GetProperty("numero").GetString()!);

        var full = await AdjuntosProveedorAmbiente.LeerAsync(await completo.GetAsync(url));
        Assert.Equal(ClabeMxn, full.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == e.CuentaMxnId).GetProperty("numero").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await sin.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Listado_de_datos_maestros_con_cuentas_administrar_devuelve_el_numero_completo()
    {
        await using var e = await SembrarAsync();
        var (admin, _) = await e.Amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer, PermisosCanonicos.DatosMaestrosProveedoresCuentasAdministrar);
        var (ver, _) = await e.Amb.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresBancariosVer);

        var completo = await AdjuntosProveedorAmbiente.LeerAsync(await admin.GetAsync(Cuentas(e.ProveedorId)));
        Assert.Contains(completo.EnumerateArray(), c => c.GetProperty("numero").GetString() == ClabeMxn);
        var mascara = await AdjuntosProveedorAmbiente.LeerAsync(await ver.GetAsync(Cuentas(e.ProveedorId)));
        Assert.DoesNotContain(mascara.EnumerateArray(), c => c.GetProperty("numero").GetString() == ClabeMxn);
    }

    [Fact]
    public async Task Rechazar_inhabilitar_y_reactivar_por_API_con_su_estado_y_403_sin_cuentas_administrar()
    {
        await using var e = await SembrarAsync();
        var (sinPermiso, _) = await e.Amb.ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer, PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar);
        var capturada = await AdjuntosProveedorAmbiente.LeerAsync(await e.Admin.PostAsJsonAsync(Cuentas(e.ProveedorId),
            new { banco = "HSBC", clabe = "021180000000005674", moneda = "MXN", titular = "Proveedor SA" }));
        var porValidarId = capturada.GetProperty("id").GetGuid();

        foreach (var ruta in new[] { "rechazar", "inhabilitar", "reactivar" })
        {
            var r = await sinPermiso.SendAsync(Accion(e.ProveedorId, porValidarId, ruta, 0, new { motivo = "Sin permiso de administración" }));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        async Task<JsonElement> Ejecutar(Guid cuentaId, string ruta)
        {
            var (_, v) = await CuentaAsync(e.Admin, e.ProveedorId, cuentaId);
            var r = await e.Admin.SendAsync(Accion(e.ProveedorId, cuentaId, ruta, v, new { motivo = "Motivo de prueba válido" }));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return await AdjuntosProveedorAmbiente.LeerAsync(r);
        }

        var rechazada = await Ejecutar(porValidarId, "rechazar");
        Assert.Equal((int)EstadoCuentaBancariaProveedor.Rechazada, rechazada.GetProperty("estado").GetInt32());

        Assert.Equal((int)EstadoCuentaBancariaProveedor.Inhabilitada, (await Ejecutar(e.CuentaMxnId, "inhabilitar")).GetProperty("estado").GetInt32());
        Assert.Equal((int)EstadoCuentaBancariaProveedor.Activa, (await Ejecutar(e.CuentaMxnId, "reactivar")).GetProperty("estado").GetInt32());
    }

    private sealed class PeriodoAbierto : Millet.Tesoreria.Domain.Ports.IPeriodoContablePort, Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }
}
