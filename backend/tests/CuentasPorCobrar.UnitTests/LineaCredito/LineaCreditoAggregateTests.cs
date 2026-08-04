using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.LineaCredito;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

public class LineaCreditoAggregateTests
{
    private static LineaCreditoAggregate Crear(
        Guid? clienteId = null,
        string moneda = "MXN",
        decimal limite = 500_000m,
        OrigenLineaCredito origen = OrigenLineaCredito.Solunion,
        int plazoDias = 45) =>
        LineaCreditoAggregate.Crear(
            empresaId: Guid.NewGuid(),
            clienteId: clienteId ?? Guid.NewGuid(),
            moneda: moneda,
            limite: limite,
            origen: origen,
            plazoDias: plazoDias);

    // --------------------------------------------------- Crear

    [Fact]
    public void Crear_valida_OK()
    {
        var l = Crear();

        l.Id.Should().NotBeEmpty();
        l.Estado.Should().Be(EstadoLineaCredito.Activa);
        l.Moneda.Should().Be("MXN");
        l.Limite.Should().Be(500_000m);
        l.Origen.Should().Be(OrigenLineaCredito.Solunion);
        l.PlazoDias.Should().Be(45);
        l.MotivoBloqueo.Should().BeNull();
    }

    [Fact]
    public void Crear_rechaza_cliente_vacio()
    {
        var act = () => Crear(clienteId: Guid.Empty);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_CLIENTE_VACIO");
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("mxn")]
    [InlineData("")]
    public void Crear_rechaza_moneda_invalida(string moneda)
    {
        var act = () => Crear(moneda: moneda);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_MONEDA_INVALIDA");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void Crear_rechaza_limite_no_positivo(decimal limite)
    {
        var act = () => Crear(limite: limite);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_LIMITE_INVALIDO");
    }

    [Fact]
    public void Crear_rechaza_plazo_no_positivo()
    {
        var act = () => Crear(plazoDias: 0);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_PLAZO_INVALIDO");
    }

    // --------------------------------------------------- ActualizarDatos

    [Fact]
    public void ActualizarDatos_edita_limite_y_plazo()
    {
        var l = Crear();

        l.ActualizarDatos(limite: 750_000m, plazoDias: 60);

        l.Limite.Should().Be(750_000m);
        l.PlazoDias.Should().Be(60);
    }

    [Fact]
    public void ActualizarDatos_permitido_en_Bloqueada()
    {
        var l = Crear();
        l.Bloquear("Cartera vencida > 90 días");

        l.ActualizarDatos(limite: 100_000m, plazoDias: 30);

        l.Limite.Should().Be(100_000m);
    }

    [Fact]
    public void ActualizarDatos_rechaza_limite_no_positivo()
    {
        var l = Crear();
        var act = () => l.ActualizarDatos(limite: 0, plazoDias: 45);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_LIMITE_INVALIDO");
    }

    // --------------------------------------------------- Bloquear / Desbloquear

    [Fact]
    public void Bloquear_desde_Activa_OK()
    {
        var l = Crear();

        l.Bloquear("  Siniestro SOLUNION reportado  ");

        l.Estado.Should().Be(EstadoLineaCredito.Bloqueada);
        l.MotivoBloqueo.Should().Be("Siniestro SOLUNION reportado");
    }

    [Fact]
    public void Bloquear_rechaza_motivo_vacio()
    {
        var l = Crear();
        var act = () => l.Bloquear("   ");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_MOTIVO_BLOQUEO_VACIO");
    }

    [Fact]
    public void Bloquear_rechaza_doble_bloqueo()
    {
        var l = Crear();
        l.Bloquear("Primer motivo");

        var act = () => l.Bloquear("Segundo motivo");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_NO_BLOQUEABLE");
    }

    [Fact]
    public void Desbloquear_desde_Bloqueada_OK()
    {
        var l = Crear();
        l.Bloquear("Cartera vencida");

        l.Desbloquear();

        l.Estado.Should().Be(EstadoLineaCredito.Activa);
        l.MotivoBloqueo.Should().BeNull();
    }

    [Fact]
    public void Desbloquear_rechaza_desde_Activa()
    {
        var l = Crear();
        var act = () => l.Desbloquear();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LC_NO_DESBLOQUEABLE");
    }
}
