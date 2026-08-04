using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

public sealed class PropuestaAplicacionAggregateTests
{
    private static readonly (string, Guid, decimal, int?)[] UnaLinea =
        [("uuid-1", Guid.NewGuid(), 10_000m, 1)];

    private static PropuestaAplicacionPago Crear(
        decimal montoDeposito = 10_000m,
        string remittance = "REM-001",
        IReadOnlyList<(string, Guid, decimal, int?)>? lineas = null,
        decimal tolerancia = 50m) =>
        PropuestaAplicacionPago.Crear(
            Guid.NewGuid(), Guid.NewGuid(), "DEP-123", montoDeposito, "USD",
            remittance, lineas ?? UnaLinea, tolerancia);

    [Fact]
    public void Crear_cuadrada_sin_ajuste_OK()
    {
        var p = Crear();
        p.Estado.Should().Be(EstadoPropuestaAplicacion.Propuesta);
        p.AjusteNoFiscal.Should().Be(0m);
        p.Facturas.Should().HaveCount(1);
    }

    [Fact]
    public void Deposito_corto_dentro_de_tolerancia_genera_ajuste_negativo()
    {
        var p = Crear(montoDeposito: 9_970m);
        p.AjusteNoFiscal.Should().Be(-30m);
    }

    [Fact]
    public void Deposito_corto_fuera_de_tolerancia_se_rechaza()
    {
        var act = () => Crear(montoDeposito: 9_940m); // diferencia 60 ≥ 50
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_TOLERANCIA_EXCEDIDA");
    }

    [Fact]
    public void Deposito_excedente_no_es_ajuste()
    {
        var act = () => Crear(montoDeposito: 10_100m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_DEPOSITO_EXCEDENTE");
    }

    [Fact]
    public void Sin_remittance_no_hay_propuesta()
    {
        var act = () => Crear(remittance: "  ");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_SIN_REMITTANCE");
    }

    [Fact]
    public void Factura_duplicada_se_rechaza()
    {
        var facturaId = Guid.NewGuid();
        var act = () => Crear(
            montoDeposito: 200m,
            lineas: [("uuid-x", facturaId, 100m, 1), ("uuid-x", facturaId, 100m, 2)]);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_FACTURA_DUPLICADA");
    }

    [Fact]
    public void Confirmar_y_rechazar_solo_desde_Propuesta()
    {
        var usuario = Guid.NewGuid();
        var ahora = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

        var p = Crear();
        p.Confirmar(usuario, ahora);
        p.Estado.Should().Be(EstadoPropuestaAplicacion.Confirmada);
        p.ResueltaPor.Should().Be(usuario);

        var act = () => p.Rechazar(usuario, "tarde", ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_YA_RESUELTA");
    }

    [Fact]
    public void Rechazar_exige_motivo()
    {
        var p = Crear();
        var act = () => p.Rechazar(Guid.NewGuid(), " ", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAP_MOTIVO_VACIO");
    }
}

public sealed class CrearPropuestaAplicacionHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    [Fact]
    public async Task Matching_valido_crea_propuesta_y_publica_evento()
    {
        using var db = CrearDbContext();
        var factura = AgregarFactura(db, uuid: "uuid-a", total: 10_000m);
        await db.SaveChangesAsync();

        var publisher = new FakePublisher();
        var response = await Handle(db, publisher, Comando(
            monto: 10_000m, [new PropuestaFacturaLinea("uuid-a", 10_000m, 1)]));

        response.Estado.Should().Be(EstadoPropuestaAplicacion.Propuesta);
        response.Facturas.Single().FacturaCarteraId.Should().Be(factura.Id);
        response.Facturas.Single().Folio.Should().Be("FV-1");
        publisher.Publicados.Should().ContainSingle()
            .Which.Should().BeOfType<PropuestaAplicacionPagoCreadaEvent>();
    }

    [Fact]
    public async Task Factura_fuera_de_cartera_falla()
    {
        using var db = CrearDbContext();

        var act = () => Handle(db, new FakePublisher(), Comando(
            monto: 100m, [new PropuestaFacturaLinea("uuid-inexistente", 100m, null)]));

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task Importe_mayor_al_saldo_falla()
    {
        using var db = CrearDbContext();
        var factura = AgregarFactura(db, uuid: "uuid-b", total: 1_000m);
        factura.AplicarPago(800m);
        await db.SaveChangesAsync();

        var act = () => Handle(db, new FakePublisher(), Comando(
            monto: 500m, [new PropuestaFacturaLinea("uuid-b", 500m, null)]));

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "PAP_IMPORTE_EXCEDE_SALDO");
    }

    [Fact]
    public async Task Factura_de_otro_cliente_falla()
    {
        using var db = CrearDbContext();
        AgregarFactura(db, uuid: "uuid-c", total: 1_000m, clienteId: Guid.NewGuid());
        await db.SaveChangesAsync();

        var act = () => Handle(db, new FakePublisher(), Comando(
            monto: 1_000m, [new PropuestaFacturaLinea("uuid-c", 1_000m, null)]));

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "PAP_FACTURA_DE_OTRO_CLIENTE");
    }

    // --------------------------------------------------- helpers

    private static CrearPropuestaAplicacionCommand Comando(
        decimal monto, IReadOnlyList<PropuestaFacturaLinea> lineas) =>
        new(ClienteId, "DEP-9", monto, "MXN", "REM-9", lineas);

    private static async Task<PropuestaAplicacionResponse> Handle(
        CuentasPorCobrarDbContext db, FakePublisher publisher, CrearPropuestaAplicacionCommand command)
    {
        var handler = new CrearPropuestaAplicacionHandler(
            db, publisher, new FakeEmpresaContext(EmpresaId),
            Options.Create(new AplicacionPagosOptions()), new FakeClock());
        return await handler.Handle(command, CancellationToken.None);
    }

    private static FacturaCartera AgregarFactura(
        CuentasPorCobrarDbContext db, string uuid, decimal total, Guid? clienteId = null)
    {
        var factura = FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), clienteId ?? ClienteId, "VGL860910IU4", "Cliente",
            uuid, "FV-1", total, "MXN", "PPD", Ahora.AddDays(-30), Ahora.AddDays(15));
        db.FacturasCartera.Add(factura);
        return factura;
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_pap_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext(EmpresaId));
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
