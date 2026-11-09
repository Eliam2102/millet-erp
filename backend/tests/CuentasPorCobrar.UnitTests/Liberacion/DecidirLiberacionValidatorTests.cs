using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.CuentasPorCobrar.UnitTests.CreditoDisponible;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

/// <summary>M9 (prueba S3-4): la decisión manual valida moneda y monto contra el pedido que Facturación tiene sin facturar.</summary>
public sealed class DecidirLiberacionValidatorTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    private static DecidirLiberacionValidator Validador(PedidosSinFacturarFalso pedidos) =>
        new(pedidos, new EmpresaContext(EmpresaId));

    private static DecidirLiberacionCommand Comando(string pedidoRef = "55000101", decimal monto = 5_800m, string moneda = "MXN") =>
        new(pedidoRef, ClienteId, moneda, monto, null);

    [Fact]
    public async Task Rechaza_un_monto_distinto_al_del_pedido_y_dice_cuanto_vale()
    {
        var pedidos = new PedidosSinFacturarFalso().Agregar(ClienteId, "55000101", 5_800m);

        var r = await Validador(pedidos).ValidateAsync(Comando(monto: 10_000m));

        r.IsValid.Should().BeFalse();
        var error = r.Errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(DecidirLiberacionCommand.MontoPedido));
        error.ErrorCode.Should().Be(DecidirLiberacionValidator.CodigoMontoDistinto);
        error.ErrorMessage.Should().Be("El pedido 55000101 vale 5,800.00 MXN con IVA; captura ese monto.");
    }

    [Theory]
    [InlineData(5_800)]
    [InlineData(5_800.01)]
    [InlineData(5_799.99)]
    public async Task Acepta_el_monto_del_pedido_con_tolerancia_de_un_centavo(decimal monto)
    {
        var pedidos = new PedidosSinFacturarFalso().Agregar(ClienteId, "55000101", 5_800m);

        (await Validador(pedidos).ValidateAsync(Comando(pedidoRef: " 55000101 ", monto: monto))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Rechaza_una_moneda_distinta_a_la_del_pedido()
    {
        var pedidos = new PedidosSinFacturarFalso().Agregar(ClienteId, "55000101", 5_800m, moneda: "USD");

        var r = await Validador(pedidos).ValidateAsync(Comando());

        r.Errors.Should().ContainSingle(e => e.PropertyName == nameof(DecidirLiberacionCommand.Moneda)
                                          && e.ErrorCode == DecidirLiberacionValidator.CodigoMonedaDistinta);
    }

    [Fact]
    public async Task Un_folio_que_Facturacion_no_tiene_para_el_cliente_se_acepta()
    {
        var pedidos = new PedidosSinFacturarFalso().Agregar(Guid.NewGuid(), "55000101", 5_800m);

        (await Validador(pedidos).ValidateAsync(Comando(monto: 10_000m))).IsValid.Should().BeTrue();
    }

    private sealed class EmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
