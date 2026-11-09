using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.CreditoDisponible;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.CreditoDisponible;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

/// <summary>
/// M2 (prueba S3-4, supuesto a validar con Crédito): el pedido que CxC libera resta crédito de inmediato, aunque A+W no
/// lo haya pasado a 40, y no se cuenta dos veces cuando llega a 40.
/// </summary>
public sealed class LiberadoPorCxcTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pedido_liberado_por_CxC_antes_de_estatus_40_resta_credito()
    {
        using var db = await DbConLinea(100_000m);
        Decidir(db, "10425102", ResultadoLiberacion.Liberado);
        await db.SaveChangesAsync();
        var pedidos = new PedidosSinFacturarFalso()
            .Agregar(Cliente, "10425101", 20_000m)                               // estatus 42
            .Agregar(Cliente, "10425102", 30_000m, comprometeCredito: false);     // estatus 10, liberado por CxC

        var mxn = (await Consultar(db, pedidos)).Lineas.Single();

        mxn.LiberadoSinFactura.Should().Be(50_000m);
        mxn.Disponible.Should().Be(50_000m);
    }

    [Fact]
    public async Task Al_llegar_a_40_el_pedido_liberado_no_se_cuenta_dos_veces()
    {
        using var db = await DbConLinea(100_000m);
        Decidir(db, "10425102", ResultadoLiberacion.Liberado);
        await db.SaveChangesAsync();
        var pedidos = new PedidosSinFacturarFalso().Agregar(Cliente, "10425102", 30_000m); // ya en 40

        (await Consultar(db, pedidos)).Lineas.Single().LiberadoSinFactura.Should().Be(30_000m);
    }

    [Theory]
    [InlineData(ResultadoLiberacion.Retenido)]
    [InlineData(ResultadoLiberacion.PendienteAutorizacion)]
    public async Task Pedido_previo_a_40_sin_liberar_no_resta(ResultadoLiberacion resultado)
    {
        using var db = await DbConLinea(100_000m);
        Decidir(db, "78000101", resultado);
        await db.SaveChangesAsync();
        var pedidos = new PedidosSinFacturarFalso()
            .Agregar(Cliente, "78000101", 5_800m, comprometeCredito: false)
            .Agregar(Cliente, "10425199", 9_000m, comprometeCredito: false); // sin decisión

        (await Consultar(db, pedidos)).Lineas.Single().LiberadoSinFactura.Should().Be(0m);
    }

    [Fact]
    public async Task Cuenta_la_ultima_decision_del_pedido()
    {
        using var db = await DbConLinea(100_000m);
        Decidir(db, "10425103", ResultadoLiberacion.Liberado, Ahora.AddHours(-2));
        Decidir(db, "10425103", ResultadoLiberacion.Retenido, Ahora);
        Decidir(db, "10425104", ResultadoLiberacion.PendienteAutorizacion, Ahora.AddHours(-2));
        Decidir(db, "10425104", ResultadoLiberacion.LiberadoConOverride, Ahora);
        await db.SaveChangesAsync();
        var pedidos = new PedidosSinFacturarFalso()
            .Agregar(Cliente, "10425103", 10_000m, comprometeCredito: false)
            .Agregar(Cliente, "10425104", 7_000m, comprometeCredito: false);

        (await Consultar(db, pedidos)).Lineas.Single().LiberadoSinFactura.Should().Be(7_000m);
    }

    private static async Task<CreditoDisponibleResponse> Consultar(CuentasPorCobrarDbContext db, PedidosSinFacturarFalso pedidos) =>
        await new CreditoDisponibleHandler(db, pedidos).Handle(new CreditoDisponibleQuery(Cliente), CancellationToken.None);

    private static void Decidir(CuentasPorCobrarDbContext db, string pedido, ResultadoLiberacion resultado, DateTimeOffset? en = null)
    {
        var pendiente = resultado == ResultadoLiberacion.PendienteAutorizacion;
        var conOverride = resultado == ResultadoLiberacion.LiberadoConOverride;
        db.DecisionesLiberacion.Add(DecisionLiberacion.Emitir(Empresa, pedido, Cliente, "MXN", 1_000m, 0m, resultado,
            pendiente ? ReglaAplicadaLiberacion.Cobertura : conOverride ? ReglaAplicadaLiberacion.Override : ReglaAplicadaLiberacion.Credito,
            conOverride ? Guid.NewGuid() : null, Guid.NewGuid(), en ?? Ahora, porcentajeCobertura: pendiente ? 60m : null));
    }

    private static async Task<CuentasPorCobrarDbContext> DbConLinea(decimal limite)
    {
        var db = new CuentasPorCobrarDbContext(
            new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_m2_{Guid.NewGuid()}").Options,
            new EmpresaContext());
        db.LineasCredito.Add(LineaCreditoAggregate.Crear(Empresa, Cliente, "MXN", limite, OrigenLineaCredito.Solunion, plazoDias: 45));
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
