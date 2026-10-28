using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
using Cargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>UX-F2-CXP-03: <c>q</c> server-side (con comodines escapados) y RFC del proveedor en las bandejas. Datos FIX, se limpian al final.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class BandejasBusquedaTests(WebApplicationFactory<Program> factory)
{
    private const string Cxp = "/api/v1/cuentas-por-pagar";

    [Fact]
    public async Task Q_filtra_facturas_notas_de_credito_y_notas_de_cargo_y_devuelve_el_rfc()
    {
        var proveedorId = Guid.NewGuid();
        var sucursalId = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
        await using var app = enEmpresa.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IProveedorReadPort>();
            s.AddSingleton<IProveedorReadPort>(new Proveedor(proveedorId));
            // Siembra directa en BD: el período contable real no es el objeto de esta prueba.
            s.RemoveAll<IPeriodoContablePort>();
            s.AddSingleton<IPeriodoContablePort>(new PeriodoAbierto());
        }));
        using var admin = await LoginAsync(app);
        var fecha = DateTimeOffset.UtcNow;
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                Factura F(string folio) => Factura.CapturarSinOc(EmpresaBootstrapId, null, null, proveedorId, sucursalId, folio, null,
                    fecha, fecha, DateOnly.FromDateTime(DateTime.UtcNow), "MXN", null, 100, 0, 0, 0, 100, "FIX bandejas", fecha);
                var f = F($"FIXQ-{token}_1");
                var nc = Nc.Capturar(EmpresaBootstrapId, null, Guid.NewGuid().ToString(), proveedorId, $"NCQ-{token}", null, fecha, "MXN", null, 100, 0, 0, 100,
                    TipoNotaCredito.Descuento, TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), f.Id, null, fecha);
                var cargo = Cargo.Crear(EmpresaBootstrapId, FolioInternoNotaCargo.FromAnioSecuencial(2026, Random.Shared.Next(800000, 999999)), proveedorId, sucursalId,
                    $"CARGOQ {token}", null, 100, "MXN", null, f.Id, null, null, fecha);
                db.AddRange(f, F($"FIXQ-{token}X1"), nc, cargo);
                await db.SaveChangesAsync();
            }

            Assert.Equal(2, (await Items(admin, $"/facturas?q={token}")).Count);
            var facturas = await Items(admin, $"/facturas?q={token}_1");
            var factura = Assert.Single(facturas);
            Assert.Equal("FIX010101ABC", factura.GetProperty("proveedorRfc").GetString());
            Assert.Equal(2, (await Items(admin, $"/facturas?q={token}")).Count);
            Assert.Empty(await Items(admin, $"/facturas?q={token}%251"));

            Assert.Single(await Items(admin, $"/notas-credito?q=NCQ-{token}"));
            Assert.Empty(await Items(admin, $"/notas-credito?q=NCQ-{token}-no"));

            var cargos = await Items(admin, $"/notas-cargo?q=CARGOQ {token}");
            Assert.Equal("FIX010101ABC", Assert.Single(cargos).GetProperty("proveedorRfc").GetString());
            Assert.Empty(await Items(admin, $"/notas-cargo?q=CARGOQ {token}-no"));
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            await db.NotasCargo.Where(n => n.ProveedorId == proveedorId).ExecuteDeleteAsync();
            await db.NotasCreditoProveedor.Where(n => n.ProveedorId == proveedorId).ExecuteDeleteAsync();
            await db.FacturasProveedor.Where(n => n.ProveedorId == proveedorId).ExecuteDeleteAsync();
            var eventos = await db.OutboxEntries.ToListAsync();
            db.OutboxEntries.RemoveRange(eventos.Where(o => o.Payload.Contains(proveedorId.ToString(), StringComparison.Ordinal)));
            await db.SaveChangesAsync();
        }
    }

    private static async Task<List<JsonElement>> Items(HttpClient c, string ruta)
    {
        var r = await c.GetAsync(Cxp + ruta);
        r.EnsureSuccessStatusCode();
        return (await Json(r)).GetProperty("items").EnumerateArray().ToList();
    }

    private sealed class PeriodoAbierto : IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }

    // Solo implementa lo obligatorio: el RFC en batch sale del método por defecto de la interfaz (vía ObtenerAsync).
    private sealed class Proveedor(Guid id) : IProveedorReadPort
    {
        public Task<ProveedorDto?> ObtenerAsync(Guid p, CancellationToken ct) =>
            Task.FromResult<ProveedorDto?>(p == id ? new(id, "FIX010101ABC", "FIX Proveedor Q", null, false, true) : null);
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string r, CancellationToken ct) => ObtenerAsync(id, ct);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(i => i, _ => "FIX Proveedor Q"));
    }
}
