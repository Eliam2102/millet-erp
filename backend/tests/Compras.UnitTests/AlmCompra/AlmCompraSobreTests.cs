using System.Text.Json;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.AlmCompra;

/// <summary>86e3jvu83: sobre v1 de COMPRA_RECIBIDA y reglas de cantidad y costo (contrato v6).</summary>
public sealed class AlmCompraSobreTests
{
    private static AlmCompraDatos Datos(string moneda = "MXN", decimal? tc = null) => new("OC-5521", 164, "3M MEXICO", moneda, tc,
        new DateOnly(2026, 10, 1),
        [new AlmCompraLinea("1", "RQ-00000003", 1, 100006, 501, 3300, 6100, 10, "L-7781", "26 47 3456 6001234", null, 210.5m, 4237.8m)]);

    [Fact]
    public void Hojas_en_m2_las_piezas_salen_de_la_superficie_y_costoPieza_es_costoM2_por_area()
    {
        // 201.3 m² de hojas de 3300x6100 (20.13 m² c/u) = 10 piezas; subtotal 42 373.65 → 210.5/m²
        var (piezas, recibidas, m2, pieza) = AlmCompraSobre.Calcular("M2", 201.3m, 40.26m, 42373.65m, 3300, 6100);
        (piezas, recibidas).Should().Be((10, 2));
        m2.Should().Be(210.5m);
        pieza.Should().Be(4237.365m);
    }

    [Fact]
    public void Material_por_pieza_sin_medida_no_manda_costoM2()
    {
        var (piezas, _, m2, pieza) = AlmCompraSobre.Calcular("PZA", 24, 0, 600m, null, null);
        (piezas, m2, pieza).Should().Be((24, null, 25m));
    }

    [Fact]
    public void M2_sin_medida_o_piezas_no_enteras_son_error_de_regla_de_negocio()
    {
        FluentActions.Invoking(() => AlmCompraSobre.Calcular("M2", 10m, 0, 100m, null, null)).Should().Throw<BusinessRuleException>().Where(e => e.Code == "ALM_COMPRA_SIN_MEDIDA");
        FluentActions.Invoking(() => AlmCompraSobre.Calcular("PZA", 2.5m, 0, 100m, null, null)).Should().Throw<BusinessRuleException>().Where(e => e.Code == "ALM_COMPRA_PIEZAS_NO_ENTERAS");
    }

    [Fact]
    public void Sobre_v1_omite_costoMedioM2_etiqueta_y_tipoCambio_en_MXN()
    {
        var sobre = AlmCompraSobre.Armar("ERP-OC-5521-R1", "COMPRA_RECIBIDA", DateTimeOffset.Parse("2026-10-07T09:30:00-06:00"), "OC-5521", AlmCompraSobre.DatosCompra(Datos()));
        using var doc = JsonDocument.Parse(sobre);
        var raiz = doc.RootElement;
        (raiz.GetProperty("mensajeId").GetString(), raiz.GetProperty("version").GetInt32(), raiz.GetProperty("origen").GetString()).Should().Be(("ERP-OC-5521-R1", 1, "ERP"));
        var datos = raiz.GetProperty("datos");
        datos.TryGetProperty("tipoCambio", out _).Should().BeFalse();
        var l = datos.GetProperty("lineas")[0];
        l.TryGetProperty("costoMedioM2", out _).Should().BeFalse();
        l.TryGetProperty("etiqueta", out _).Should().BeFalse();
        (l.GetProperty("costoM2").GetDecimal(), l.GetProperty("posicion").GetInt32(), l.GetProperty("requisicion").GetString()).Should().Be((210.5m, 1, "RQ-00000003"));
    }

    [Fact]
    public void Moneda_extranjera_manda_tipoCambio_y_la_modificada_identica_se_detecta()
    {
        var d = Datos("USD", 18.45m);
        var sobre = AlmCompraSobre.Armar("ERP-OC-5521-R1", "COMPRA_RECIBIDA", DateTimeOffset.UtcNow, "OC-5521", AlmCompraSobre.DatosCompra(d));
        JsonDocument.Parse(sobre).RootElement.GetProperty("datos").GetProperty("tipoCambio").GetDecimal().Should().Be(18.45m);
        AlmCompraSobre.TraeLosMismosDatos(sobre, d).Should().BeTrue();
        AlmCompraSobre.TraeLosMismosDatos(sobre, d with { Lineas = [d.Lineas[0] with { Pedimento = "26 47 3456 6009999" }] }).Should().BeFalse();
    }

    [Fact]
    public void Medida_girada_es_la_misma_hoja_y_se_agrupa_sin_tocar_el_material_por_cantidad()
    {
        var a = new AlmCompraLinea("1", null, null, 100006, null, 3302, 6100, 4, "L-7781", null, null, 210.5m, 4237.8m);
        var b = a with { LineaErp = "2", Ancho = 6100, Alto = 3302, Piezas = 6 };
        var otraMedida = a with { LineaErp = "3", Ancho = 3300, Alto = 6100 };
        var sinMedida1 = new AlmCompraLinea("4", null, null, 200010, null, null, null, 24, null, null, null, null, 25m);
        var sinMedida2 = sinMedida1 with { LineaErp = "5" };

        var r = AlmCompraSobre.AgruparMedidaGirada([a, b, otraMedida, sinMedida1, sinMedida2]);

        r.Should().HaveCount(4);
        r[0].Should().Match<AlmCompraLinea>(l => l.Ancho == 6100 && l.Alto == 3302 && l.Piezas == 10);
        r.Select(l => l.LineaErp).Should().Equal("1", "3", "4", "5");
    }

    [Fact]
    public void MensajeId_es_determinista_y_cabe_en_64()
    {
        AlmCompraSobre.MensajeId("OC-5521", Guid.Empty, "COMPRA_MODIFICADA", 2).Should().Be("ERP-OC-5521-M2");
        AlmCompraSobre.MensajeId(new string('X', 80), Guid.Empty, "COMPRA_CANCELADA", 1).Length.Should().BeLessThanOrEqualTo(64);
    }
}
