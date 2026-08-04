using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

public class AutorizacionCreditoAggregateTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private static AutorizacionCredito Crear(
        Guid? supervisor = null,
        Guid? beneficiario = null,
        TimeSpan? vigencia = null) =>
        AutorizacionCredito.Crear(
            empresaId: Guid.NewGuid(),
            supervisorUsuarioId: supervisor ?? Guid.NewGuid(),
            beneficiarioUsuarioId: beneficiario ?? Guid.NewGuid(),
            motivo: "Cliente estratégico con promesa de pago",
            clienteOPedidoRef: "3000123",
            ahora: Ahora,
            vigencia: vigencia ?? TimeSpan.FromHours(24));

    [Fact]
    public void Crear_valida_OK()
    {
        var a = Crear();
        a.Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
        a.VigenteHasta.Should().Be(Ahora.AddHours(24));
        a.DecisionLiberacionId.Should().BeNull();
    }

    [Fact]
    public void Crear_rechaza_autoconsumo()
    {
        var usuario = Guid.NewGuid();
        var act = () => Crear(supervisor: usuario, beneficiario: usuario);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_AUTOCONSUMO");
    }

    [Fact]
    public void Crear_rechaza_vigencia_mayor_a_24h()
    {
        var act = () => Crear(vigencia: TimeSpan.FromHours(25));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_VIGENCIA_INVALIDA");
    }

    [Fact]
    public void Consumir_un_solo_uso()
    {
        var beneficiario = Guid.NewGuid();
        var a = Crear(beneficiario: beneficiario);
        var decisionId = Guid.NewGuid();

        a.Consumir(decisionId, beneficiario, Ahora.AddHours(1));

        a.Estado.Should().Be(EstadoAutorizacionCredito.Usada);
        a.DecisionLiberacionId.Should().Be(decisionId);

        var act = () => a.Consumir(Guid.NewGuid(), beneficiario, Ahora.AddHours(2));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_NO_DISPONIBLE");
    }

    [Fact]
    public void Consumir_rechaza_vencida()
    {
        var beneficiario = Guid.NewGuid();
        var a = Crear(beneficiario: beneficiario, vigencia: TimeSpan.FromHours(1));

        var act = () => a.Consumir(Guid.NewGuid(), beneficiario, Ahora.AddHours(2));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_VENCIDA");
    }

    [Fact]
    public void Consumir_rechaza_beneficiario_distinto()
    {
        var a = Crear();
        var act = () => a.Consumir(Guid.NewGuid(), Guid.NewGuid(), Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_BENEFICIARIO_DISTINTO");
    }

    [Fact]
    public void Cancelar_solo_vigentes()
    {
        var beneficiario = Guid.NewGuid();
        var a = Crear(beneficiario: beneficiario);
        a.Consumir(Guid.NewGuid(), beneficiario, Ahora);

        var act = a.Cancelar;
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_NO_DISPONIBLE");
    }
}
