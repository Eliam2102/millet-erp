using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Ingesta.ImportarPedidoPlantaPintura;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Ingesta;

public sealed class PlantaPinturaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static ClienteFiscalLectura Cliente() =>
        new(Guid.NewGuid(), "AAA010101AAA", "Cliente PP", "601", "97000", "G03", "01", "PUE", "MXN", false);

    private static ProductoFiscalLectura Producto() =>
        new(Guid.NewGuid(), "Pintura", "01010101", "H87", "02", 0.16m, null, null, "PlantaPintura");

    private static PedidoPlantaPintura Pedido(long version = 1) => new(
        "PP-1", Guid.NewGuid(), "CLI-1", "Cliente PP", CanalesVentaConocidos.PlantaPintura,
        ComportamientoFiscal.MostradorInmediato, "MXN", null, null, "comentario", version, "ABIERTO",
        [new LineaPedidoPlantaPintura("PRD-1", "Pintura", "01010101", "H87", 2m, 100m, 0m, false)],
        "{\"pp\":\"PP-1\"}");

    private static ImportarPedidoPlantaPinturaHandler Handler(
        FacturacionDbContext db, ClienteFiscalLectura? cliente, ProductoFiscalLectura? producto) =>
        new(db, new FakeClientesReadPort(cliente), new FakeProductosReadPort(producto), new FakeClock(Ahora));

    [Fact]
    public void ImportarDesdePlantaPintura_tiene_origen_PlantaPintura()
    {
        var p = PedidoFacturable.ImportarDesdePlantaPintura(Guid.NewGuid(), "PP-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", CanalesVentaConocidos.PlantaPintura, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "ABIERTO");
        p.Origen.Should().Be(OrigenPedido.PlantaPintura);
        p.Estado.Should().Be(EstadoPedidoFacturable.Importado);
    }

    [Fact]
    public async Task Importar_con_master_existente_crea_el_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, Cliente(), Producto())
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        var pedido = await db.PedidosFacturables.SingleAsync();
        pedido.Origen.Should().Be(OrigenPedido.PlantaPintura);
        await db.IngestaControles.SingleAsync();
        await db.PedidosFacturablesSnapshot.SingleAsync();
    }

    [Fact]
    public async Task Importar_sin_cliente_genera_excepcion_sin_autoprovision()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, cliente: null, producto: Producto())
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        resp.MotivoExcepcion.Should().Be(MotivoExcepcion.ClienteNoExiste);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.ClienteNoExiste);
        (await db.PedidosFacturables.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Importar_sin_articulo_genera_excepcion()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, cliente: Cliente(), producto: null)
            .Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido()), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        resp.MotivoExcepcion.Should().Be(MotivoExcepcion.ArticuloNoExiste);
    }

    [Fact]
    public async Task Importar_version_repetida_es_idempotente()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await Handler(db, Cliente(), Producto()).Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido(version: 1)), CancellationToken.None);

        var resp = await Handler(db, Cliente(), Producto()).Handle(new ImportarPedidoPlantaPinturaCommand(empresaId, Pedido(version: 1)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Pospuesta);
        (await db.PedidosFacturables.CountAsync()).Should().Be(1);
    }
}
