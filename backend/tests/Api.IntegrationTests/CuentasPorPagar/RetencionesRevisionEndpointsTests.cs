using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>86e3pdmee · Fiscal revisa la alerta de retenciones: permiso, motivo, bitácora y desbloqueo de «Autorizar».</summary>
public sealed class RetencionesRevisionEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Cxp = "/api/v1/cuentas-por-pagar/facturas";

    private static async Task<(Guid Id, int Version)> SembrarFacturaConAlertaAsync(AdjuntosProveedorAmbiente amb)
    {
        using var scope = amb.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var ahora = DateTimeOffset.UtcNow;
        var f = Factura.CapturarSinOc(AdjuntosProveedorAmbiente.EmpresaInicialId, null, Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(),
            "RET-" + Guid.NewGuid().ToString("N")[..6], null, ahora, ahora, DateOnly.FromDateTime(DateTime.Today.AddDays(30)), "MXN", null, 1000, 0, 0, 0, 1000, "RET", ahora);
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        db.Add(f);
        await db.SaveChangesAsync();
        // El DbContext recalcula la alerta al insertar; se asigna después para simular una factura con alerta.
        f.AsignarAlertaRetenciones("Las retenciones del XML difieren de las del proveedor");
        await db.SaveChangesAsync();
        return (f.Id, f.Version);
    }

    private static HttpRequestMessage Peticion(string ruta, Guid id, int version, object? body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Cxp}/{id}/{ruta}") { Content = JsonContent.Create(body ?? new { }) };
        req.Headers.Add("X-Expected-Version", version.ToString());
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return req;
    }

    [Fact]
    public async Task Revisar_retenciones_exige_permiso_y_motivo_deja_bitacora_y_desbloquea_autorizar()
    {
        var amb = new AdjuntosProveedorAmbiente(factory, s =>
        {
            s.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort>();
            s.AddSingleton<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        var (id, version) = await SembrarFacturaConAlertaAsync(amb);
        var admin = await amb.SuperAdminAsync();
        var (fiscal, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CuentasPorPagarRetencionesAdministrar, PermisosCanonicos.CuentasPorPagarFacturasGestionarTodasSucursales);
        var (sinPermiso, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CuentasPorPagarFacturasAutorizar, PermisosCanonicos.CuentasPorPagarFacturasGestionarTodasSucursales);

        var bloqueada = await admin.SendAsync(Peticion("autorizar", id, version, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bloqueada.StatusCode);
        Assert.Equal("CXP_RETENCIONES_SIN_REVISAR", await AdjuntosProveedorAmbiente.CodigoAsync(bloqueada));

        var prohibido = await sinPermiso.SendAsync(Peticion("retenciones/revisar", id, version, new { motivo = "Fiscal confirma ISR" }));
        Assert.Equal(HttpStatusCode.Forbidden, prohibido.StatusCode);

        var corto = await fiscal.SendAsync(Peticion("retenciones/revisar", id, version, new { motivo = "ok" }));
        Assert.True(corto.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest, $"HTTP {(int)corto.StatusCode}");
        Assert.NotEqual(HttpStatusCode.OK, corto.StatusCode);

        var revisada = await fiscal.SendAsync(Peticion("retenciones/revisar", id, version, new { motivo = "Fiscal confirma ISR" }));
        Assert.Equal(HttpStatusCode.OK, revisada.StatusCode);
        var nuevaVersion = (await AdjuntosProveedorAmbiente.LeerAsync(revisada)).GetProperty("version").GetInt32();

        var autorizada = await admin.SendAsync(Peticion("autorizar", id, nuevaVersion, null));
        Assert.Equal(HttpStatusCode.OK, autorizada.StatusCode);

        using var scope = amb.Factory.Services.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var bitacora = await core.AuditLog.Where(a => a.AggregateRootId == id && a.Operacion == "factura.retenciones-revisadas").ToListAsync();
        Assert.Single(bitacora);
        Assert.Contains("Fiscal confirma ISR", bitacora[0].Cambios);
    }

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
