using Millet.Almacen.Domain.Movimientos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>86e3tabdp S1: dominio de la cancelación de recepción (tipo CEN + transición a Cancelado).</summary>
public class CancelarRecepcionDominioTests
{
    private static MovimientoInventario Mov(TipoMovimiento tipo, bool registrado)
    {
        var mov = new MovimientoInventario(Guid.NewGuid(), tipo, Guid.NewGuid(), new DateOnly(2026, 10, 23));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 1, Guid.NewGuid(), 5m, "PZA", 10m,
            ubicacionId: Guid.NewGuid()));
        if (registrado)
            mov.Registrar(FolioMovimiento.Construir(tipo, 2026, 1), Guid.NewGuid());
        return mov;
    }

    [Fact]
    public void CEN_tiene_prefijo_y_resta_del_saldo()
    {
        FolioMovimiento.Construir(TipoMovimiento.CancelacionEntradaCompra, 2026, 3).Valor
            .Should().Be("M-CEN2026-000003");
        TipoMovimientoExtensions.EsEntrada(TipoMovimiento.CancelacionEntradaCompra).Should().BeFalse();
    }

    [Fact]
    public void Entrada_registrada_pasa_a_cancelado_con_motivo()
    {
        var mov = Mov(TipoMovimiento.EntradaCompra, registrado: true);
        mov.CancelarRecepcion("ARTICULO: era otro");
        mov.Estado.Should().Be(EstadoMovimiento.Cancelado);
        mov.Motivo.Should().Be("ARTICULO: era otro");
    }

    [Theory]
    [InlineData(TipoMovimiento.EntradaCompra, false)]
    [InlineData(TipoMovimiento.SalidaConsumo, true)]
    public void Solo_entrada_compra_registrada_se_cancela(TipoMovimiento tipo, bool registrado)
    {
        var act = () => Mov(tipo, registrado).CancelarRecepcion("x");
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_NO_CANCELABLE_RECEPCION");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Motivo_vacio_falla(string motivo)
    {
        var act = () => Mov(TipoMovimiento.EntradaCompra, true).CancelarRecepcion(motivo);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_CANCEL_SIN_MOTIVO");
    }

    [Fact]
    public void Motivo_mayor_a_500_falla()
    {
        var act = () => Mov(TipoMovimiento.EntradaCompra, true).CancelarRecepcion(new string('a', 501));
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_CANCEL_MOTIVO_LARGO");
    }

    [Fact]
    public void VincularCancelacion_solo_en_CEN()
    {
        var origen = Guid.NewGuid();
        var cen = Mov(TipoMovimiento.CancelacionEntradaCompra, registrado: false);
        cen.VincularCancelacion(origen);
        cen.RecepcionOrigenId.Should().Be(origen);

        var act = () => Mov(TipoMovimiento.EntradaCompra, false).VincularCancelacion(origen);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("MOV_NO_CANCELACION");
    }
}
