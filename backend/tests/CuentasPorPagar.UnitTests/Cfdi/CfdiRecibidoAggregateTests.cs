using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class CfdiRecibidoAggregateTests
{
    private static CfdiRecibido Crear() =>
        CfdiRecibido.Ingresar(
            empresaId: Guid.NewGuid(),
            uuid: UuidCfdi.Parse("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B"),
            rfcEmisor: RfcMexicano.Parse("PROV010101AAA"),
            rfcReceptor: RfcMexicano.Parse("MIL010101AAA"),
            tipo: TipoCfdi.Ingreso,
            folio: "F123",
            serie: "A",
            fechaCfdi: DateTimeOffset.UtcNow,
            total: 1160m,
            subtotal: 1000m,
            impuestosTrasladados: 160m,
            retenciones: 0m,
            moneda: "MXN",
            tipoCambio: null,
            canalOrigen: CanalOrigenCfdi.CargaManual,
            fechaRecepcion: DateTimeOffset.UtcNow,
            xmlBlobRef: "cxp/2026/05/cfdi/abc.xml",
            pdfBlobRef: null,
            xmlHashSha256: "deadbeef" + new string('0', 56));

    [Fact]
    public void Ingresar_crea_cfdi_en_estado_PorProcesar()
    {
        var cfdi = Crear();
        cfdi.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
        cfdi.DocumentoDestinoId.Should().BeNull();
        cfdi.MotivoDescarte.Should().BeNull();
        cfdi.CfdiOriginalId.Should().BeNull();
    }

    [Fact]
    public void Ingresar_rechaza_total_negativo()
    {
        var act = () => CfdiRecibido.Ingresar(
            empresaId: Guid.NewGuid(),
            uuid: UuidCfdi.Parse("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B"),
            rfcEmisor: RfcMexicano.Parse("PROV010101AAA"),
            rfcReceptor: RfcMexicano.Parse("MIL010101AAA"),
            tipo: TipoCfdi.Ingreso,
            folio: null, serie: null,
            fechaCfdi: DateTimeOffset.UtcNow,
            total: -1m, subtotal: 0m, impuestosTrasladados: 0m, retenciones: 0m,
            moneda: "MXN", tipoCambio: null,
            canalOrigen: CanalOrigenCfdi.CargaManual,
            fechaRecepcion: DateTimeOffset.UtcNow,
            xmlBlobRef: "x", pdfBlobRef: null, xmlHashSha256: "y");

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CFDI_TOTAL_NEGATIVO");
    }

    [Fact]
    public void Descartar_desde_PorProcesar_pasa_a_Descartado_con_motivo()
    {
        var cfdi = Crear();
        cfdi.Descartar("Emitido por error por el proveedor");

        cfdi.Estado.Should().Be(EstadoCfdiRecibido.Descartado);
        cfdi.MotivoDescarte.Should().Be("Emitido por error por el proveedor");
    }

    [Fact]
    public void Descartar_rechaza_motivo_vacio()
    {
        var cfdi = Crear();
        var act = () => cfdi.Descartar("");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CFDI_MOTIVO_DESCARTE_VACIO");
    }

    [Fact]
    public void Descartar_rechaza_si_no_esta_en_PorProcesar()
    {
        var cfdi = Crear();
        cfdi.Descartar("Motivo");

        var act = () => cfdi.Descartar("Otro motivo");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CFDI_ESTADO_INVALIDO");
    }

    [Fact]
    public void MarcarDuplicado_pasa_a_Duplicado_apuntando_al_original()
    {
        var cfdi = Crear();
        var originalId = Guid.NewGuid();

        cfdi.MarcarDuplicado(originalId);

        cfdi.Estado.Should().Be(EstadoCfdiRecibido.Duplicado);
        cfdi.CfdiOriginalId.Should().Be(originalId);
    }

    [Fact]
    public void MarcarDuplicado_rechaza_auto_duplicado()
    {
        var cfdi = Crear();
        var act = () => cfdi.MarcarDuplicado(cfdi.Id);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CFDI_AUTO_DUPLICADO");
    }

    [Fact]
    public void MarcarConvertidoEnPasivo_pasa_a_ConvertidoEnPasivo()
    {
        var cfdi = Crear();
        var pasivoId = Guid.NewGuid();

        cfdi.MarcarConvertidoEnPasivo(pasivoId);

        cfdi.Estado.Should().Be(EstadoCfdiRecibido.ConvertidoEnPasivo);
        cfdi.DocumentoDestinoId.Should().Be(pasivoId);
    }

    [Fact]
    public void RevertirAPorProcesar_regresa_a_PorProcesar_y_limpia_destino()
    {
        var cfdi = Crear();
        var pasivoId = Guid.NewGuid();
        cfdi.MarcarConvertidoEnPasivo(pasivoId);

        cfdi.RevertirAPorProcesar(pasivoId);

        cfdi.Estado.Should().Be(EstadoCfdiRecibido.PorProcesar);
        cfdi.DocumentoDestinoId.Should().BeNull();
    }

    [Fact]
    public void RevertirAPorProcesar_rechaza_si_no_esta_ConvertidoEnPasivo()
    {
        var cfdi = Crear();

        var act = () => cfdi.RevertirAPorProcesar(Guid.NewGuid());

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CFDI_ESTADO_INVALIDO");
    }

    [Fact]
    public void RevertirAPorProcesar_rechaza_si_el_destino_no_coincide()
    {
        var cfdi = Crear();
        cfdi.MarcarConvertidoEnPasivo(Guid.NewGuid());

        var act = () => cfdi.RevertirAPorProcesar(Guid.NewGuid());

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CFDI_DESTINO_NO_COINCIDE");
    }

    [Fact]
    public void RevertirAPorProcesar_permite_reconvertir_despues()
    {
        var cfdi = Crear();
        var primero = Guid.NewGuid();
        cfdi.MarcarConvertidoEnPasivo(primero);
        cfdi.RevertirAPorProcesar(primero);

        var segundo = Guid.NewGuid();
        cfdi.MarcarConvertidoEnPasivo(segundo);

        cfdi.Estado.Should().Be(EstadoCfdiRecibido.ConvertidoEnPasivo);
        cfdi.DocumentoDestinoId.Should().Be(segundo);
    }
}
