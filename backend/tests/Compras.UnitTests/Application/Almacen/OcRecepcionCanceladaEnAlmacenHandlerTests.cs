using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>86e3tabdp: la cancelación de una recepción devuelve a pendiente lo recibido de cada línea de OC.</summary>
public sealed class OcRecepcionCanceladaEnAlmacenHandlerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static OcRecepcionCanceladaEnAlmacenHandler Handler(ComprasDbContext db, CapturingPublisher publisher) =>
        new(db, publisher, NullLogger<OcRecepcionCanceladaEnAlmacenHandler>.Instance);

    [Fact]
    public async Task Descuenta_lo_cancelado_de_cada_linea_y_marca_el_evento()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var (ocId, lineaId) = await SembrarOcAsync(db, recibido: 10m);
        var eventoId = Guid.NewGuid();

        // Principal 8 + material en revisión 2 sobre la misma línea de OC.
        await Handler(db, publisher).Handle(new OcRecepcionCanceladaEnAlmacenCommand(eventoId, Payload(ocId, Linea(lineaId, 8m), Linea(lineaId, 2m))), default);

        publisher.Published.OfType<OcDevolucionRegistradaEvent>().Select(e => e.CantidadAcumuladaAjustada).Should().Equal(2m, 0m);
        (await db.EventosProcesados.SingleAsync()).EventoTipo.Should().Be("almacen.recepcion_oc.cancelada.v1");
    }

    [Fact]
    public async Task No_baja_de_cero_aunque_Almacen_mande_mas_de_lo_recibido()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var (ocId, lineaId) = await SembrarOcAsync(db, recibido: 3m);

        await Handler(db, publisher).Handle(new OcRecepcionCanceladaEnAlmacenCommand(Guid.NewGuid(), Payload(ocId, Linea(lineaId, 5m))), default);

        publisher.Published.OfType<OcDevolucionRegistradaEvent>().Single().CantidadAcumuladaAjustada.Should().Be(0m);
    }

    [Fact]
    public async Task No_omite_la_linea_aunque_quede_bajo_lo_facturado()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var (ocId, lineaId) = await SembrarOcAsync(db, recibido: 10m);
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadFacturada))!
            .SetValue(db.OrdenesCompra.Local.Single().Lineas.Single(), 10m);
        await db.SaveChangesAsync();

        await Handler(db, publisher).Handle(new OcRecepcionCanceladaEnAlmacenCommand(Guid.NewGuid(), Payload(ocId, Linea(lineaId, 10m))), default);

        publisher.Published.OfType<OcDevolucionRegistradaEvent>().Single().CantidadAcumuladaAjustada.Should().Be(0m);
    }

    [Fact]
    public async Task Linea_sin_linea_de_oc_no_descuenta_pero_marca()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var (ocId, _) = await SembrarOcAsync(db, recibido: 10m);

        await Handler(db, publisher).Handle(new OcRecepcionCanceladaEnAlmacenCommand(Guid.NewGuid(), Payload(ocId, Linea(null, 5m))), default);

        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Idempotente_no_reprocesa_mismo_evento()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var (ocId, lineaId) = await SembrarOcAsync(db, recibido: 10m);
        var eventoId = Guid.NewGuid();
        var cmd = new OcRecepcionCanceladaEnAlmacenCommand(eventoId, Payload(ocId, Linea(lineaId, 4m)));

        await Handler(db, publisher).Handle(cmd, default);
        publisher.Published.Clear();
        await Handler(db, publisher).Handle(cmd, default);

        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Oc_inexistente_o_linea_ajena_falla_sin_marcar()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var (ocId, _) = await SembrarOcAsync(db, recibido: 10m);

        await FluentActions.Awaiting(() => Handler(db, publisher).Handle(
                new OcRecepcionCanceladaEnAlmacenCommand(Guid.NewGuid(), Payload(Guid.NewGuid(), Linea(Guid.NewGuid(), 1m))), default))
            .Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "ORDEN_COMPRA_NO_ENCONTRADA");
        await FluentActions.Awaiting(() => Handler(db, publisher).Handle(
                new OcRecepcionCanceladaEnAlmacenCommand(Guid.NewGuid(), Payload(ocId, Linea(Guid.NewGuid(), 1m))), default))
            .Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "RECEPCION_CANCELADA_LINEA_OC_NO_RESOLUBLE");

        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void Deserializa_el_payload_del_evento_de_Almacen()
    {
        var body = """
        {
          "empresaId": "00000000-0000-0000-0000-000000000001",
          "ocurridoEn": "2026-10-23T20:00:00Z",
          "recepcionId": "00000000-0000-0000-0000-000000000010",
          "folioRecepcion": "M-ENT2026-000010",
          "ordenCompraId": "00000000-0000-0000-0000-000000000020",
          "fechaMovimiento": "2026-10-17",
          "motivoCodigo": "ARTICULO",
          "motivoTexto": "Era otro",
          "canceladaPor": "00000000-0000-0000-0000-000000000002",
          "foliosCancelacion": ["M-CEN2026-000001"],
          "lineas": [{
            "lineaRecepcionId": "00000000-0000-0000-0000-000000000041",
            "lineaOcId": "00000000-0000-0000-0000-000000000051",
            "articuloId": "00000000-0000-0000-0000-000000000061",
            "unidadMedida": "PZA", "cantidad": 24, "cantidadDocumento": 2,
            "costoUnitarioMxn": 25, "montoTotalMxn": 600, "ubicacionId": "00000000-0000-0000-0000-000000000071"
          }]
        }
        """;

        var p = JsonSerializer.Deserialize<OcRecepcionCanceladaAlmacenPayload>(body, JsonOpts);

        p!.RecepcionId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000010"));
        p.Lineas.Single().CantidadDocumento.Should().Be(2m);
        p.Lineas.Single().LineaOcId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000051"));
    }

    private static async Task<(Guid OcId, Guid LineaId)> SembrarOcAsync(ComprasDbContext db, decimal recibido)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(), empresaId: Guid.CreateVersion7(), folio: Folio.Parse("OC-MID2026-000001"), folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(), sucursalDestinoId: Guid.CreateVersion7(), condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(), compradorTitularId: Guid.CreateVersion7(), encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime), sinRequisicionPrevia: true, motivoSinRequisicion: "Test");
        oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 100, "PZA", 25, Guid.CreateVersion7());
        var linea = oc.Lineas.Single();
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadRecibida))!.SetValue(linea, recibido);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();
        return (oc.Id, linea.Id);
    }

    private static LineaRecepcionCanceladaAlmacenPayload Linea(Guid? lineaOcId, decimal cantidadDocumento) =>
        new(Guid.NewGuid(), lineaOcId, Guid.NewGuid(), cantidadDocumento, cantidadDocumento);

    private static OcRecepcionCanceladaAlmacenPayload Payload(Guid ordenCompraId, params LineaRecepcionCanceladaAlmacenPayload[] lineas) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "M-ENT2026-000010", ordenCompraId, lineas);

    private static async Task<ComprasDbContext> CreateDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-recepcion-cancelada-tests-{Guid.NewGuid():N}").Options;
        var db = new ComprasDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class CapturingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        { Published.Add(notification); return Task.CompletedTask; }
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        { Published.Add(notification!); return Task.CompletedTask; }
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
