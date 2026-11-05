using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class CfdiRecibidoEstatusSatTests
{
    private static readonly DateTimeOffset Fecha = new(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(-6));

    private static CfdiRecibido Crear(EstatusSatCfdi? estatus = null, DateTimeOffset? fecha = null) =>
        CfdiRecibido.Ingresar(
            empresaId: Guid.NewGuid(),
            uuid: UuidCfdi.Parse("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B"),
            rfcEmisor: RfcMexicano.Parse("PROV010101AAA"),
            rfcReceptor: RfcMexicano.Parse("MIL010101AAA"),
            tipo: TipoCfdi.Ingreso,
            folio: null, serie: null,
            fechaCfdi: DateTimeOffset.UtcNow,
            total: 1160m, subtotal: 1000m, impuestosTrasladados: 160m, retenciones: 0m,
            moneda: "MXN", tipoCambio: null,
            canalOrigen: CanalOrigenCfdi.CargaManual,
            fechaRecepcion: DateTimeOffset.UtcNow,
            xmlBlobRef: "x", pdfBlobRef: null, xmlHashSha256: "y",
            estatusSat: estatus, fechaCancelacionSat: fecha);

    [Fact]
    public void Ingresar_con_Cancelado_guarda_estatus_y_fecha_utc()
    {
        var cfdi = Crear(EstatusSatCfdi.Cancelado, Fecha);
        cfdi.EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        cfdi.FechaCancelacionSat.Should().Be(Fecha);
        cfdi.FechaCancelacionSat!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Ingresar_sin_argumentos_deja_ambos_nulos()
    {
        var cfdi = Crear();
        cfdi.EstatusSat.Should().BeNull();
        cfdi.FechaCancelacionSat.Should().BeNull();
    }

    [Fact]
    public void ActualizarEstatusSat_devuelve_true_si_cambia_y_false_si_es_igual()
    {
        var cfdi = Crear();
        cfdi.ActualizarEstatusSat(EstatusSatCfdi.Cancelado, Fecha).Should().BeTrue();
        cfdi.ActualizarEstatusSat(EstatusSatCfdi.Cancelado, Fecha).Should().BeFalse();
        cfdi.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
    }

    [Fact]
    public void ActualizarEstatusSat_de_Cancelado_a_Vigente_limpia_la_fecha()
    {
        var cfdi = Crear(EstatusSatCfdi.Cancelado, Fecha);
        cfdi.ActualizarEstatusSat(EstatusSatCfdi.Vigente, Fecha).Should().BeTrue();
        cfdi.EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
        cfdi.FechaCancelacionSat.Should().BeNull();
    }

    [Theory]
    [InlineData("CANCELADO", EstatusSatCfdi.Cancelado)]
    [InlineData(" cancelado ", EstatusSatCfdi.Cancelado)]
    [InlineData("Vigente", EstatusSatCfdi.Vigente)]
    [InlineData("Xyz", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseEstatusSat_interpreta_el_texto(string? valor, EstatusSatCfdi? esperado) =>
        CfdiRecibido.ParseEstatusSat(valor).Should().Be(esperado);
}
