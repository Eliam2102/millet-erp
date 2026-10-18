using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compras.Domain.Ports.Almacen;
using static Millet.Compras.IntegrationTests.Autorizacion.AutorizacionTestSeeding;

namespace Millet.Compras.IntegrationTests.Autorizacion;

/// <summary>
/// La bandeja de pendientes de autorización (<c>GET /api/v1/compras/pendientes-autorizacion</c>)
/// calcula la disponibilidad con el mismo reparto que la autorización: una línea de
/// artículo Servicio no consulta stock, aunque el puerto reporte existencia.
/// </summary>
public class PendientesServicioSinStockTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string EndpointPendientes = "/api/v1/compras/pendientes-autorizacion";

    private readonly StubsWebApplicationFactory _factory;

    public PendientesServicioSinStockTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Pendientes_LineaDeServicio_No_Consulta_Stock()
    {
        var stock = new ContadorStock(disponible: 100m);
        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConsultarStockPort>();
                services.AddSingleton<IConsultarStockPort>(stock);
            });
        });
        var admin = await CreateSuperAdminClientAsync(factory);
        var rqId = await CrearTransmitirAsync(admin, new[] { (ArticuloServicio, 10m) });

        var ids = await IdsPendientesAsync(admin);

        Assert.Contains(rqId, ids);
        Assert.DoesNotContain(ArticuloServicio, stock.Consultas);
    }

    /// <summary>
    /// Pagina toda la bandeja (la BD es compartida entre tests) y devuelve los ids.
    /// </summary>
    private static async Task<HashSet<Guid>> IdsPendientesAsync(HttpClient client)
    {
        var ids = new HashSet<Guid>();
        const int limit = 200;
        var offset = 0;
        while (true)
        {
            var resp = await client.GetAsync($"{EndpointPendientes}?limit={limit}&offset={offset}");
            resp.EnsureSuccessStatusCode();
            var items = (await ReadJsonAsync(resp)).GetProperty("items").EnumerateArray().ToList();
            foreach (var item in items)
            {
                ids.Add(item.GetProperty("id").GetGuid());
            }

            if (items.Count < limit)
            {
                return ids;
            }

            offset += limit;
        }
    }

    /// <summary>Stock fijo que registra qué artículos se consultaron.</summary>
    private sealed class ContadorStock : IConsultarStockPort
    {
        private readonly decimal _disponible;

        public ContadorStock(decimal disponible) => _disponible = disponible;

        public List<Guid> Consultas { get; } = new();

        public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
            Guid sucursalId, Guid articuloId, CancellationToken cancellationToken)
        {
            Consultas.Add(articuloId);
            return Task.FromResult(new DisponibilidadStock(OnHand: _disponible, Disponible: _disponible));
        }
    }
}
