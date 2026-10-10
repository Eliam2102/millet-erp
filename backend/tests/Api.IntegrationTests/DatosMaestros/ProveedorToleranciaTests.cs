using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>G1.13: HTTP, permisos, bitácora, adaptadores y fotos persistidas sobre PostgreSQL desechable.</summary>
public sealed class ProveedorToleranciaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task G113c_Compras403_CxpEdita_Audita_YApiNoAdmitePorcentaje()
    {
        var ambiente = new AdjuntosProveedorAmbiente(factory);
        var proveedorId = await ambiente.SeedProveedorAsync();
        try
        {
            var (compras, _) = await ambiente.ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosProveedoresGestionar);
            var (cxp, usuarioId) = await ambiente.ClienteConPermisosAsync(
                PermisosCanonicos.DatosMaestrosProveedoresGestionar, PermisosCanonicos.DatosMaestrosProveedoresToleranciaEditar);
            var url = $"{AdjuntosProveedorAmbiente.Base}/{proveedorId}/tolerancia";
            Assert.Equal(HttpStatusCode.Forbidden, (await compras.PutAsJsonAsync(url, new { montoMxn = 5 })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await cxp.PutAsJsonAsync(url, new { montoMxn = 5 })).StatusCode);
            // Margen amplio: el sello de la bitácora puede venir de otro reloj (app vs. prueba).
            var desde = DateTimeOffset.UtcNow.AddMinutes(-1);
            Assert.Equal(HttpStatusCode.NoContent, (await cxp.PutAsJsonAsync(url, new { montoMxn = 1 })).StatusCode);
            var detalle = await AdjuntosProveedorAmbiente.LeerAsync(await cxp.GetAsync($"{AdjuntosProveedorAmbiente.Base}/{proveedorId}"));
            Assert.Equal(1m, detalle.GetProperty("toleranciaFacturaContraOcMxn").GetDecimal());
            using (var scope = ambiente.Factory.Services.CreateScope())
            {
                var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
                var entradas = await core.AuditLog.Where(a => a.EntidadId == proveedorId && a.Operacion == "proveedor.tolerancia-cambiada")
                    .OrderBy(a => a.Timestamp).ToListAsync();
                Assert.Equal(2, entradas.Count);
                // Los dos cambios pueden quedar con el mismo sello de tiempo: se identifica el segundo por su valor, no por el orden.
                static decimal? Despues(string cambios)
                {
                    using var doc = JsonDocument.Parse(cambios);
                    var d = doc.RootElement.GetProperty("toleranciaFacturaContraOcMxn").GetProperty("despues");
                    return d.ValueKind == JsonValueKind.Number ? d.GetDecimal() : null;
                }
                var cambio = entradas.Single(e => Despues(e.Cambios) == 1m);
                Assert.Equal(usuarioId, cambio.UsuarioId);
                Assert.InRange(cambio.Timestamp, desde, DateTimeOffset.UtcNow.AddMinutes(1));
                using var json = JsonDocument.Parse(cambio.Cambios);
                var valores = json.RootElement.GetProperty("toleranciaFacturaContraOcMxn");
                Assert.Equal(5m, valores.GetProperty("antes").GetDecimal());
                Assert.Equal(1m, valores.GetProperty("despues").GetDecimal());
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await cxp.PutAsJsonAsync(url, new { montoMxn = 1, tipo = "Porcentaje" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cxp.PutAsJsonAsync(url, new { })).StatusCode);
            var negativo = await cxp.PutAsJsonAsync(url, new { montoMxn = -1 });
            Assert.Equal(HttpStatusCode.BadRequest, negativo.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await cxp.PutAsJsonAsync(url, new { montoMxn = (decimal?)null })).StatusCode);
        }
        finally { await LimpiarAsync(ambiente, proveedorId); }
    }

    [Fact]
    public async Task Captura_CubreCA23_CA86_G113a_G113d_YCambioGeneralEnSiguienteFactura()
    {
        var oc = new OcDemo();
        // La prueba mide la tolerancia, no el candado de periodo (P8): el periodo se da por abierto
        // para no depender de que otra prueba haya abierto el mes en la base compartida.
        var ambiente = new AdjuntosProveedorAmbiente(factory, services =>
        {
            services.AddSingleton<IComprasOcReadPort>(oc);
            services.AddScoped<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        var proveedorId = await ambiente.SeedProveedorAsync();
        oc.ProveedorId = proveedorId;
        var admin = await ambiente.SuperAdminAsync();
        var parametroUrl = $"/api/v1/admin/parametros/{ToleranciaFacturaContraOcParametro.Clave}";
        var listado = await AdjuntosProveedorAmbiente.LeerAsync(await admin.GetAsync("/api/v1/admin/parametros?modulo=cxp"));
        var parametro = listado.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == ToleranciaFacturaContraOcParametro.Clave);
        var original = parametro.GetProperty("valor").GetString();
        try
        {
            (await admin.PatchAsJsonAsync(parametroUrl, new { valor = "0.99" })).EnsureSuccessStatusCode();
            async Task Configurar(decimal? monto) => (await admin.PutAsJsonAsync(
                $"{AdjuntosProveedorAmbiente.Base}/{proveedorId}/tolerancia", new { montoMxn = monto })).EnsureSuccessStatusCode();
            async Task<Guid> Capturar(decimal diferencia, EstadoPasivo esperado, decimal foto)
            {
                var total = 10000m + diferencia;
                var ahora = DateTimeOffset.UtcNow;
                // P3 concilia por línea y no deja facturar dos veces la misma cantidad: cada captura usa su propia OC
                // (la línea toma el mismo Id que la OC) para medir solo la tolerancia.
                var ocId = Guid.NewGuid();
                var command = new CapturarFacturaConOcCommand(ocId, proveedorId, oc.SucursalId, null, null, "DEMO-G113", null,
                    ahora, ahora, DateOnly.FromDateTime(ahora.UtcDateTime).AddDays(30), "MXN", null,
                    total, 0, 0, 0, total, [new(oc.ArticuloId, null, "Material DEMO G1.13", 1, "H87", "Pieza", total, total, null, ocId, null)]);
                var response = await admin.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", command);
                response.EnsureSuccessStatusCode();
                var id = (await AdjuntosProveedorAmbiente.LeerAsync(response)).GetProperty("id").GetGuid();
                using var scope = ambiente.Factory.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                var factura = await db.FacturasProveedor.AsNoTracking().SingleAsync(f => f.Id == id);
                Assert.Equal(esperado, factura.Estado);
                Assert.Equal(foto, factura.ToleranciaValor);
                Assert.Equal(ToleranciaTipo.MontoAbsoluto, factura.ToleranciaTipo);
                var tipoEvento = esperado == EstadoPasivo.Cancelada
                    ? "cuentas_por_pagar.factura.rechazada-por-tolerancia.v1"
                    : "cuentas_por_pagar.factura.registrada.v1";
                var eventos = await db.OutboxEntries.AsNoTracking().Where(e => e.EventType == tipoEvento).ToListAsync();
                Assert.Contains(eventos, e => e.Payload.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));
                if (esperado == EstadoPasivo.Cancelada)
                {
                    Assert.Equal(MotivoCancelacion.RechazadaPorTolerancia, factura.MotivoDeCancelacion);
                    Assert.Throws<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => factura.Autorizar(null, ahora));
                }
                return id;
            }
            await Configurar(5);
            var primera = await Capturar(3, EstadoPasivo.Capturada, 5);
            await Capturar(6, EstadoPasivo.Cancelada, 5);
            await Capturar(8, EstadoPasivo.Cancelada, 5);
            await Configurar(1);
            await Capturar(3, EstadoPasivo.Cancelada, 1);
            using (var scope = ambiente.Factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                var anterior = await db.FacturasProveedor.AsNoTracking().SingleAsync(f => f.Id == primera);
                Assert.Equal(5m, anterior.ToleranciaValor);
                Assert.Equal(EstadoPasivo.Capturada, anterior.Estado);
            }
            await Configurar(null);
            await Capturar(0.50m, EstadoPasivo.Capturada, 0.99m);
            await Capturar(1.50m, EstadoPasivo.Cancelada, 0.99m);
            (await admin.PatchAsJsonAsync(parametroUrl, new { valor = "1.50" })).EnsureSuccessStatusCode();
            await Capturar(1.50m, EstadoPasivo.Capturada, 1.50m);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PatchAsJsonAsync(parametroUrl, new { valor = "-1" })).StatusCode);
        }
        finally
        {
            (await admin.PatchAsJsonAsync(parametroUrl, new { valor = original })).EnsureSuccessStatusCode();
            await LimpiarAsync(ambiente, proveedorId);
        }
    }

    private static async Task LimpiarAsync(AdjuntosProveedorAmbiente ambiente, Guid proveedorId)
    {
        using var scope = ambiente.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor
            .Where(f => f.ProveedorId == proveedorId).ExecuteDeleteAsync();
        await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Proveedores
            .Where(p => p.Id == proveedorId).ExecuteDeleteAsync();
    }

    private sealed class OcDemo : IComprasOcReadPort
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid SucursalId { get; } = Guid.NewGuid();
        public Guid ArticuloId { get; } = Guid.NewGuid();
        public Guid ProveedorId { get; set; }
        public Task<OrdenCompraDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OrdenCompraDto?>(
            new(id, "OC-DEMO-G113", ProveedorId, AdjuntosProveedorAmbiente.EmpresaInicialId, SucursalId, 10000, "Autorizada",
                [new(id, ArticuloId, 1, 10000, 0, 1)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(true && ids.Contains(Id)
                ? new Dictionary<Guid, string> { [Id] = "OC-DEMO-G113" } : new Dictionary<Guid, string>());
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrdenCompraDto>>([]);
    }

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
