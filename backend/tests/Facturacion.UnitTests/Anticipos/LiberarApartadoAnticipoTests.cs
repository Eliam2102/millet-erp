using Millet.Facturacion.Domain.Anticipos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>U-4 (86e3yzemw · flujo 3): liberar con usuario y motivo el importe apartado que no corresponde.</summary>
public sealed class LiberarApartadoAnticipoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Usuario = Guid.CreateVersion7();

    private static Anticipo Nuevo() => Anticipo.Crear(
        empresaId: Guid.NewGuid(), clienteId: Guid.NewGuid(), receptorRfc: "AAA010101AAA",
        tipoAnticipo: TipoAnticipo.ClientesMxp, moneda: "MXN", montoCobrado: 1000m, facturaAnticipoId: Guid.NewGuid());

    [Fact]
    public void Libera_el_apartado_devuelve_el_saldo_disponible_y_deja_constancia()
    {
        var a = Nuevo();
        var factura = Guid.NewGuid();
        a.Vincular(factura, 400m, Ahora);
        a.SaldoDisponible.Should().Be(600m);

        var l = a.LiberarApartado(factura, "  Se vinculó a la factura equivocada ", Usuario, "Facturista", Ahora.AddHours(1));

        a.SaldoDisponible.Should().Be(1000m);
        a.Saldo.Should().Be(1000m);
        a.Vinculaciones.Should().BeEmpty();
        l.Importe.Should().Be(400m);
        l.Motivo.Should().Be("Se vinculó a la factura equivocada");
        l.LiberadoPorId.Should().Be(Usuario);
        l.LiberadoPorNombre.Should().Be("Facturista");
        a.Liberaciones.Should().ContainSingle().Which.Should().BeSameAs(l);
    }

    [Fact]
    public void Despues_de_liberar_se_puede_volver_a_vincular_a_otra_factura()
    {
        var a = Nuevo();
        var f1 = Guid.NewGuid();
        a.Vincular(f1, 1000m, Ahora);
        a.LiberarApartado(f1, "No corresponde a este pedido", Usuario, "Facturista", Ahora);

        var act = () => a.Vincular(Guid.NewGuid(), 1000m, Ahora);
        act.Should().NotThrow();
    }

    [Fact]
    public void Sin_apartado_para_esa_factura_se_rechaza()
    {
        var a = Nuevo();
        var act = () => a.LiberarApartado(Guid.NewGuid(), "No corresponde", Usuario, "Facturista", Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_APARTADO_NO_ENCONTRADO");
    }

    [Fact]
    public void Ya_amortizado_con_su_NC_no_se_libera()
    {
        var a = Nuevo();
        var factura = Guid.NewGuid();
        a.Vincular(factura, 300m, Ahora);
        a.RegistrarAmortizacion(factura, Guid.NewGuid(), 300m, Ahora);

        var act = () => a.LiberarApartado(factura, "No corresponde", Usuario, "Facturista", Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_APARTADO_YA_AMORTIZADO");
        a.Vinculaciones.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("no")]
    public void Exige_motivo(string motivo)
    {
        var a = Nuevo();
        var factura = Guid.NewGuid();
        a.Vincular(factura, 100m, Ahora);

        var act = () => a.LiberarApartado(factura, motivo, Usuario, "Facturista", Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_MOTIVO_REQUERIDO");
        a.Vinculaciones.Should().ContainSingle();
    }
}
