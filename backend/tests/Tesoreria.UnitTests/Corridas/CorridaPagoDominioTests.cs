using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.UnitTests.Corridas;

/// <summary>T9.1-A (86e3ke5bt): parámetro de firmas, umbral inclusivo y máquina de estados de la corrida.</summary>
public sealed class CorridaPagoDominioTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Armador = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    internal static PasivoPendientePago Pasivo(Guid? proveedor, decimal monto, decimal subtotal = 0, decimal traslados = 0, decimal retenciones = 0, string moneda = "MXN", Guid? empresa = null) =>
        new(empresa ?? Empresa, Guid.NewGuid(), proveedor ?? Guid.NewGuid(), null, monto, monto, moneda, null, new DateOnly(2026, 11, 15), null, "F-1", Ahora,
            "PUE", subtotal == 0 ? null : subtotal, subtotal == 0 ? null : traslados, subtotal == 0 ? null : retenciones);

    private static CorridaPago Corrida(params decimal[] montos)
    {
        var c = CorridaPago.Crear(Empresa, null, Guid.NewGuid(), "MXN", Armador, Ahora);
        foreach (var m in montos) c.AgregarLinea(Pasivo(null, m));
        return c;
    }

    // ---- Parámetro de firmas: umbral inclusivo (plano 7.2 paso 5), T-1 / T / T+1 ----

    [Theory]
    [InlineData("499999.99", 1)]
    [InlineData("500000.00", 2)]
    [InlineData("500000.01", 2)]
    public void Umbral_es_inclusivo_con_el_valor_demo(string total, int firmasEsperadas)
    {
        var (firmas, rol) = PoliticaFirmaCorrida.Requisito([PoliticaFirmaCorrida.Demo(Empresa)], decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture));
        firmas.Should().Be(firmasEsperadas);
        rol.Should().Be(firmasEsperadas == 2 ? "gerente" : null);
    }

    [Fact]
    public void Escalones_son_parametros_no_valores_fijos()
    {
        var escalones = new[]
        {
            new PoliticaFirmaCorrida(Empresa, "MXN", 100m, 2, null),
            new PoliticaFirmaCorrida(Empresa, "MXN", 1_000m, 3, "direccion"),
        };
        PoliticaFirmaCorrida.Requisito(escalones, 99.99m).Should().Be((1, null));
        PoliticaFirmaCorrida.Requisito(escalones, 100m).Should().Be((2, null));
        PoliticaFirmaCorrida.Requisito(escalones, 1_000m).Should().Be((3, "direccion"));
    }

    [Fact]
    public void Escalon_invalido_lanza()
    {
        var act = () => new PoliticaFirmaCorrida(Empresa, "MXN", 100m, 0, null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_POLITICA_INVALIDA");
    }

    // ---- Desglose bruto / retenciones / neto ----

    [Fact]
    public void Desglose_bruto_es_subtotal_mas_traslados_y_neto_es_el_total()
    {
        // subtotal 1,000 + IVA 160 - retención 106.67 = total 1,053.33
        var p = Pasivo(null, 1_053.33m, 1_000m, 160m, 106.67m);
        p.Desglose(1_053.33m).Should().Be((1_160m, 106.67m));
    }

    [Fact]
    public void Desglose_sin_datos_del_CFDI_no_inventa_retenciones()
    {
        Pasivo(null, 500m).Desglose(500m).Should().Be((500m, 0m));
    }

    [Fact]
    public void Linea_guarda_el_desglose_y_la_corrida_suma_el_neto()
    {
        var c = CorridaPago.Crear(Empresa, null, Guid.NewGuid(), "MXN", Armador, Ahora);
        c.AgregarLinea(Pasivo(null, 1_053.33m, 1_000m, 160m, 106.67m));
        c.Total.Should().Be(1_053.33m);
        c.Lineas.Single().Bruto.Should().Be(1_160m);
        c.Lineas.Single().Retenciones.Should().Be(106.67m);
    }

    // ---- Máquina de estados y firmas ----

    [Fact]
    public void Quien_arma_no_autoriza()
    {
        var c = Corrida(1_000m); c.EnviarAAutorizacion(1, null);
        var act = () => c.Firmar(Armador, [], Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_MISMO_USUARIO");
        c.Estado.Should().Be(EstadoCorridaPago.EnAutorizacion);
    }

    [Fact]
    public void Una_firma_basta_bajo_el_limite()
    {
        var c = Corrida(1_000m); c.EnviarAAutorizacion(1, null);
        c.Firmar(Guid.NewGuid(), ["jefe-tesoreria"], Ahora);
        c.Estado.Should().Be(EstadoCorridaPago.Autorizada);
    }

    [Fact]
    public void Sobre_el_limite_no_se_autoriza_con_una_sola_firma_ni_con_dos_sin_el_rol()
    {
        var c = Corrida(650_000m); c.EnviarAAutorizacion(2, "gerente");
        c.Firmar(Guid.NewGuid(), ["jefe-tesoreria"], Ahora);
        c.Estado.Should().Be(EstadoCorridaPago.EnAutorizacion);
        c.Firmar(Guid.NewGuid(), ["jefe-nomina"], Ahora);
        c.Estado.Should().Be(EstadoCorridaPago.EnAutorizacion, "faltan las firmas requeridas con el rol gerente");
        c.Firmar(Guid.NewGuid(), ["Gerente"], Ahora);
        c.Estado.Should().Be(EstadoCorridaPago.Autorizada);
    }

    [Fact]
    public void Segunda_firma_del_gerente_autoriza_al_llegar_a_dos()
    {
        var c = Corrida(650_000m); c.EnviarAAutorizacion(2, "gerente");
        c.Firmar(Guid.NewGuid(), ["jefe-tesoreria"], Ahora);
        c.Firmar(Guid.NewGuid(), ["gerente"], Ahora);
        c.Estado.Should().Be(EstadoCorridaPago.Autorizada);
        c.FirmasRecibidas.Should().Be(2);
    }

    [Fact]
    public void No_se_firma_dos_veces()
    {
        var c = Corrida(650_000m); c.EnviarAAutorizacion(2, null);
        var u = Guid.NewGuid();
        c.Firmar(u, [], Ahora);
        var act = () => c.Firmar(u, [], Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_YA_FIRMADA");
    }

    [Fact]
    public void Corrida_vacia_no_se_envia()
    {
        var act = () => Corrida().EnviarAAutorizacion(1, null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_SIN_LINEAS");
    }

    [Fact]
    public void Rechazar_y_cancelar_solo_desde_borrador_o_en_autorizacion_y_liberan_las_lineas()
    {
        var c = Corrida(1_000m); c.EnviarAAutorizacion(1, null);
        c.Rechazar("no procede", Guid.NewGuid());
        c.Estado.Should().Be(EstadoCorridaPago.Rechazada);
        c.Lineas.Should().OnlyContain(l => !l.Viva);

        var a = Corrida(1_000m); a.EnviarAAutorizacion(1, null); a.Firmar(Guid.NewGuid(), [], Ahora);
        ((Action)(() => a.Cancelar("x"))).Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_ESTADO_INVALIDO");
        ((Action)(() => a.Rechazar("x", Guid.NewGuid()))).Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_ESTADO_INVALIDO");
    }

    [Fact]
    public void Quien_arma_no_rechaza_pero_si_cancela()
    {
        var c = Corrida(1_000m);
        ((Action)(() => c.Rechazar("x", Armador))).Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_MISMO_USUARIO");
        c.Cancelar("error de captura");
        c.Estado.Should().Be(EstadoCorridaPago.Cancelada);
    }

    [Fact]
    public void Pasivo_retirado_sale_de_la_corrida_en_autorizacion_y_la_regresa_a_borrador_con_aviso()
    {
        var c = CorridaPago.Crear(Empresa, null, Guid.NewGuid(), "MXN", Armador, Ahora);
        var p1 = Pasivo(null, 400m); var p2 = Pasivo(null, 600m);
        c.AgregarLinea(p1); c.AgregarLinea(p2);
        c.EnviarAAutorizacion(2, null); c.Firmar(Guid.NewGuid(), [], Ahora);

        c.RetirarPasivo(p1.FacturaProveedorId, "revisión documental").Should().BeTrue();

        c.Lineas.Should().ContainSingle(l => l.FacturaProveedorId == p2.FacturaProveedorId);
        c.Total.Should().Be(600m);
        c.Estado.Should().Be(EstadoCorridaPago.Borrador);
        c.FirmasRecibidas.Should().Be(0);
        c.Avisos.Should().Contain(p1.FacturaProveedorId.ToString());
    }

    [Fact]
    public void Pasivo_retirado_en_corrida_autorizada_solo_avisa()
    {
        var c = CorridaPago.Crear(Empresa, null, Guid.NewGuid(), "MXN", Armador, Ahora);
        var p = Pasivo(null, 400m); c.AgregarLinea(p);
        c.EnviarAAutorizacion(1, null); c.Firmar(Guid.NewGuid(), [], Ahora);

        c.RetirarPasivo(p.FacturaProveedorId, "x").Should().BeTrue();

        c.Estado.Should().Be(EstadoCorridaPago.Autorizada);
        c.Lineas.Should().HaveCount(1);
        c.Avisos.Should().NotBeNull();
    }

    [Fact]
    public void Moneda_distinta_o_corrida_no_borrador_no_admite_pasivos()
    {
        var c = Corrida(1_000m);
        ((Action)(() => c.AgregarLinea(Pasivo(null, 10m, moneda: "USD")))).Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_MONEDA_DISTINTA");
        c.EnviarAAutorizacion(1, null);
        ((Action)(() => c.AgregarLinea(Pasivo(null, 10m)))).Should().Throw<BusinessRuleException>().Where(e => e.Code == "CORRIDA_ESTADO_INVALIDO");
    }
}
