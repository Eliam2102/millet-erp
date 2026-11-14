using Millet.Facturacion.Domain.Anticipos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>M-344: reversa interna autorizada de la aplicación de un anticipo a una factura.</summary>
public sealed class ReversaAplicacionAnticipoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid QuienAplico = Guid.CreateVersion7();
    private static readonly Guid QuienAutoriza = Guid.CreateVersion7();

    private static Anticipo Nuevo() => Anticipo.Crear(
        empresaId: Guid.NewGuid(), clienteId: Guid.NewGuid(), receptorRfc: "AAA010101AAA",
        tipoAnticipo: TipoAnticipo.ClientesMxp, moneda: "MXN", montoCobrado: 1000m, facturaAnticipoId: Guid.NewGuid());

    private static (Anticipo Anticipo, Guid Factura, Guid Nc) Aplicado(decimal importe = 1000m)
    {
        var a = Nuevo();
        var factura = Guid.NewGuid();
        var nc = Guid.NewGuid();
        a.RegistrarAmortizacion(factura, nc, importe, Ahora);
        return (a, factura, nc);
    }

    [Fact]
    public void La_reversa_devuelve_el_saldo_al_anticipo_lo_reabre_y_deja_el_rastro_ligado_a_la_NC()
    {
        var (a, factura, nc) = Aplicado(1000m);
        a.Estado.Should().Be(EstadoAnticipo.Amortizado);

        var r = a.RevertirAplicacion(factura, "  Se aplicó a la factura equivocada ", QuienAplico, QuienAutoriza, "Gerente", Ahora.AddHours(1));

        a.Saldo.Should().Be(1000m);
        a.MontoAmortizado.Should().Be(0m);
        a.Estado.Should().Be(EstadoAnticipo.Abierto);
        a.SaldoDisponible.Should().Be(1000m);
        r.NcAmortizacionId.Should().Be(nc);
        r.FacturaVentaId.Should().Be(factura);
        r.Importe.Should().Be(1000m);
        r.Motivo.Should().Be("Se aplicó a la factura equivocada");
        r.AplicadoPorId.Should().Be(QuienAplico);
        r.AutorizadoPorId.Should().Be(QuienAutoriza);
    }

    [Fact]
    public void Quien_aplico_no_puede_autorizar_su_reversa_y_nada_cambia()
    {
        var (a, factura, _) = Aplicado(400m);

        var act = () => a.RevertirAplicacion(factura, "Se aplicó a la factura equivocada", QuienAplico, QuienAplico, "Facturista", Ahora);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_REVERSA_MISMO_USUARIO");
        a.MontoAmortizado.Should().Be(400m);
        a.Vinculaciones.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("corto")]
    public void El_motivo_es_obligatorio(string motivo)
    {
        var (a, factura, _) = Aplicado(400m);
        var act = () => a.RevertirAplicacion(factura, motivo, QuienAplico, QuienAutoriza, "Gerente", Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_REVERSA_MOTIVO");
    }

    [Fact]
    public void Sin_aplicacion_a_esa_factura_no_hay_nada_que_revertir()
    {
        var a = Nuevo();
        var act = () => a.RevertirAplicacion(Guid.NewGuid(), "No hay aplicación que revertir", QuienAplico, QuienAutoriza, "Gerente", Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_SIN_APLICACION");
    }

    [Fact]
    public void Despues_de_revertir_la_factura_puede_recibir_otra_aplicacion_y_la_reversa_no_se_repite()
    {
        var (a, factura, _) = Aplicado(300m);
        a.RevertirAplicacion(factura, "Se aplicó a la factura equivocada", QuienAplico, QuienAutoriza, "Gerente", Ahora);

        var otraVez = () => a.RevertirAplicacion(factura, "Se aplicó a la factura equivocada", QuienAplico, QuienAutoriza, "Gerente", Ahora);
        otraVez.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_SIN_APLICACION");

        var reaplicar = () => a.RegistrarAmortizacion(factura, Guid.NewGuid(), 300m, Ahora.AddHours(2));
        reaplicar.Should().NotThrow();
        a.Saldo.Should().Be(700m);
    }
}
