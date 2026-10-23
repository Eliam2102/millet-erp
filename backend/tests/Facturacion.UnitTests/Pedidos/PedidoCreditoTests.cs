using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Ingesta.ProcesarSolicitudAw;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// 86e3tfdw2 — pedidos a crédito: la ingesta A+W conserva el método y la
/// condición de pago del origen y el dominio marca <c>EsCredito</c> cuando la
/// condición no es CONTADO (o, sin condición, cuando el método es PPD).
/// </summary>
public sealed class PedidoCreditoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static DatosPedidoAw Datos(string? metodo, string? condicion) => new(
        "AW-1", Guid.NewGuid(), "CLI-1", "Cliente A+W", (short)1,
        ComportamientoFiscal.ConAnticipo, "MXN", null, null, null, "15",
        [new LineaPedidoAw("PRD-1", "Producto A+W", "01010101", "H87", 2m, 100m, 0m, false)],
        "{\"pedido\":\"AW-1\"}",
        MetodoPagoOrigen: metodo,
        CondicionPagoOrigen: condicion);

    private static ClienteFiscalLectura Cliente() =>
        new(Guid.NewGuid(), "AAA010101AAA", "Cliente A+W", "601", "97000", "G03", "01", "PUE", "MXN", false);

    private static ProcesarSolicitudAwHandler Handler(FacturacionDbContext db, DatosPedidoAw datos) =>
        new(db, new FakeAwSolicitudesReader(datos), new FakeAwWriteBackPort(), new FakeClientesReadPort(Cliente()),
            new FakeProductosReadPort(), new FakeMasterProvisioningPort(null),
            new FakeClock(Ahora), Options.Create(new AwSolicitudesOptions()));

    private static async Task<PedidoFacturable> IngestarAsync(string? metodo, string? condicion)
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await Handler(db, Datos(metodo, condicion)).Handle(
            new ProcesarSolicitudAwCommand(empresaId, new SolicitudAw(Guid.NewGuid(), "AW-1", OperacionAw.Alta, 1, Ahora)),
            CancellationToken.None);
        return await db.PedidosFacturables.SingleAsync();
    }

    [Fact]
    public async Task Ingesta_con_condicion_credito_marca_EsCredito_y_guarda_los_datos_de_origen()
    {
        var pedido = await IngestarAsync("PPD", "CRÉDITO 30 DÍAS");

        pedido.EsCredito.Should().BeTrue();
        pedido.MetodoPagoOrigen.Should().Be("PPD");
        pedido.CondicionPagoOrigen.Should().Be("CRÉDITO 30 DÍAS");
    }

    [Theory]
    [InlineData("CONTADO")]
    [InlineData(" contado ")]
    public async Task Ingesta_con_condicion_contado_no_es_credito(string condicion)
    {
        var pedido = await IngestarAsync("PUE", condicion);

        pedido.EsCredito.Should().BeFalse();
        pedido.MetodoPagoOrigen.Should().Be("PUE");
    }

    [Fact]
    public async Task Ingesta_sin_condicion_y_metodo_PPD_es_credito()
    {
        var pedido = await IngestarAsync("PPD", null);

        pedido.EsCredito.Should().BeTrue();
        pedido.CondicionPagoOrigen.Should().BeNull();
    }

    [Fact]
    public async Task Ingesta_sin_condicion_ni_metodo_no_es_credito()
    {
        var pedido = await IngestarAsync(null, "  ");

        pedido.EsCredito.Should().BeFalse();
        pedido.CondicionPagoOrigen.Should().BeNull();
    }

    [Fact]
    public async Task Modificacion_A_W_recalcula_EsCredito()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await Handler(db, Datos("PUE", "CONTADO")).Handle(
            new ProcesarSolicitudAwCommand(empresaId, new SolicitudAw(Guid.NewGuid(), "AW-1", OperacionAw.Alta, 1, Ahora)),
            CancellationToken.None);
        (await db.PedidosFacturables.SingleAsync()).EsCredito.Should().BeFalse();

        await Handler(db, Datos("PPD", "CRÉDITO 15 DÍAS")).Handle(
            new ProcesarSolicitudAwCommand(empresaId, new SolicitudAw(Guid.NewGuid(), "AW-1", OperacionAw.Modificacion, 2, Ahora)),
            CancellationToken.None);

        var pedido = await db.PedidosFacturables.SingleAsync();
        pedido.EsCredito.Should().BeTrue();
        pedido.CondicionPagoOrigen.Should().Be("CRÉDITO 15 DÍAS");
    }

    [Fact]
    public void Pedido_manual_no_es_credito()
    {
        var pedido = PedidoFacturable.CrearManual(
            Guid.NewGuid(), "MAN-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, Guid.NewGuid(),
            ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio, null, null);

        pedido.EsCredito.Should().BeFalse();
        pedido.MetodoPagoOrigen.Should().BeNull();
        pedido.CondicionPagoOrigen.Should().BeNull();
    }

    [Fact]
    public void Condicion_de_origen_mas_larga_que_la_columna_se_recorta()
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(
            Guid.NewGuid(), "AW-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.ConAnticipo, "MXN", null, null, null, 1, "15",
            metodoPagoOrigen: "PPD", condicionPagoOrigen: new string('C', 80));

        pedido.CondicionPagoOrigen.Should().HaveLength(PedidoFacturable.CondicionPagoOrigenMaxLength);
        pedido.EsCredito.Should().BeTrue();
    }

    private static async Task<(FacturacionDbContext Db, Guid PedidoId)> SembrarPedidoAsync(string? metodo, string? condicion)
    {
        var empresaId = Guid.NewGuid();
        var db = NewDb(empresaId);
        var pedido = PedidoFacturable.ImportarDesdeAw(
            empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.ConAnticipo, "MXN", null, null, null, 1, "15",
            metodoPagoOrigen: metodo, condicionPagoOrigen: condicion);
        pedido.AgregarLinea(null, "Producto", "01010101", "H87", 1m, 100m, 0m, false);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        return (db, pedido.Id);
    }

    private static Task<PedidoFacturableDetalleResponse> DetalleAsync(
        FacturacionDbContext db, Guid pedidoId, params string[] permisos) =>
        new PedidoFacturableDetalleHandler(
                db, new FakeClientesReadPort(null), new FakeProductosReadPort(), new FakeCanalesVentaReadPort(),
                new FakeAlcanceCajaEvaluator(), new FakeCurrentUserPermissions(permisos))
            .Handle(new PedidoFacturableDetalleQuery(pedidoId), CancellationToken.None);

    [Fact]
    public async Task Detalle_con_permiso_leer_credito_expone_EsCredito_y_datos_de_pago_del_origen()
    {
        var (db, pedidoId) = await SembrarPedidoAsync("PPD", "CRÉDITO 30 DÍAS");
        using var _ = db;

        var detalle = await DetalleAsync(db, pedidoId, PermisoPedidoCredito.PermisoLeerCredito);

        detalle.EsCredito.Should().BeTrue();
        detalle.MetodoPagoOrigen.Should().Be("PPD");
        detalle.CondicionPagoOrigen.Should().Be("CRÉDITO 30 DÍAS");
    }

    [Fact]
    public async Task Detalle_de_pedido_a_credito_sin_permiso_responde_no_encontrado()
    {
        var (db, pedidoId) = await SembrarPedidoAsync("PPD", "CRÉDITO 30 DÍAS");
        using var _ = db;

        var act = () => DetalleAsync(db, pedidoId, "facturacion.facturas.leer");

        (await act.Should().ThrowAsync<EntityNotFoundException>())
            .Which.Code.Should().Be("PEDIDO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Detalle_de_pedido_de_contado_sin_permiso_de_credito_se_ve()
    {
        var (db, pedidoId) = await SembrarPedidoAsync("PUE", "CONTADO");
        using var _ = db;

        var detalle = await DetalleAsync(db, pedidoId);

        detalle.Id.Should().Be(pedidoId);
        detalle.EsCredito.Should().BeFalse();
    }
    private static async Task<FacturacionDbContext> SembrarBandejaAsync()
    {
        var empresaId = Guid.NewGuid();
        var db = NewDb(empresaId);
        db.PedidosFacturables.AddRange(
            PedidoFacturable.ImportarDesdeAw(
                empresaId, "AW-CRE", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
                ComportamientoFiscal.ConAnticipo, "MXN", null, null, null, 1, "15",
                metodoPagoOrigen: "PPD", condicionPagoOrigen: "CRÉDITO 30 DÍAS"),
            PedidoFacturable.ImportarDesdeAw(
                empresaId, "AW-CON", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
                ComportamientoFiscal.ConAnticipo, "MXN", null, null, null, 1, "15",
                metodoPagoOrigen: "PUE", condicionPagoOrigen: "CONTADO"));
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<BandejaPedidosFacturablesResponse> BandejaAsync(
        FacturacionDbContext db, bool? credito, params string[] permisos) =>
        new BandejaPedidosFacturablesHandler(db, new FakeAlcanceCajaEvaluator(), new FakeCurrentUserPermissions(permisos))
            .Handle(new BandejaPedidosFacturablesQuery(null, null, 0, 50, Credito: credito), CancellationToken.None);

    [Fact]
    public async Task Bandeja_sin_permiso_leer_credito_oculta_pedidos_a_credito_y_no_los_cuenta_sin_asignar()
    {
        using var db = await SembrarBandejaAsync();

        var response = await BandejaAsync(db, null);

        response.Items.Should().ContainSingle().Which.NumeroPedido.Should().Be("AW-CON");
        response.Items.Single().EsCredito.Should().BeFalse();
        // FakeAlcanceCajaEvaluator sin cajas activas: todo cae en "Sin asignar".
        response.SinAsignarCount.Should().Be(1);
    }

    [Fact]
    public async Task Bandeja_sin_permiso_y_filtro_credito_true_no_devuelve_nada()
    {
        using var db = await SembrarBandejaAsync();

        var response = await BandejaAsync(db, true);

        response.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Bandeja_con_permiso_leer_credito_devuelve_pedidos_a_credito_marcados()
    {
        using var db = await SembrarBandejaAsync();

        var response = await BandejaAsync(db, null, PermisoPedidoCredito.PermisoLeerCredito);

        response.Items.Should().HaveCount(2);
        response.Items.Single(i => i.NumeroPedido == "AW-CRE").EsCredito.Should().BeTrue();
        response.Items.Single(i => i.NumeroPedido == "AW-CON").EsCredito.Should().BeFalse();
        response.SinAsignarCount.Should().Be(2);
    }

    [Theory]
    [InlineData(true, "AW-CRE")]
    [InlineData(false, "AW-CON")]
    public async Task Bandeja_filtra_por_condicion_de_pago(bool credito, string esperado)
    {
        using var db = await SembrarBandejaAsync();

        var response = await BandejaAsync(db, credito, PermisoPedidoCredito.PermisoLeerCredito);

        response.Items.Should().ContainSingle().Which.NumeroPedido.Should().Be(esperado);
    }
}
