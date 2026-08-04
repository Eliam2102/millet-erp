using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Pasivos;

/// <summary>
/// Tests de la proyección de pasivos autorizados (TES-PR3): upsert por
/// FacturaProveedorId + marca de idempotencia en la misma operación.
/// </summary>
public sealed class ProyectarPasivoAutorizadoHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static PasivoAutorizadoParaPagoPayload Payload(
        Guid? facturaId = null,
        decimal montoTotal = 10_000m,
        decimal saldoPendiente = 10_000m,
        string? uuidCfdi = "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        DateOnly? vencimiento = null,
        string? metodoPago = null) =>
        new(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: Ahora,
            FacturaProveedorId: facturaId ?? Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            MontoTotal: montoTotal,
            SaldoPendiente: saldoPendiente,
            Moneda: "MXN",
            TipoCambio: null,
            FechaVencimiento: vencimiento ?? new DateOnly(2026, 8, 1),
            UuidCfdi: uuidCfdi,
            FolioProveedor: "F-001",
            MetodoPago: metodoPago);

    [Fact]
    public async Task Proyecta_pasivo_nuevo_y_marca_idempotencia()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));
        var payload = Payload();
        var eventoId = Guid.NewGuid();

        await handler.Handle(new ProyectarPasivoAutorizadoCommand(eventoId, payload), CancellationToken.None);

        var pasivo = await db.PasivosPendientesPago.SingleAsync();
        pasivo.FacturaProveedorId.Should().Be(payload.FacturaProveedorId);
        pasivo.SaldoPendiente.Should().Be(10_000m);
        pasivo.UuidCfdi.Should().Be(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"));
        pasivo.RecibidoEn.Should().Be(Ahora);

        var marca = await db.EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventoId);
        marca.EventoTipo.Should().Be(PasivoAutorizadoParaPagoPayload.EventType);
    }

    [Fact]
    public async Task Reemision_actualiza_sin_duplicar()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));
        var facturaId = Guid.NewGuid();

        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(), Payload(facturaId, saldoPendiente: 10_000m)),
            CancellationToken.None);

        // Re-autorización tras reversa: el evento trae el saldo vigente.
        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(),
                Payload(facturaId, saldoPendiente: 4_000m, vencimiento: new DateOnly(2026, 9, 1))),
            CancellationToken.None);

        var pasivo = await db.PasivosPendientesPago.SingleAsync();
        pasivo.SaldoPendiente.Should().Be(4_000m);
        pasivo.FechaVencimiento.Should().Be(new DateOnly(2026, 9, 1));

        (await db.EventosProcesados.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task MetodoPago_se_proyecta_y_reemision_sin_dato_no_lo_borra()
    {
        // TES-PR8 [T-G11 opción (a)]: el evento enriquecido trae PUE/PPD.
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));
        var facturaId = Guid.NewGuid();

        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(), Payload(facturaId, metodoPago: "PPD")),
            CancellationToken.None);
        (await db.PasivosPendientesPago.SingleAsync()).MetodoPago.Should().Be("PPD");

        // Re-emisión sin el dato (p. ej. publisher viejo): no borra el conocido.
        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(), Payload(facturaId, metodoPago: null)),
            CancellationToken.None);
        (await db.PasivosPendientesPago.SingleAsync()).MetodoPago.Should().Be("PPD");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-uuid")]
    public async Task UuidCfdi_no_parseable_se_proyecta_null(string? uuidCfdi)
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));

        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(), Payload(uuidCfdi: uuidCfdi)),
            CancellationToken.None);

        (await db.PasivosPendientesPago.SingleAsync()).UuidCfdi.Should().BeNull();
    }

    // ------------------------------------------ GI-PR2: pasivos internos

    private static PasivoAutorizadoParaPagoPayload PayloadInterno(
        Guid? origenId = null,
        string tipoBeneficiario = "CajaSucursal",
        string origenTipo = "ReposicionCajaChica",
        decimal monto = 1_102m) =>
        new(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: Ahora,
            FacturaProveedorId: Guid.Empty,
            ProveedorId: Guid.Empty,
            OrdenCompraId: null,
            MontoTotal: monto,
            SaldoPendiente: monto,
            Moneda: "MXN",
            TipoCambio: null,
            FechaVencimiento: new DateOnly(2026, 7, 17),
            UuidCfdi: null,
            FolioProveedor: null,
            MetodoPago: null,
            TipoBeneficiario: tipoBeneficiario,
            BeneficiarioId: Guid.NewGuid(),
            OrigenTipo: origenTipo,
            OrigenId: origenId ?? Guid.NewGuid());

    [Fact]
    public async Task Proyecta_pasivo_interno_con_beneficiario_y_origen()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));
        var payload = PayloadInterno();

        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(), payload), CancellationToken.None);

        var pasivo = await db.PasivosPendientesPago.SingleAsync();
        pasivo.EsInterno.Should().BeTrue();
        pasivo.TipoBeneficiario.Should().Be("CajaSucursal");
        pasivo.BeneficiarioId.Should().Be(payload.BeneficiarioId);
        pasivo.OrigenTipo.Should().Be("ReposicionCajaChica");
        pasivo.OrigenId.Should().Be(payload.OrigenId!.Value);
        // La clave física reutiliza el origen (índice único intacto).
        pasivo.FacturaProveedorId.Should().Be(payload.OrigenId!.Value);
        pasivo.SaldoPendiente.Should().Be(1_102m);

        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reemision_de_interno_actualiza_sin_duplicar()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));
        var origenId = Guid.NewGuid();

        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(
                Guid.NewGuid(), PayloadInterno(origenId, monto: 1_000m)),
            CancellationToken.None);
        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(
                Guid.NewGuid(), PayloadInterno(origenId, monto: 1_500m)),
            CancellationToken.None);

        var pasivo = await db.PasivosPendientesPago.SingleAsync();
        pasivo.SaldoPendiente.Should().Be(1_500m);
    }

    [Fact]
    public async Task Interno_sin_beneficiario_se_ignora_pero_marca_idempotencia()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));
        var payload = PayloadInterno() with { BeneficiarioId = null };

        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(), payload), CancellationToken.None);

        (await db.PasivosPendientesPago.CountAsync()).Should().Be(0);
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Pasivo_de_factura_queda_con_beneficiario_proveedor()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPasivoAutorizadoHandler(db, new FakeClock(Ahora));
        var payload = Payload();

        await handler.Handle(
            new ProyectarPasivoAutorizadoCommand(Guid.NewGuid(), payload), CancellationToken.None);

        var pasivo = await db.PasivosPendientesPago.SingleAsync();
        pasivo.EsInterno.Should().BeFalse();
        pasivo.TipoBeneficiario.Should().Be("Proveedor");
        pasivo.BeneficiarioId.Should().Be(payload.ProveedorId);
        pasivo.OrigenTipo.Should().Be("Factura");
        pasivo.OrigenId.Should().Be(payload.FacturaProveedorId);
    }

    // ------------------------------------------------------------ fakes

    private static TesoreriaDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase(databaseName: $"tesoreria_test_{Guid.NewGuid()}")
            .Options;
        return new TesoreriaDbContext(options, new FakeEmpresaContext());
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

    private sealed class FakeClock(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }
}
