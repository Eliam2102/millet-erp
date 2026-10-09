using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Application.EventListeners;
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
            remittance, lineas ?? UnaLinea, tolerancia, Guid.NewGuid());

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
        var propuesta = Crear(montoDeposito: 10_100m);
        propuesta.AjusteNoFiscal.Should().Be(0);
        propuesta.SaldoAFavorPorIdentificar.Should().Be(100m);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reservas_pendientes_o_confirmadas_sin_rep_no_exceden_saldo(bool confirmar)
    {
        using var db = CrearDbContext(); AgregarFactura(db, "reserva", 1000); await db.SaveChangesAsync();
        var primera = await Handle(db, new FakePublisher(), Comando(700, [new("reserva", 700, null)]));
        var resolver = new ResolverPropuestaTesoreriaHandler(db, new FakeClock());
        if (confirmar)
            await resolver.Handle(new(Guid.NewGuid(), EmpresaId, primera.Id, Guid.NewGuid(), Ahora, Guid.NewGuid(), null), default);
        var segunda = () => Handle(db, new FakePublisher(), Comando(400, [new("reserva", 400, null)]));
        await segunda.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PAP_IMPORTE_EXCEDE_SALDO");
    }

    [Fact]
    public async Task Rechazo_de_tesoreria_libera_reserva_y_permite_nueva_propuesta()
    {
        using var db = CrearDbContext(); AgregarFactura(db, "rechazo", 1000); await db.SaveChangesAsync();
        var primera = await Handle(db, new FakePublisher(), Comando(1000, [new("rechazo", 1000, null)]));
        var resolver = new ResolverPropuestaTesoreriaHandler(db, new FakeClock());
        var rechazo = new ResolverPropuestaTesoreriaCommand(Guid.NewGuid(), EmpresaId, primera.Id, Guid.NewGuid(), Ahora, null, "No existe depósito");
        await resolver.Handle(rechazo, default); await resolver.Handle(rechazo, default);
        (await db.PropuestasAplicacionPago.FindAsync(primera.Id))!.Estado.Should().Be(EstadoPropuestaAplicacion.Rechazada);
        var nueva = await Handle(db, new FakePublisher(), Comando(1100, [new("rechazo", 1000, null)]));
        nueva.SaldoAFavorPorIdentificar.Should().Be(100);
        nueva.Id.Should().NotBe(primera.Id);
        db.EventosProcesados.Should().ContainSingle();
    }

    [Fact]
    public async Task Confirmacion_de_tesoreria_es_idempotente_y_no_aplica_cartera()
    {
        using var db = CrearDbContext(); var factura = AgregarFactura(db, "confirma", 1000); await db.SaveChangesAsync();
        var primera = await Handle(db, new FakePublisher(), Comando(1000, [new("confirma", 1000, null)]));
        var resolver = new ResolverPropuestaTesoreriaHandler(db, new FakeClock());
        var confirmar = new ResolverPropuestaTesoreriaCommand(Guid.NewGuid(), EmpresaId, primera.Id, Guid.NewGuid(), Ahora, Guid.NewGuid(), null);
        await resolver.Handle(confirmar, default); await resolver.Handle(confirmar, default);
        var propuesta = (await db.PropuestasAplicacionPago.FindAsync(primera.Id))!;
        propuesta.Estado.Should().Be(EstadoPropuestaAplicacion.Confirmada);
        propuesta.MovimientoBancarioId.Should().Be(confirmar.MovimientoBancarioId);
        factura.SaldoPendiente.Should().Be(1000);
        db.EventosProcesados.Should().ContainSingle();
    }

    [Fact]
    public async Task Confirmacion_del_proponente_se_rechaza()
    {
        using var db = CrearDbContext(); AgregarFactura(db, "autoconfirma", 1000); await db.SaveChangesAsync();
        var primera = await Handle(db, new FakePublisher(), Comando(1000, [new("autoconfirma", 1000, null)]));
        var act = () => new ResolverPropuestaTesoreriaHandler(db, new FakeClock()).Handle(
            new(Guid.NewGuid(), EmpresaId, primera.Id, primera.PropuestoPor!.Value, Ahora, Guid.NewGuid(), null), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEP_MISMO_USUARIO");
    }

    [Fact]
    public async Task Rep_timbrado_libera_reserva_sin_descontar_dos_veces()
    {
        using var db = CrearDbContext(); var factura = AgregarFactura(db, "rep", 1000); await db.SaveChangesAsync();
        var primera = await Handle(db, new FakePublisher(), Comando(700, [new("rep", 700, 1)]));
        var movimiento = Guid.NewGuid();
        await new ResolverPropuestaTesoreriaHandler(db, new FakeClock()).Handle(
            new(Guid.NewGuid(), EmpresaId, primera.Id, Guid.NewGuid(), Ahora, movimiento, null), default);
        var recibo = new ReciboPagoTimbradoCommand(Guid.NewGuid(), new(EmpresaId, Ahora, Guid.NewGuid(), "REP-P5", 700, 0,
            [new(factura.FacturaVentaId, 700, 1, "MXN", 300)], movimiento));
        var timbrado = new ReciboPagoTimbradoHandler(db, new FakeClock(), NullLogger<ReciboPagoTimbradoHandler>.Instance);
        await timbrado.Handle(recibo, default); await timbrado.Handle(recibo, default);
        factura.SaldoPendiente.Should().Be(300);
        (await db.PropuestasAplicacionPago.FindAsync(primera.Id))!.ReppTimbrado.Should().BeTrue();
        var segunda = await Handle(db, new FakePublisher(), Comando(300, [new("rep", 300, 2)]));
        segunda.Estado.Should().Be(EstadoPropuestaAplicacion.Propuesta);
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
            Options.Create(new AplicacionPagosOptions()), new FakeClock(), new FakeUser());
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

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public string? UserName => "Proponente de prueba";
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
