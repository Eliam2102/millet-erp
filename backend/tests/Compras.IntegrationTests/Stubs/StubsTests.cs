using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Stubs;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Stubs;

/// <summary>
/// Tests integration de los 5 stubs cross-module (F3-PR2). Ejecutan
/// los puertos resueltos por DI con <c>Compras:UseStubs=true</c> en
/// <c>appsettings.Test.json</c>.
///
/// <para>
/// El stub de stock está configurado con dos overrides:
/// </para>
/// <list type="bullet">
///   <item><c>...aaa</c> → 0.5 (mitad del stock)</item>
///   <item><c>...bbb</c> → 0.0 (sin stock — fuerza bifurcación)</item>
/// </list>
/// <para>
/// Cualquier otro artículo cae en el <c>DefaultRatio</c> (1.0 → todo cubierto).
/// </para>
/// </summary>
public class StubsTests : IClassFixture<StubsWebApplicationFactory>
{
    private static readonly Guid ArticuloMitad = Guid.Parse("00000000-0000-0000-0000-000000000aaa");
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public StubsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // --- IConsultarStockPort ---

    [Fact]
    public async Task ConsultarStock_ConRatioDefault_DevuelveOnHand_Maximo()
    {
        using var scope = _factory.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IConsultarStockPort>();

        var resultado = await port.ConsultarPorSucursalAsync(
            sucursalId: Guid.NewGuid(),
            articuloId: Guid.NewGuid(), // sin override → DefaultRatio = 1.0
            CancellationToken.None);

        Assert.Equal(InMemoryConsultarStockPort.OnHandEscala, resultado.OnHand);
        Assert.Equal(InMemoryConsultarStockPort.OnHandEscala, resultado.Disponible);
    }

    [Fact]
    public async Task ConsultarStock_ConOverride_AplicaRatioPorArticulo()
    {
        using var scope = _factory.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IConsultarStockPort>();

        var mitad = await port.ConsultarPorSucursalAsync(Guid.NewGuid(), ArticuloMitad, CancellationToken.None);
        var cero = await port.ConsultarPorSucursalAsync(Guid.NewGuid(), ArticuloSinStock, CancellationToken.None);

        Assert.Equal(InMemoryConsultarStockPort.OnHandEscala * 0.5m, mitad.OnHand);
        Assert.Equal(0m, cero.OnHand);
    }

    // --- IGenerarSolicitudCompraPort (persiste a oc_borrador_stub) ---

    [Fact]
    public async Task GenerarSolicitudCompra_PersisteFila_Y_ContenidoEsRecuperable()
    {
        using var scope = _factory.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IGenerarSolicitudCompraPort>();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        // Sin auth real, ICurrentEmpresaContext no tiene empresa. Bypass
        // para que el interceptor permita el INSERT del stub.
        using var bypass = empresaContext.Bypass();

        var origenRq = Guid.NewGuid();
        var saldo = new List<LineaSaldo>
        {
            new(
                LineaRequisicionId: Guid.NewGuid(),
                ArticuloId: ArticuloSinStock,
                CantidadSaldo: 100m,
                UnidadMedida: "PZA",
                PrecioEstimado: Millet.SharedKernel.Domain.Money.Mxn(15m),
                CuentaContableId: null,
                CentroCostoId: null,
                Proyecto: null),
        };

        var ocId = await port.GenerarBorradorAsync(origenRq, saldo, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, ocId);

        var fila = await db.OcBorradorStubs
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == ocId);

        Assert.NotNull(fila);
        Assert.Equal(origenRq, fila!.OrigenRequisicionId);

        var deserialized = JsonSerializer.Deserialize<List<LineaSaldo>>(fila.LineasJson);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized!);
        Assert.Equal(ArticuloSinStock, deserialized![0].ArticuloId);
        Assert.Equal(100m, deserialized[0].CantidadSaldo);
    }
}
