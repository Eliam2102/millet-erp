using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>
/// GAP-5 (verificación e2e 2026-07-15): el dispatch de
/// <c>almacen.oc_devolucion.registrada.v1</c> calcula el acumulado
/// ajustado (recibido − devuelto) y publica el evento in-proc
/// <see cref="OcDevolucionRegistradaEvent"/> que consume el listener
/// F5-PR2 (hasta hoy huérfano). Foco: fan-out, marca de idempotencia y
/// casos borde (sin OC de origen, línea sin LineaOcId, re-entrega).
/// </summary>
public sealed class OcDevolucionEnAlmacenHandlerTests
{
    [Fact]
    public async Task Sin_orden_compra_origen_solo_marca_idempotencia()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);
        var eventoId = Guid.NewGuid();

        await handler.Handle(new OcDevolucionEnAlmacenCommand(eventoId, Payload(ordenCompraId: null,
            Linea(lineaOcId: Guid.NewGuid(), cantidad: 5m))), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
        var marca = await db.EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventoId);
        marca.EventoTipo.Should().Be("almacen.oc_devolucion.registrada.v1");
    }

    [Fact]
    public async Task Linea_sin_linea_oc_id_no_resoluble_falla_sin_publicar_ni_marcar()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);
        var articulo = Guid.NewGuid();
        var ocId = await SembrarOcAsync(db, articulo, articulo);

        var act = () => handler.Handle(new OcDevolucionEnAlmacenCommand(Guid.NewGuid(), Payload(
            ordenCompraId: ocId,
            Linea(lineaOcId: null, cantidad: 5m, articuloId: articulo))), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "DEVOLUCION_LINEA_OC_NO_RESOLUBLE");
        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Orden_compra_inexistente_falla_sin_publicar_ni_marcar()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);

        var act = () => handler.Handle(new OcDevolucionEnAlmacenCommand(Guid.NewGuid(), Payload(
            ordenCompraId: Guid.NewGuid(),
            Linea(lineaOcId: Guid.NewGuid(), cantidad: 5m))), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>()
            .Where(e => e.Code == "ORDEN_COMPRA_NO_ENCONTRADA");
        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Idempotente_no_reprocesa_mismo_evento()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);
        var eventoId = Guid.NewGuid();

        db.EventosProcesados.Add(new Millet.Compras.Domain.Idempotencia.EventoProcesado(
            eventoId, "almacen.oc_devolucion.registrada.v1", "ya estaba"));
        await db.SaveChangesAsync();

        await handler.Handle(new OcDevolucionEnAlmacenCommand(eventoId, Payload(
            ordenCompraId: Guid.NewGuid(),
            Linea(lineaOcId: Guid.NewGuid(), cantidad: 5m))), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Devolucion_que_deja_recibido_bajo_facturado_se_omite_y_marca_procesado()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);
        var articulo = Guid.NewGuid();
        var ocId = await SembrarOcAsync(db, articulo);
        var linea = db.OrdenesCompra.Local.Single(o => o.Id == ocId).Lineas.Single();
        // Recibido 110 de 100 y facturado 110; devolver 10 violaría cantidad_facturada <= max(cantidad, recibida).
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadRecibida))!.SetValue(linea, 110m);
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadFacturada))!.SetValue(linea, 110m);
        await db.SaveChangesAsync();

        await handler.Handle(new OcDevolucionEnAlmacenCommand(Guid.NewGuid(), Payload(
            ordenCompraId: ocId,
            Linea(lineaOcId: linea.Id, cantidad: 10m, articuloId: articulo))), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Devolucion_de_lo_facturado_dentro_de_lo_pedido_publica_descuento()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);
        var articulo = Guid.NewGuid();
        var ocId = await SembrarOcAsync(db, articulo);
        var linea = db.OrdenesCompra.Local.Single(o => o.Id == ocId).Lineas.Single();
        // Pedido 100, recibido 100, facturado 100; devolver 10 deja 90 recibidas y 100 facturadas <= 100 pedidas.
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadRecibida))!.SetValue(linea, 100m);
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadFacturada))!.SetValue(linea, 100m);
        await db.SaveChangesAsync();

        await handler.Handle(new OcDevolucionEnAlmacenCommand(Guid.NewGuid(), Payload(
            ordenCompraId: ocId,
            Linea(lineaOcId: linea.Id, cantidad: 10m, articuloId: articulo))), CancellationToken.None);

        publisher.Published.Should().ContainSingle();
    }

    private static async Task<Guid> SembrarOcAsync(ComprasDbContext db, params Guid[] articuloIds)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse("OC-MID2026-000001"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test", usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
        foreach (var articuloId in articuloIds)
        {
            oc.AgregarLineaManual(Guid.CreateVersion7(), articuloId, 100, "PZA", 25, Guid.CreateVersion7());
        }
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();
        return oc.Id;
    }

    private static LineaDevolucionAlmacenPayload Linea(Guid? lineaOcId, decimal cantidad, Guid? articuloId = null) =>
        new(
            LineaDevolucionId: Guid.NewGuid(),
            LineaRecepcionOrigenId: Guid.NewGuid(),
            LineaOcId: lineaOcId,
            ArticuloId: articuloId ?? Guid.NewGuid(),
            UnidadMedida: "PZA",
            Cantidad: cantidad,
            CostoUnitarioMxn: 25m,
            MontoTotalMxn: cantidad * 25m);

    private static OcDevolucionRegistradaAlmacenPayload Payload(
        Guid? ordenCompraId, params LineaDevolucionAlmacenPayload[] lineas) =>
        new(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: DateTimeOffset.UtcNow,
            DevolucionId: Guid.NewGuid(),
            FolioMovimiento: "M-DEV2026-000099",
            ProveedorId: Guid.NewGuid(),
            RecepcionOrigenId: Guid.NewGuid(),
            FacturaProveedorOrigenId: null,
            OrdenCompraOrigenId: ordenCompraId,
            Motivo: "Test",
            MontoTotalMxn: lineas.Sum(l => l.MontoTotalMxn),
            Lineas: lineas);

    private static async Task<ComprasDbContext> CreateDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-devolucion-listener-tests-{Guid.NewGuid():N}")
            .Options;
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

/// <summary>
/// Tests del contrato espejo <see cref="OcDevolucionRegistradaAlmacenPayload"/>
/// que Compras deserializa al recibir <c>almacen.oc_devolucion.registrada.v1</c>.
/// Blindan la presencia opcional de <c>CantidadDocumento</c> por línea (aditivo
/// 86e3pc50v): un mensaje con el campo y otro sin él deben deserializar.
/// </summary>
public sealed class OcDevolucionRegistradaAlmacenPayloadTests
{
    // Mismas opciones que el worker productivo y que OcRecepcionRegistradaAlmacenPayloadTests.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Deserializa_linea_con_cantidadDocumento()
    {
        var body = """
        {
          "empresaId": "00000000-0000-0000-0000-000000000001",
          "ocurridoEn": "2026-05-24T20:00:00Z",
          "devolucionId": "00000000-0000-0000-0000-000000000070",
          "folioMovimiento": "M-DEV2026-000070",
          "proveedorId": "00000000-0000-0000-0000-000000000080",
          "recepcionOrigenId": "00000000-0000-0000-0000-000000000010",
          "facturaProveedorOrigenId": null,
          "ordenCompraOrigenId": "00000000-0000-0000-0000-000000000020",
          "motivo": "Devolución a proveedor",
          "montoTotalMxn": 50,
          "lineas": [
            {
              "lineaDevolucionId": "00000000-0000-0000-0000-000000000071",
              "lineaRecepcionOrigenId": "00000000-0000-0000-0000-000000000041",
              "lineaOcId": "00000000-0000-0000-0000-000000000051",
              "articuloId": "00000000-0000-0000-0000-000000000061",
              "unidadMedida": "PZA",
              "cantidad": 2,
              "costoUnitarioMxn": 25,
              "montoTotalMxn": 50,
              "cantidadDocumento": 2
            }
          ]
        }
        """;

        var p = JsonSerializer.Deserialize<OcDevolucionRegistradaAlmacenPayload>(body, JsonOpts);

        p.Should().NotBeNull();
        p!.DevolucionId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000070"));
        p.OrdenCompraOrigenId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000020"));
        p.Lineas.Should().HaveCount(1);
        p.Lineas[0].LineaOcId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000051"));
        p.Lineas[0].Cantidad.Should().Be(2m);
        p.Lineas[0].CantidadDocumento.Should().Be(2m);
    }

    [Fact]
    public void Deserializa_linea_sin_cantidadDocumento_queda_null()
    {
        var body = """
        {
          "empresaId": "00000000-0000-0000-0000-000000000001",
          "ocurridoEn": "2026-05-24T20:00:00Z",
          "devolucionId": "00000000-0000-0000-0000-000000000070",
          "folioMovimiento": "M-DEV2026-000070",
          "proveedorId": "00000000-0000-0000-0000-000000000080",
          "recepcionOrigenId": "00000000-0000-0000-0000-000000000010",
          "facturaProveedorOrigenId": null,
          "ordenCompraOrigenId": "00000000-0000-0000-0000-000000000020",
          "motivo": "Devolución a proveedor",
          "montoTotalMxn": 50,
          "lineas": [
            {
              "lineaDevolucionId": "00000000-0000-0000-0000-000000000071",
              "lineaRecepcionOrigenId": "00000000-0000-0000-0000-000000000041",
              "lineaOcId": "00000000-0000-0000-0000-000000000051",
              "articuloId": "00000000-0000-0000-0000-000000000061",
              "unidadMedida": "PZA",
              "cantidad": 2,
              "costoUnitarioMxn": 25,
              "montoTotalMxn": 50
            }
          ]
        }
        """;

        var p = JsonSerializer.Deserialize<OcDevolucionRegistradaAlmacenPayload>(body, JsonOpts);

        p.Should().NotBeNull();
        p!.Lineas[0].CantidadDocumento.Should().BeNull();
    }
}
