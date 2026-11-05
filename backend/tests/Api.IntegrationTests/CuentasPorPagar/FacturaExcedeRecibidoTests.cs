using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>
/// 86e3yxpy4: la captura contra OC cuyo neto excede lo recibido sin facturar se registra completa (201); el detalle deja el
/// excedente como retenido y solo lo recibido como elegible para pago. OC ficticia de 10 piezas a 20.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class FacturaExcedeRecibidoTests(WebApplicationFactory<Program> factory)
{
    private const string Facturas = "/api/v1/cuentas-por-pagar/facturas";

    [Fact]
    public async Task Registra_lo_que_excede_lo_recibido_y_retiene_el_excedente()
    {
        var oc = new OcDemo();
        await using var ambiente = new AdjuntosProveedorAmbiente(factory, services =>
        {
            services.AddSingleton<IComprasOcReadPort>(oc);
            services.AddScoped<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        var proveedorId = await ambiente.SeedProveedorAsync();
        oc.ProveedorId = proveedorId;
        using var admin = await ambiente.SuperAdminAsync();
        try
        {
            Task<HttpResponseMessage> Capturar(Guid ocId, decimal cantidad)
            {
                var ahora = DateTimeOffset.UtcNow;
                return admin.PostAsJsonAsync(Facturas, new CapturarFacturaConOcCommand(ocId, proveedorId, oc.SucursalId, null, null,
                    $"DEMO-EXC-{Guid.NewGuid():N}"[..20], null, ahora, ahora, DateOnly.FromDateTime(ahora.UtcDateTime).AddDays(30), "MXN", null,
                    cantidad * 20, 0, 0, 0, cantidad * 20,
                    [new(oc.ArticuloId, null, "Material DEMO", cantidad, "H87", "Pieza", 20, cantidad * 20, null, ocId, null)]));
            }

            // Factura de 10 con 6 recibidas: se registra completa (200) con 80 retenidos y 120 elegibles.
            var parcial = oc.Nueva(6);
            var creada = await Capturar(parcial, 10);
            Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
            var id = (await creada.Content.ReadFromJsonAsync<CapturarFacturaConOcResponse>())!.Id;
            var detalle = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Facturas}/{id}");
            Assert.Equal(80m, detalle.GetProperty("retenido").GetDecimal());
            Assert.Equal(120m, detalle.GetProperty("elegible").GetDecimal());
            // Sin recepción alguna también se registra, todo retenido.
            var sinRecepcion = oc.Nueva(0);
            Assert.Equal(HttpStatusCode.Created, (await Capturar(sinRecepcion, 10)).StatusCode);

            using var scope = ambiente.Factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            Assert.Equal(1, await db.FacturasProveedor.CountAsync(f => f.OrdenCompraId == sinRecepcion));
            Assert.Equal(1, await db.FacturasProveedor.CountAsync(f => f.OrdenCompraId == parcial));
        }
        finally
        {
            using var scope = ambiente.Factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor
                .Where(f => f.ProveedorId == proveedorId).ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Proveedores
                .Where(p => p.Id == proveedorId).ExecuteDeleteAsync();
        }
    }

    /// <summary>Cada OC ficticia (la línea toma el Id de la OC) con su cantidad recibida en Compras.</summary>
    private sealed class OcDemo : IComprasOcReadPort
    {
        private readonly Dictionary<Guid, decimal> _recibido = [];
        public Guid SucursalId { get; } = Guid.NewGuid();
        public Guid ArticuloId { get; } = Guid.NewGuid();
        public Guid ProveedorId { get; set; }
        public Guid Nueva(decimal recibido) { var id = Guid.NewGuid(); _recibido[id] = recibido; return id; }
        public Task<OrdenCompraDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OrdenCompraDto?>(
            _recibido.TryGetValue(id, out var recibido)
                ? new(id, "OC-DEMO-EXC", ProveedorId, AdjuntosProveedorAmbiente.EmpresaInicialId, SucursalId, 232, "Autorizada",
                    [new(id, ArticuloId, 10, 20, 0, recibido)])
                : null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.Where(_recibido.ContainsKey).ToDictionary(i => i, _ => "OC-DEMO-EXC"));
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OrdenCompraDto>>([]);
    }

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
