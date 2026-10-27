using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Reportes.ControlAnticipos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>
/// 86e3tfd6n — anticipo cobrado antes de que el pedido llegue al ERP: guarda el número de pedido de A+W y queda ligado
/// en cuanto la ingesta importa ese pedido para el mismo cliente (ligado calculado, sin versión).
/// </summary>
public sealed class AnticipoPedidoAwTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private const string NumeroAw = "10425068";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static EmitirFacturaAnticipoCommand Command(string? pedidoOrigenRef) => new(
        Guid.NewGuid(), Guid.NewGuid(), "AAA010101AAA", "Cliente Maquila", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, 1000m, 0.16m, null, null, pedidoOrigenRef, null, null);

    private static Anticipo AnticipoDe(Guid empresaId, Guid clienteId, string? pedido) =>
        Anticipo.Crear(empresaId, clienteId, "AAA010101AAA", TipoAnticipo.ClientesMxp, "MXN", 1160m, Guid.NewGuid(), pedido);

    private static PedidoFacturable PedidoAw(Guid empresaId, Guid clienteId, string numero = NumeroAw) =>
        PedidoFacturable.ImportarDesdeAw(empresaId, numero, Guid.NewGuid(), clienteId, "Cliente Maquila", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");

    private static Task<ControlAnticiposDetalladaResponse> ControlAsync(FacturacionDbContext db, Guid clienteId) =>
        new ControlAnticiposDetalladaHandler(db, new FakeClock(Ahora))
            .Handle(new ControlAnticiposDetalladaQuery(clienteId), CancellationToken.None);

    [Theory]
    [InlineData(null)]
    [InlineData(NumeroAw)]
    public void Numero_de_pedido_aw_es_opcional_y_numerico(string? numero) =>
        new EmitirFacturaAnticipoValidator().Validate(Command(numero))
            .Errors.Should().NotContain(e => e.PropertyName == nameof(EmitirFacturaAnticipoCommand.PedidoOrigenRef));

    [Theory]
    [InlineData("AW 10425068")]
    [InlineData("10425068-005")]
    public void Numero_de_pedido_aw_con_letras_espacios_o_guiones_se_rechaza(string numero) =>
        new EmitirFacturaAnticipoValidator().Validate(Command(numero))
            .Errors.Should().Contain(e => e.PropertyName == nameof(EmitirFacturaAnticipoCommand.PedidoOrigenRef));

    [Fact]
    public void El_anticipo_guarda_el_numero_sin_espacios_y_en_blanco_queda_sin_pedido()
    {
        AnticipoDe(Guid.NewGuid(), Guid.NewGuid(), $"  {NumeroAw} ").PedidoOrigenRef.Should().Be(NumeroAw);
        AnticipoDe(Guid.NewGuid(), Guid.NewGuid(), "   ").PedidoOrigenRef.Should().BeNull();
    }

    [Fact]
    public async Task El_anticipo_queda_pendiente_y_se_liga_cuando_la_ingesta_importa_el_pedido_del_mismo_cliente()
    {
        var empresaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        db.Anticipos.Add(AnticipoDe(empresaId, clienteId, NumeroAw));
        await db.SaveChangesAsync();

        // (1) Cobrado con un número de pedido que aún no existe en el ERP.
        var antes = (await ControlAsync(db, clienteId)).Anticipos.Single();
        antes.PedidoOrigenRef.Should().Be(NumeroAw);
        antes.PedidoLigado.Should().BeNull();

        // (2) La ingesta importa el pedido (15/42 o liberado): el anticipo aparece ligado y sigue disponible.
        var pedido = PedidoAw(empresaId, clienteId);
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();

        var despues = (await ControlAsync(db, clienteId)).Anticipos.Single();
        despues.PedidoLigado.Should().NotBeNull();
        despues.PedidoLigado!.PedidoFacturableId.Should().Be(pedido.Id);
        despues.PedidoLigado.NumeroPedido.Should().Be(NumeroAw);
        despues.Saldo.Should().Be(1160m);
        despues.Estado.Should().Be(nameof(EstadoAnticipo.Abierto));
    }

    [Fact]
    public async Task Un_pedido_con_el_mismo_numero_de_otro_cliente_no_liga_el_anticipo()
    {
        var empresaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        db.Anticipos.Add(AnticipoDe(empresaId, clienteId, NumeroAw));
        db.PedidosFacturables.Add(PedidoAw(empresaId, Guid.NewGuid()));
        await db.SaveChangesAsync();

        (await ControlAsync(db, clienteId)).Anticipos.Single().PedidoLigado.Should().BeNull();
    }

    [Fact]
    public async Task El_detalle_del_pedido_muestra_los_anticipos_ligados_del_mismo_cliente()
    {
        var empresaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = PedidoAw(empresaId, clienteId);
        db.PedidosFacturables.Add(pedido);
        db.Anticipos.Add(AnticipoDe(empresaId, clienteId, NumeroAw));
        db.Anticipos.Add(AnticipoDe(empresaId, Guid.NewGuid(), NumeroAw)); // otro cliente: no
        db.Anticipos.Add(AnticipoDe(empresaId, clienteId, "99999999"));    // otro pedido: no
        await db.SaveChangesAsync();

        var detalle = await new PedidoFacturableDetalleHandler(
                db, new FakeClientesReadPort(), new FakeProductosReadPort(), new FakeCanalesVentaReadPort(), new FakeAlcanceCajaEvaluator(), new FakeCurrentUserPermissions())
            .Handle(new PedidoFacturableDetalleQuery(pedido.Id), CancellationToken.None);

        var ligado = detalle.AnticiposLigados.Should().ContainSingle().Subject;
        ligado.Saldo.Should().Be(1160m);
        ligado.Estado.Should().Be(nameof(EstadoAnticipo.Abierto));
    }
}
