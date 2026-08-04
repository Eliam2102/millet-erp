using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Cartera;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

public sealed class FacturacionEventHandlersTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ClienteId = Guid.NewGuid();
    private const string Rfc = "VGL860910IU4";

    // --------------------------------------------------- FacturaVentaTimbrada

    [Fact]
    public async Task FacturaVentaTimbrada_proyecta_con_cliente_y_vencimiento_por_plazo_de_linea()
    {
        using var db = CrearDbContext();
        db.LineasCredito.Add(LineaCreditoAggregate.Crear(
            Guid.NewGuid(), ClienteId, "MXN", 500_000m, OrigenLineaCredito.Solunion, plazoDias: 45));
        await db.SaveChangesAsync();

        var payload = PayloadFactura(facturaVentaId: Guid.NewGuid());
        await HandleFactura(db, Guid.NewGuid(), payload);

        var f = await db.FacturasCartera.SingleAsync();
        f.ClienteId.Should().Be(ClienteId);
        f.Estado.Should().Be(EstadoFacturaCartera.Abierta);
        f.FechaVencimiento.Should().Be(payload.FechaTimbrado!.Value.AddDays(45));
    }

    [Fact]
    public async Task FacturaVentaTimbrada_sin_match_de_rfc_queda_sin_cliente_y_vence_al_timbrado()
    {
        using var db = CrearDbContext();

        var payload = PayloadFactura(facturaVentaId: Guid.NewGuid()) with { ReceptorRfc = "ZZZ010101ZZ9" };
        await HandleFactura(db, Guid.NewGuid(), payload, clientePort: new FakeClientePort(null));

        var f = await db.FacturasCartera.SingleAsync();
        f.ClienteId.Should().BeNull();
        f.FechaVencimiento.Should().Be(payload.FechaTimbrado!.Value);
    }

    [Fact]
    public async Task FacturaVentaTimbrada_replay_mismo_evento_no_duplica()
    {
        using var db = CrearDbContext();
        var eventId = Guid.NewGuid();
        var payload = PayloadFactura(facturaVentaId: Guid.NewGuid());

        await HandleFactura(db, eventId, payload);
        await HandleFactura(db, eventId, payload);

        (await db.FacturasCartera.CountAsync()).Should().Be(1);
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FacturaVentaTimbrada_reemision_con_otro_eventId_hace_upsert_no_duplica()
    {
        using var db = CrearDbContext();
        var payload = PayloadFactura(facturaVentaId: Guid.NewGuid());

        await HandleFactura(db, Guid.NewGuid(), payload);
        await HandleFactura(db, Guid.NewGuid(), payload);

        (await db.FacturasCartera.CountAsync()).Should().Be(1);
        (await db.EventosProcesados.CountAsync()).Should().Be(2);
    }

    // --------------------------------------------------- ReciboPagoTimbrado

    [Fact]
    public async Task ReciboPagoTimbrado_aplica_desglose_y_registra_movimientos()
    {
        using var db = CrearDbContext();
        var factura = await ProyectarFactura(db, total: 100_000m);
        var reppId = Guid.NewGuid();

        var handler = new ReciboPagoTimbradoHandler(db, new FakeClock(), NullLogger<ReciboPagoTimbradoHandler>.Instance);
        await handler.Handle(new ReciboPagoTimbradoCommand(Guid.NewGuid(), new ReciboPagoTimbradoPayload(
            factura.EmpresaId, Ahora, reppId, "uuid-repp", 40_000m, 0m,
            [new ReppFacturaPagadaPayload(factura.FacturaVentaId, 40_000m, 1, "MXN", 60_000m)])),
            CancellationToken.None);

        var f = await db.FacturasCartera.SingleAsync();
        f.MontoPagado.Should().Be(40_000m);
        f.Estado.Should().Be(EstadoFacturaCartera.Parcial);

        var mov = await db.MovimientosCartera.SingleAsync();
        mov.OrigenComprobanteId.Should().Be(reppId);
        mov.Tipo.Should().Be(TipoMovimientoCartera.Pago);
    }

    [Fact]
    public async Task ReciboPagoTimbrado_factura_no_proyectada_lanza_para_retry()
    {
        using var db = CrearDbContext();

        var handler = new ReciboPagoTimbradoHandler(db, new FakeClock(), NullLogger<ReciboPagoTimbradoHandler>.Instance);
        var act = () => handler.Handle(new ReciboPagoTimbradoCommand(Guid.NewGuid(), new ReciboPagoTimbradoPayload(
            Guid.NewGuid(), Ahora, Guid.NewGuid(), "uuid", 10m, 0m,
            [new ReppFacturaPagadaPayload(Guid.NewGuid(), 10m, 1, "MXN", 0m)])),
            CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    // --------------------------------------------------- NotaCreditoTimbrada

    [Fact]
    public async Task NotaCreditoTimbrada_acumula_monto_nc()
    {
        using var db = CrearDbContext();
        var factura = await ProyectarFactura(db, total: 50_000m);
        var ncId = Guid.NewGuid();

        var handler = new NotaCreditoTimbradaHandler(db, new FakeClock(), NullLogger<NotaCreditoTimbradaHandler>.Instance);
        await handler.Handle(new NotaCreditoTimbradaCommand(Guid.NewGuid(), new NotaCreditoTimbradaPayload(
            factura.EmpresaId, Ahora, ncId, "Bonificacion", "uuid-nc", 5_000m, factura.FacturaVentaId, null)),
            CancellationToken.None);

        var f = await db.FacturasCartera.SingleAsync();
        f.MontoNc.Should().Be(5_000m);
        f.Estado.Should().Be(EstadoFacturaCartera.Parcial);
    }

    // --------------------------------------------------- ComprobanteCancelado

    [Fact]
    public async Task ComprobanteCancelado_Ingreso_cancela_la_factura()
    {
        using var db = CrearDbContext();
        var factura = await ProyectarFactura(db, total: 10_000m);

        var handler = new ComprobanteCanceladoHandler(db, new FakeClock(), NullLogger<ComprobanteCanceladoHandler>.Instance);
        await handler.Handle(new ComprobanteCanceladoCommand(Guid.NewGuid(), new ComprobanteCanceladoPayload(
            factura.EmpresaId, Ahora, factura.FacturaVentaId, "Ingreso", factura.Uuid)),
            CancellationToken.None);

        (await db.FacturasCartera.SingleAsync()).Estado.Should().Be(EstadoFacturaCartera.Cancelada);
    }

    [Fact]
    public async Task ComprobanteCancelado_Pago_revierte_los_movimientos_del_repp()
    {
        using var db = CrearDbContext();
        var factura = await ProyectarFactura(db, total: 100m);
        var reppId = Guid.NewGuid();

        var reppHandler = new ReciboPagoTimbradoHandler(db, new FakeClock(), NullLogger<ReciboPagoTimbradoHandler>.Instance);
        await reppHandler.Handle(new ReciboPagoTimbradoCommand(Guid.NewGuid(), new ReciboPagoTimbradoPayload(
            factura.EmpresaId, Ahora, reppId, "uuid-repp", 100m, 0m,
            [new ReppFacturaPagadaPayload(factura.FacturaVentaId, 100m, 1, "MXN", 0m)])),
            CancellationToken.None);
        (await db.FacturasCartera.SingleAsync()).Estado.Should().Be(EstadoFacturaCartera.Pagada);

        var handler = new ComprobanteCanceladoHandler(db, new FakeClock(), NullLogger<ComprobanteCanceladoHandler>.Instance);
        await handler.Handle(new ComprobanteCanceladoCommand(Guid.NewGuid(), new ComprobanteCanceladoPayload(
            factura.EmpresaId, Ahora, reppId, "Pago", "uuid-repp")),
            CancellationToken.None);

        var f = await db.FacturasCartera.SingleAsync();
        f.MontoPagado.Should().Be(0m);
        f.Estado.Should().Be(EstadoFacturaCartera.Abierta);
        (await db.MovimientosCartera.SingleAsync()).Revertido.Should().BeTrue();
    }

    // --------------------------------------------------- Cobro mostrador

    [Fact]
    public async Task CobroMostrador_aplica_y_su_cancelacion_revierte()
    {
        using var db = CrearDbContext();
        var factura = await ProyectarFactura(db, total: 1_000m);
        var cobroId = Guid.NewGuid();

        var registrado = new CobroMostradorRegistradoHandler(db, new FakeClock(), NullLogger<CobroMostradorRegistradoHandler>.Instance);
        await registrado.Handle(new CobroMostradorRegistradoCommand(Guid.NewGuid(), new CobroMostradorRegistradoPayload(
            factura.EmpresaId, Ahora, cobroId, Guid.NewGuid(), Guid.NewGuid(),
            factura.FacturaVentaId, "Ingreso", "Pedido", 1_000m)),
            CancellationToken.None);
        (await db.FacturasCartera.SingleAsync()).Estado.Should().Be(EstadoFacturaCartera.Pagada);

        var cancelado = new CobroMostradorCanceladoHandler(db, new FakeClock(), NullLogger<CobroMostradorCanceladoHandler>.Instance);
        await cancelado.Handle(new CobroMostradorCanceladoCommand(Guid.NewGuid(), new CobroMostradorCanceladoPayload(
            factura.EmpresaId, Ahora, cobroId, factura.FacturaVentaId, 1_000m, true)),
            CancellationToken.None);

        (await db.FacturasCartera.SingleAsync()).Estado.Should().Be(EstadoFacturaCartera.Abierta);
    }

    [Fact]
    public async Task CobroMostrador_de_comprobante_fuera_de_cartera_es_informativo()
    {
        using var db = CrearDbContext();

        var handler = new CobroMostradorRegistradoHandler(db, new FakeClock(), NullLogger<CobroMostradorRegistradoHandler>.Instance);
        await handler.Handle(new CobroMostradorRegistradoCommand(Guid.NewGuid(), new CobroMostradorRegistradoPayload(
            Guid.NewGuid(), Ahora, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "Ingreso", "Anticipo", 500m)),
            CancellationToken.None);

        (await db.FacturasCartera.CountAsync()).Should().Be(0);
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    // --------------------------------------------------- helpers

    private static FacturaVentaTimbradaPayload PayloadFactura(Guid facturaVentaId, decimal total = 100_000m) =>
        new(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: Ahora,
            FacturaVentaId: facturaVentaId,
            Uuid: Guid.NewGuid().ToString(),
            Total: total,
            Moneda: "MXN",
            PedidoFacturableId: null,
            ReceptorRfc: Rfc,
            ReceptorNombre: "Vidrios del Golfo",
            Folio: "FV-1001",
            MetodoPago: "PPD",
            FechaTimbrado: Ahora);

    private static async Task HandleFactura(
        CuentasPorCobrarDbContext db,
        Guid eventId,
        FacturaVentaTimbradaPayload payload,
        IClienteReadPort? clientePort = null)
    {
        var handler = new FacturaVentaTimbradaHandler(
            db,
            clientePort ?? new FakeClientePort(new ClienteRefDto(ClienteId, Rfc, "Vidrios del Golfo", false)),
            new FakeClock(),
            NullLogger<FacturaVentaTimbradaHandler>.Instance);
        await handler.Handle(new FacturaVentaTimbradaCommand(eventId, payload), CancellationToken.None);
    }

    private static async Task<FacturaCartera> ProyectarFactura(CuentasPorCobrarDbContext db, decimal total)
    {
        await HandleFactura(db, Guid.NewGuid(), PayloadFactura(Guid.NewGuid(), total));
        return await db.FacturasCartera.SingleAsync();
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_cartera_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakeClientePort(ClienteRefDto? cliente) : IClienteReadPort
    {
        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) =>
            Task.FromResult(cliente);
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult(cliente);
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

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
