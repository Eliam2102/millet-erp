using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Reportes.AntiguedadSaldos;
using Millet.CuentasPorCobrar.Application.Reportes.Comun;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Reportes;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

public sealed class BucketsAntiguedadCxcTests
{
    private static readonly BucketsAntiguedadCxc Buckets = new([15, 30, 60, 90]);
    private static readonly DateOnly Corte = new(2026, 7, 14);

    [Theory]
    [InlineData(0, BucketsAntiguedadCxc.PorVencer)]   // vence hoy
    [InlineData(-10, BucketsAntiguedadCxc.PorVencer)] // aún en plazo
    [InlineData(1, "b1_15")]
    [InlineData(15, "b1_15")]
    [InlineData(16, "b16_30")]
    [InlineData(30, "b16_30")]
    [InlineData(31, "b31_60")]
    [InlineData(60, "b31_60")]
    [InlineData(61, "b61_90")]
    [InlineData(90, "b61_90")]
    [InlineData(91, "bMas90")]
    [InlineData(400, "bMas90")]
    public void Calcular_asigna_bucket_por_dias_vencidos(int diasVencidos, string esperado)
    {
        var vencimiento = Corte.AddDays(-diasVencidos);
        Buckets.Calcular(vencimiento, Corte).Should().Be(esperado);
    }

    [Fact]
    public void Keys_y_labels_derivan_de_los_limites_configurados()
    {
        var custom = new BucketsAntiguedadCxc([21, 31]);
        custom.Keys.Should().Equal("b1_21", "b22_31", "bMas31");
        custom.Labels.Should().Equal("1-21 días", "22-31 días", "+31 días");
    }

    [Fact]
    public void Limites_no_ascendentes_se_rechazan()
    {
        var act = () => new BucketsAntiguedadCxc([30, 15]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Options_sin_limites_usa_el_default()
    {
        // El ConfigurationBinder CONCATENA los elementos del appsettings
        // sobre el default de la propiedad, por eso BucketLimites arranca
        // vacío y el default vive en LimitesDefault (incidente 2026-07-14:
        // límites duplicados → 500 en /cartera/antiguedad).
        var buckets = new BucketsAntiguedadCxc(
            Options.Create(new ReportesCxcOptions()));
        buckets.Keys.Should().Equal("b1_15", "b16_30", "b31_60", "b61_90", "bMas90");
    }
}

public sealed class AntiguedadSaldosHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ClienteId = Guid.NewGuid();

    [Fact]
    public async Task Agrupa_por_cliente_moneda_con_por_vencer_y_vencido()
    {
        using var db = CrearDbContext();
        // Vence en 10 días → por_vencer.
        AgregarFactura(db, vencimiento: Ahora.AddDays(10), total: 1_000m);
        // Venció hace 20 días → b16_30.
        AgregarFactura(db, vencimiento: Ahora.AddDays(-20), total: 2_000m);
        // Venció hace 100 días → bMas90; con pago parcial de 500.
        var vieja = AgregarFactura(db, vencimiento: Ahora.AddDays(-100), total: 3_000m);
        vieja.AplicarPago(500m);
        // Línea con clasificación.
        db.LineasCredito.Add(LineaCreditoAggregate.Crear(
            Guid.NewGuid(), ClienteId, "MXN", 100_000m, OrigenLineaCredito.Solunion, 45, clasificacion: "B"));
        await db.SaveChangesAsync();

        var response = await Handle(db);

        var fila = response.Filas.Should().ContainSingle().Subject;
        fila["clasificacion"].Should().Be("B");
        fila["por_vencer"].Should().Be(1_000m);
        fila["b16_30"].Should().Be(2_000m);
        fila["bMas90"].Should().Be(2_500m);
        fila["vencido"].Should().Be(4_500m);
        fila["total"].Should().Be(5_500m);
        response.Totales!["vencido"].Should().Be(4_500m);
    }

    [Fact]
    public async Task Excluye_pagadas_y_canceladas()
    {
        using var db = CrearDbContext();
        var pagada = AgregarFactura(db, vencimiento: Ahora.AddDays(-5), total: 100m);
        pagada.AplicarPago(100m);
        var cancelada = AgregarFactura(db, vencimiento: Ahora.AddDays(-5), total: 200m);
        cancelada.Cancelar();
        await db.SaveChangesAsync();

        var response = await Handle(db);

        response.Filas.Should().BeEmpty();
    }

    [Fact]
    public async Task No_expone_cliente_id_y_el_filtro_muestra_la_razon_social()
    {
        using var db = CrearDbContext();
        AgregarFactura(db, vencimiento: Ahora.AddDays(-20), total: 100m);
        await db.SaveChangesAsync();

        var response = await Handle(db, new AntiguedadSaldosQuery(ClienteId: ClienteId));

        response.Columnas.Should().NotContain(c => c.Key == "cliente_id");
        response.Filas.Should().OnlyContain(f => !f.ContainsKey("cliente_id"));
        response.FiltrosAplicados.Should().Contain(
            f => f.Label == "Cliente" && f.Valor == "Vidrios del Golfo");
    }

    private static async Task<ReporteJsonResponse> Handle(
        CuentasPorCobrarDbContext db, AntiguedadSaldosQuery? query = null)
    {
        var handler = new AntiguedadSaldosHandler(
            db, new BucketsAntiguedadCxc([15, 30, 60, 90]), new FakeClientePort(), new FakeClock());
        return await handler.Handle(query ?? new AntiguedadSaldosQuery(), CancellationToken.None);
    }

    private static FacturaCartera AgregarFactura(
        CuentasPorCobrarDbContext db, DateTimeOffset vencimiento, decimal total)
    {
        var timbrado = vencimiento.AddDays(-45) < Ahora.AddDays(-400) ? vencimiento.AddDays(-1) : vencimiento.AddDays(-45);
        var factura = FacturaCartera.Crear(
            Guid.NewGuid(), Guid.NewGuid(), ClienteId, "VGL860910IU4", "Vidrios del Golfo",
            Guid.NewGuid().ToString(), "FV-1", total, "MXN", "PPD",
            fechaTimbrado: timbrado, fechaVencimiento: vencimiento);
        db.FacturasCartera.Add(factura);
        return factura;
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_antiguedad_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakeClientePort : IClienteReadPort
    {
        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(null);
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(
                new ClienteRefDto(clienteId, "VGL860910IU4", "Vidrios del Golfo", EsGenerico: false));
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
            string? rfc, string? razonSocial, IReadOnlyCollection<Guid>? ids, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteLookupCxcDto>>([]);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
