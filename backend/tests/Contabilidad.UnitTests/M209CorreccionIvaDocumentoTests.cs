using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-209 (86e3zr7mp) · CA4: el IVA de una factura de proveedor se compara por tratamiento contra su XML y la diferencia corrige el saldo
/// de IVA del documento; la parte que corresponde a lo ya pagado va directo a pagado. Datos FIX.
/// </summary>
public class M209CorreccionIvaDocumentoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Proveedor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 10);
    private static readonly Dictionary<string, (decimal Base, decimal Iva)> SinNotas = new();

    // Capturada con IVA 1,500 sobre 10,000 al 16 %; el XML dice 1,600.
    private static SaldoIvaDocumento Capturada() =>
        SaldoIvaDocumento.Crear(Empresa, LadoIva.Acreditable, N.DocFacturaProveedor, Guid.NewGuid(), "F-209", "UUID-FIX", "PPD", Sucursal,
            Proveedor, new DateOnly(2026, 11, 1), "MXN", null, 11_500m, 1_500m, 0m, Guid.NewGuid(), tratamientos: [("TASA16", 10_000m, 1_500m)]);

    [Fact]
    public void CA4_la_diferencia_contra_el_xml_es_de_100_y_queda_pendiente_si_no_hay_pagos()
    {
        var s = Capturada();

        var d = s.Diferencias([("TASA16", 10_000m, 1_600m)], SinNotas).Should().ContainSingle().Subject;

        (d.Tratamiento, d.IvaContabilizado, d.IvaXml, d.IvaDiferencia, d.IvaPagado, d.BaseDiferencia)
            .Should().Be(("TASA16", 1_500m, 1_600m, 100m, 0m, 0m));
    }

    [Fact]
    public void CA4_corregir_sube_el_iva_del_saldo_y_deja_el_movimiento_de_ajuste()
    {
        var s = Capturada();
        var poliza = Guid.NewGuid();
        var diferencias = s.Diferencias([("TASA16", 10_000m, 1_600m)], SinNotas);

        var ajuste = s.Corregir(diferencias, poliza, Hoy, poliza, 1m);

        s.Iva.Should().Be(1_600m);
        s.Tratamientos.Should().ContainSingle(t => t.Tratamiento == "TASA16" && t.Iva == 1_600m);
        ajuste.Tipo.Should().Be(TipoMovimientoIva.Ajuste);
        ajuste.PolizaId.Should().Be(poliza);
        ajuste.Iva.Should().Be(0m, "no había pagos: nada pasa a pagado");
        s.Diferencias([("TASA16", 10_000m, 1_600m)], SinNotas).Should().BeEmpty("ya coincide con el XML");
    }

    [Fact]
    public void Con_la_mitad_pagada_la_mitad_del_ajuste_va_a_pagado()
    {
        var s = Capturada();
        s.Aplicar(("tesoreria", "pago"), Guid.NewGuid(), Hoy, 5_750m, 750m, 0m, Guid.NewGuid());

        var d = s.Diferencias([("TASA16", 10_000m, 1_600m)], SinNotas).Single();
        d.IvaPagado.Should().Be(50m);

        var ajuste = s.Corregir([d], Guid.NewGuid(), Hoy, Guid.NewGuid(), 1m);
        (s.Iva, s.IvaReclasificado, s.IvaPendiente).Should().Be((1_600m, 800m, 800m));
        ajuste.Iva.Should().Be(50m, "la liquidación del mes suma lo que el ajuste pasó a pagado");
    }

    [Fact]
    public void Lo_que_redujeron_las_notas_cuenta_como_iva_del_documento()
    {
        var s = Capturada();
        s.Reducir(("cuentas_por_pagar", "nota_credito"), Guid.NewGuid(), Hoy, 1_150m, 150m, 0m, Guid.NewGuid());

        var notas = new Dictionary<string, (decimal Base, decimal Iva)> { ["TASA16"] = (1_000m, 150m) };
        s.Diferencias([("TASA16", 10_000m, 1_500m)], notas).Should().BeEmpty("1,350 vigente + 150 de la nota = 1,500 del XML");
    }

    [Fact]
    public void Una_base_exenta_que_falta_se_agrega_sin_iva()
    {
        var s = Capturada();

        var d = s.Diferencias([("TASA16", 10_000m, 1_500m), ("EXENTO", 2_500m, 0m)], SinNotas).Should().ContainSingle().Subject;
        (d.Tratamiento, d.BaseDiferencia, d.IvaDiferencia).Should().Be(("EXENTO", 2_500m, 0m));

        s.Corregir([d], Guid.NewGuid(), Hoy, Guid.NewGuid(), 1m);
        s.Tratamientos.Should().Contain(t => t.Tratamiento == "EXENTO" && t.Base == 2_500m);
    }

    [Fact]
    public void Un_documento_cancelado_no_se_corrige()
    {
        var s = Capturada();
        var diferencias = s.Diferencias([("TASA16", 10_000m, 1_600m)], SinNotas);
        s.Cancelar();

        var act = () => s.Corregir(diferencias, Guid.NewGuid(), Hoy, Guid.NewGuid(), 1m);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_IVA_DOCUMENTO_CANCELADO");
    }
}
