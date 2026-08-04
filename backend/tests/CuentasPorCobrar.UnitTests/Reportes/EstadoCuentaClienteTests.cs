using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Reportes;

public sealed class EstadoCuentaClienteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ClienteId = Guid.NewGuid();

    [Fact]
    public async Task Incluye_facturas_movimientos_y_anticipos_con_saldo()
    {
        using var db = CrearDbContext();
        var factura = FacturaCartera.Crear(
            Guid.NewGuid(), Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            Guid.NewGuid().ToString(), "FV-77", 10_000m, "MXN", "PPD", Ahora.AddDays(-30), Ahora.AddDays(15));
        factura.AplicarPago(4_000m);
        db.FacturasCartera.Add(factura);
        db.MovimientosCartera.Add(new MovimientoCartera(
            factura.EmpresaId, factura.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), 4_000m, Ahora.AddDays(-10)));
        await db.SaveChangesAsync();

        var anticipos = new[]
        {
            new AnticipoSaldoClienteDto(Guid.NewGuid(), ClienteId, "Abierto", 5_000m, 0m, 5_000m, 5_000m, "MXN", "PED-1"),
            new AnticipoSaldoClienteDto(Guid.NewGuid(), ClienteId, "Amortizado", 2_000m, 2_000m, 0m, 0m, "MXN", null),
        };

        var handler = new EstadoCuentaClienteHandler(
            db, new FakePort(anticipos), new FakeClientePort(), new FakeClock());
        var response = await handler.Handle(new EstadoCuentaClienteQuery(ClienteId), CancellationToken.None);

        response.Filas.Should().HaveCount(3); // factura + pago + 1 anticipo con saldo
        response.Filas.Select(f => f["tipo"]).Should().Contain(
            ["Factura", "Pago", "Anticipo (saldo disponible)"]);
        response.Totales!["cargo"].Should().Be(10_000m);
        response.Totales!["abono"].Should().Be(9_000m); // 4,000 pago + 5,000 anticipo
        response.Totales!["saldo_documento"].Should().Be(6_000m);

        // Sin UUIDs de cara al usuario: la referencia del pago es el folio y
        // el filtro Cliente resuelve la razón social (feedback 2026-07-14).
        response.Filas.Single(f => (string?)f["tipo"] == "Pago")["referencia"].Should().Be("FV-77");
        response.FiltrosAplicados.Should().Contain(
            f => f.Label == "Cliente" && f.Valor == "Vidrios del Golfo");
    }

    [Fact]
    public async Task Movimientos_revertidos_no_aparecen()
    {
        using var db = CrearDbContext();
        var factura = FacturaCartera.Crear(
            Guid.NewGuid(), Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            Guid.NewGuid().ToString(), "FV-78", 1_000m, "MXN", "PUE", Ahora.AddDays(-5), Ahora.AddDays(-5));
        db.FacturasCartera.Add(factura);
        var mov = new MovimientoCartera(
            factura.EmpresaId, factura.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), 1_000m, Ahora.AddDays(-2));
        mov.Revertir(Ahora);
        db.MovimientosCartera.Add(mov);
        await db.SaveChangesAsync();

        var handler = new EstadoCuentaClienteHandler(
            db, new FakePort([]), new FakeClientePort(), new FakeClock());
        var response = await handler.Handle(new EstadoCuentaClienteQuery(ClienteId), CancellationToken.None);

        response.Filas.Should().ContainSingle().Which["tipo"].Should().Be("Factura");
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_edocta_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakePort(IReadOnlyList<AnticipoSaldoClienteDto> anticipos) : IFacturacionAnticiposReadPort
    {
        public Task<IReadOnlyList<AnticipoSaldoClienteDto>> ListarPorClienteAsync(
            Guid clienteId, CancellationToken ct) => Task.FromResult(anticipos);
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
