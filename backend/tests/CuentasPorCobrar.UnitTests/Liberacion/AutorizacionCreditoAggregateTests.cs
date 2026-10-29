using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

public class AutorizacionCreditoAggregateTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);
    private const string Folio = "3000123";

    private static AutorizacionCredito Crear(
        Guid? supervisor = null,
        Guid? beneficiario = null,
        TimeSpan? vigencia = null) =>
        AutorizacionCredito.Crear(
            empresaId: Guid.NewGuid(),
            supervisorUsuarioId: supervisor ?? Guid.NewGuid(),
            beneficiarioUsuarioId: beneficiario ?? Guid.NewGuid(),
            motivo: "Cliente estratégico con promesa de pago",
            clienteOPedidoRef: Folio,
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

        a.Consumir(decisionId, beneficiario, Ahora.AddHours(1), Folio);

        a.Estado.Should().Be(EstadoAutorizacionCredito.Usada);
        a.DecisionLiberacionId.Should().Be(decisionId);

        var act = () => a.Consumir(Guid.NewGuid(), beneficiario, Ahora.AddHours(2), Folio);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_NO_DISPONIBLE");
    }

    [Fact]
    public void Consumir_rechaza_vencida()
    {
        var beneficiario = Guid.NewGuid();
        var a = Crear(beneficiario: beneficiario, vigencia: TimeSpan.FromHours(1));

        var act = () => a.Consumir(Guid.NewGuid(), beneficiario, Ahora.AddHours(2), Folio);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_VENCIDA");
    }

    [Fact]
    public void Consumir_rechaza_beneficiario_distinto()
    {
        var a = Crear();
        var act = () => a.Consumir(Guid.NewGuid(), Guid.NewGuid(), Ahora, Folio);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_BENEFICIARIO_DISTINTO");
    }

    // C7.3 · E10 (Eliam, 26-oct) — la autorización es POR PEDIDO: solo se
    // consume con el folio exacto (tras Trim, ordinal).
    [Fact]
    public void Consumir_rechaza_folio_de_otro_pedido_sin_consumir()
    {
        var beneficiario = Guid.NewGuid();
        var a = Crear(beneficiario: beneficiario);

        var act = () => a.Consumir(Guid.NewGuid(), beneficiario, Ahora, "3000999");

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_REFERENCIA_DISTINTA");
        a.Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    [Fact]
    public void Consumir_acepta_el_folio_exacto_con_espacios_sobrantes()
    {
        var beneficiario = Guid.NewGuid();
        var a = Crear(beneficiario: beneficiario);

        a.Consumir(Guid.NewGuid(), beneficiario, Ahora, " 3000123 ");

        a.Estado.Should().Be(EstadoAutorizacionCredito.Usada);
    }

    [Theory]
    [InlineData("VGL860910IU4")]          // otorgada al RFC del cliente
    [InlineData("VIDRIOS DEL GOLFO SA")]  // otorgada a la razón social
    [InlineData("610090")]                // folio que solo es prefijo
    public void Consumir_ya_no_acepta_referencia_por_cliente_ni_folio_parecido(string referenciaOtorgada)
    {
        var beneficiario = Guid.NewGuid();
        var a = AutorizacionCredito.Crear(
            Guid.NewGuid(), Guid.NewGuid(), beneficiario, "Motivo", referenciaOtorgada, Ahora, TimeSpan.FromHours(1));

        var act = () => a.Consumir(Guid.NewGuid(), beneficiario, Ahora, "6100901");

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_REFERENCIA_DISTINTA");
        a.Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    [Fact]
    public void Consumir_distingue_mayusculas_del_folio()
    {
        var beneficiario = Guid.NewGuid();
        var a = AutorizacionCredito.Crear(
            Guid.NewGuid(), Guid.NewGuid(), beneficiario, "Motivo", "QA-C73-01", Ahora, TimeSpan.FromHours(1));

        var act = () => a.Consumir(Guid.NewGuid(), beneficiario, Ahora, "qa-c73-01");

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_REFERENCIA_DISTINTA");
    }

    [Fact]
    public void Cancelar_solo_vigentes()
    {
        var beneficiario = Guid.NewGuid();
        var a = Crear(beneficiario: beneficiario);
        a.Consumir(Guid.NewGuid(), beneficiario, Ahora, Folio);

        var act = a.Cancelar;
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_NO_DISPONIBLE");
    }
}
