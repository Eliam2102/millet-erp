using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.CreditoDisponible;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.CreditoDisponible;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

public sealed class CreditoDisponibleQueryTests
{
    [Fact]
    public async Task Devuelve_una_linea_por_moneda_con_disponible_igual_al_limite_sin_cartera_ni_pedidos()
    {
        using var db = CrearDbContext();
        var clienteId = Guid.NewGuid();
        db.LineasCredito.Add(CrearLinea(clienteId, "MXN", 500_000m, plazoDias: 45));
        db.LineasCredito.Add(CrearLinea(clienteId, "USD", 20_000m, plazoDias: 60));
        await db.SaveChangesAsync();

        var response = await Handle(db, clienteId);

        response.ClienteId.Should().Be(clienteId);
        response.Lineas.Should().HaveCount(2);

        var mxn = response.Lineas.Single(l => l.Moneda == "MXN");
        mxn.Limite.Should().Be(500_000m);
        mxn.Facturado.Should().Be(0m);
        mxn.LiberadoSinFactura.Should().Be(0m);
        mxn.Disponible.Should().Be(500_000m);
        mxn.Estado.Should().Be(EstadoLineaCredito.Activa);
    }

    [Fact]
    public async Task C7_1_liberado_sin_factura_real_y_sin_marca_de_dato_incompleto()
    {
        using var db = CrearDbContext();
        var clienteId = Guid.NewGuid();
        db.LineasCredito.Add(CrearLinea(clienteId, "MXN", 100_000m, plazoDias: 45));
        db.LineasCredito.Add(CrearLinea(clienteId, "USD", 10_000m, plazoDias: 45));
        await db.SaveChangesAsync();
        var pedidos = new PedidosSinFacturarFalso()
            .Agregar(clienteId, "6000100", 20_000m)
            .Agregar(clienteId, "6000101", 5_000m, anticipoLigado: 5_000m)
            .Agregar(clienteId, "6000102", 1_500m, moneda: "USD");

        var response = await Handle(db, clienteId, pedidos);

        response.DatoIncompleto.Should().BeFalse();
        var mxn = response.Lineas.Single(l => l.Moneda == "MXN");
        mxn.LiberadoSinFactura.Should().Be(20_000m);
        mxn.Disponible.Should().Be(80_000m);
        var usd = response.Lineas.Single(l => l.Moneda == "USD");
        usd.LiberadoSinFactura.Should().Be(1_500m);
        usd.Disponible.Should().Be(8_500m);
    }

    [Fact]
    public async Task Cliente_sin_lineas_devuelve_lista_vacia()
    {
        using var db = CrearDbContext();

        var response = await Handle(db, Guid.NewGuid());

        response.Lineas.Should().BeEmpty();
        response.DatoIncompleto.Should().BeFalse();
    }

    [Fact]
    public async Task Incluye_lineas_bloqueadas_con_su_estado()
    {
        using var db = CrearDbContext();
        var clienteId = Guid.NewGuid();
        var bloqueada = CrearLinea(clienteId, "MXN", 300_000m, plazoDias: 45);
        bloqueada.Bloquear("Cartera vencida > 90 días");
        db.LineasCredito.Add(bloqueada);
        await db.SaveChangesAsync();

        var response = await Handle(db, clienteId);

        response.Lineas.Should().ContainSingle()
            .Which.Estado.Should().Be(EstadoLineaCredito.Bloqueada);
    }

    [Fact]
    public async Task No_mezcla_lineas_de_otros_clientes()
    {
        using var db = CrearDbContext();
        var clienteId = Guid.NewGuid();
        db.LineasCredito.Add(CrearLinea(clienteId, "MXN", 100_000m, plazoDias: 45));
        db.LineasCredito.Add(CrearLinea(Guid.NewGuid(), "MXN", 999_999m, plazoDias: 45));
        await db.SaveChangesAsync();

        var response = await Handle(db, clienteId);

        response.Lineas.Should().ContainSingle()
            .Which.Limite.Should().Be(100_000m);
    }

    private static async Task<CreditoDisponibleResponse> Handle(
        CuentasPorCobrarDbContext db, Guid clienteId, PedidosSinFacturarFalso? pedidos = null)
    {
        var handler = new CreditoDisponibleHandler(db, pedidos ?? new PedidosSinFacturarFalso());
        return await handler.Handle(new CreditoDisponibleQuery(clienteId), CancellationToken.None);
    }

    private static LineaCreditoAggregate CrearLinea(
        Guid clienteId, string moneda, decimal limite, int plazoDias) =>
        LineaCreditoAggregate.Crear(
            empresaId: Guid.NewGuid(),
            clienteId: clienteId,
            moneda: moneda,
            limite: limite,
            origen: OrigenLineaCredito.Solunion,
            plazoDias: plazoDias);

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_test_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
