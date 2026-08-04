using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Application.Repp;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Repp;

/// <summary>
/// Tests de TES-PR8 (§3.6.b): registro del REPP recibido (UUID único,
/// XML a blob, evento congelado hacia CxP) y read model de pagos PPD sin
/// complemento con SLA de 5 días.
/// </summary>
public sealed class ReppRecibidoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();

    // ------------------------------------------------------------ registrar

    [Fact]
    public async Task Registrar_guarda_xml_y_publica_evento_congelado()
    {
        using var db = CrearDbContext();
        var facturaId = await SembrarPasivoAsync(db, metodoPago: "PPD");
        var blob = new FakeBlob();
        var publisher = new FakePublisher();
        var handler = Handler(db, blob, publisher);

        var uuid = Guid.NewGuid();
        var xml = Convert.ToBase64String("<pago20:Pagos/>"u8.ToArray());
        var response = await handler.Handle(new RegistrarReppRecibidoCommand(
            facturaId, uuid, new DateOnly(2026, 7, 14), xml), CancellationToken.None);

        response.XmlBlobRef.Should().NotBeNull();
        blob.Guardados.Should().ContainSingle();

        var evento = publisher.Publicados.OfType<ReppProveedorRecibidoIntegrationEvent>().Single();
        evento.FacturaProveedorId.Should().Be(facturaId);
        evento.UuidComplementoPago.Should().Be(uuid.ToString("D").ToUpperInvariant());
        evento.FechaComplemento.Should().Be(new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero));

        (await db.ReppsProveedorRecibidos.SingleAsync()).UuidComplemento.Should().Be(uuid);
    }

    [Fact]
    public async Task Registrar_sin_xml_es_valido_y_uuid_duplicado_rechaza()
    {
        using var db = CrearDbContext();
        var facturaId = await SembrarPasivoAsync(db, metodoPago: "PPD");
        var handler = Handler(db, new FakeBlob(), new FakePublisher());
        var uuid = Guid.NewGuid();

        var response = await handler.Handle(new RegistrarReppRecibidoCommand(
            facturaId, uuid, new DateOnly(2026, 7, 14)), CancellationToken.None);
        response.XmlBlobRef.Should().BeNull();

        var act = () => handler.Handle(new RegistrarReppRecibidoCommand(
            facturaId, uuid, new DateOnly(2026, 7, 14)), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "REPP_UUID_DUPLICADO");
    }

    [Fact]
    public async Task Registrar_pasivo_inexistente_rechaza()
    {
        using var db = CrearDbContext();
        var handler = Handler(db, new FakeBlob(), new FakePublisher());

        var act = () => handler.Handle(new RegistrarReppRecibidoCommand(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 14)), CancellationToken.None);
        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    // ------------------------------------------------------------ pendientes

    [Fact]
    public async Task Pendientes_lista_pago_ppd_sin_repp_y_excluye_pue_y_cubiertos()
    {
        using var db = CrearDbContext();

        var facturaPpd = await SembrarPasivoConPagoAsync(db, "PPD", new DateOnly(2026, 7, 1));
        var facturaPue = await SembrarPasivoConPagoAsync(db, "PUE", new DateOnly(2026, 7, 1));
        var facturaSinDato = await SembrarPasivoConPagoAsync(db, null, new DateOnly(2026, 7, 13));
        var facturaCubierta = await SembrarPasivoConPagoAsync(db, "PPD", new DateOnly(2026, 7, 1));

        var registrar = Handler(db, new FakeBlob(), new FakePublisher());
        await registrar.Handle(new RegistrarReppRecibidoCommand(
            facturaCubierta, Guid.NewGuid(), new DateOnly(2026, 7, 10)), CancellationToken.None);

        var handler = new ReppPendientesHandler(db, new FakeProveedores(), new FakeClock(Ahora));
        var page = await handler.Handle(new ReppPendientesQuery(), CancellationToken.None);

        page.Items.Select(i => i.FacturaProveedorId)
            .Should().BeEquivalentTo([facturaPpd, facturaSinDato]);

        var ppd = page.Items.Single(i => i.FacturaProveedorId == facturaPpd);
        ppd.DiasSinRepp.Should().Be(14);
        ppd.VencidoSla.Should().BeTrue(); // SLA 5 días

        var sinDato = page.Items.Single(i => i.FacturaProveedorId == facturaSinDato);
        sinDato.MetodoPago.Should().BeNull();
        sinDato.VencidoSla.Should().BeFalse();

        // PUE explícito nunca entra; sin-dato puede excluirse por filtro.
        var soloPpd = await handler.Handle(
            new ReppPendientesQuery(IncluirSinMetodo: false), CancellationToken.None);
        soloPpd.Items.Select(i => i.FacturaProveedorId).Should().BeEquivalentTo([facturaPpd]);

        var vencidos = await handler.Handle(
            new ReppPendientesQuery(SoloVencidos: true), CancellationToken.None);
        vencidos.Items.Select(i => i.FacturaProveedorId).Should().BeEquivalentTo([facturaPpd]);

        page.Items.Should().NotContain(i => i.FacturaProveedorId == facturaPue);
    }

    // ------------------------------------------------------------ helpers

    private static RegistrarReppRecibidoHandler Handler(
        TesoreriaDbContext db, FakeBlob blob, FakePublisher publisher) =>
        new(db, new FakeEmpresaContext(), new FakeUserContext(), blob, publisher, new FakeClock(Ahora));

    private static async Task<Guid> SembrarPasivoAsync(TesoreriaDbContext db, string? metodoPago)
    {
        var facturaId = Guid.NewGuid();
        db.PasivosPendientesPago.Add(new Domain.Pasivos.PasivoPendientePago(
            EmpresaId, facturaId, Guid.NewGuid(), null, 1_000m, 0m, "MXN", null,
            new DateOnly(2026, 8, 1), null, "F-001", Ahora, metodoPago));
        await db.SaveChangesAsync();
        return facturaId;
    }

    private static async Task<Guid> SembrarPasivoConPagoAsync(
        TesoreriaDbContext db, string? metodoPago, DateOnly fechaPago)
    {
        var facturaId = await SembrarPasivoAsync(db, metodoPago);

        var cuenta = new Domain.Cuentas.CuentaBancaria(EmpresaId, "BBVA", "0123456789", null, "MXN");
        var proveedorId = db.PasivosPendientesPago.Local.Single(p => p.FacturaProveedorId == facturaId).ProveedorId;
        var movimiento = Domain.Movimientos.MovimientoBancario.RegistrarPagoProveedor(
            EmpresaId, cuenta, proveedorId, 1_000m, fechaPago, "SPEI-1", null, UsuarioId, Ahora);
        db.MovimientosBancarios.Add(movimiento);
        db.AplicacionesPagoProveedor.Add(new Domain.Movimientos.AplicacionPagoProveedor(
            movimiento.Id, facturaId, proveedorId, 1_000m, Ahora));
        await db.SaveChangesAsync();
        return facturaId;
    }

    private static TesoreriaDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase(databaseName: $"tesoreria_test_{Guid.NewGuid()}")
            .Options;
        return new TesoreriaDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakeBlob : IReppXmlBlobStorage
    {
        public List<string> Guardados { get; } = [];

        public Task<string> GuardarXmlAsync(
            string uuid, DateOnly fechaComplemento, Stream contenido, CancellationToken cancellationToken)
        {
            var blobRef = $"tesoreria/{fechaComplemento:yyyy}/{fechaComplemento:MM}/repp/{uuid.ToUpperInvariant()}.xml";
            Guardados.Add(blobRef);
            return Task.FromResult(blobRef);
        }

        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];

        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProveedores : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken) =>
            Task.FromResult<ProveedorBancoDto?>(null);

        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(
            IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(
                proveedorIds.ToDictionary(
                    id => id,
                    id => new ProveedorBancoDto(id, "P000001", "Proveedor Uno SA", null, null, null)));
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeUserContext : ICurrentUserContext
    {
        public Guid? UserId => UsuarioId;
        public string? UserName => "test";
    }

    private sealed class FakeClock(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }
}
