using Millet.Contabilidad.Application;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

public class DominioYOpcionesTests
{
    private static CuentaContable Nueva(NaturalezaCuenta? n = NaturalezaCuenta.Deudora, TipoCuenta? t = TipoCuenta.Afectable,
        CuentaControl c = CuentaControl.Ninguna, string codigo = "FIX-1", string nombre = "FIX") =>
        new(Guid.CreateVersion7(), codigo, nombre, null, 1, n, t, c, null, null);

    [Fact]
    public void Naturaleza_y_tipo_anulables_dejan_la_cuenta_pendiente_de_validacion()
    {
        Nueva().PendienteValidacion.Should().BeFalse();
        Nueva(n: null).PendienteValidacion.Should().BeTrue();
        Nueva(t: null).PendienteValidacion.Should().BeTrue();
        Nueva(n: null, t: null).PendienteValidacion.Should().BeTrue();
    }

    [Fact]
    public void Control_solo_en_afectable_explicito()
    {
        Nueva(c: CuentaControl.Clientes).CuentaControl.Should().Be(CuentaControl.Clientes);
        foreach (var tipo in new TipoCuenta?[] { TipoCuenta.Titulo, null })
            ((Action)(() => Nueva(t: tipo, c: CuentaControl.Proveedores))).Should().Throw<BusinessRuleException>()
                .Which.Code.Should().Be("CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE");
    }

    [Theory]
    [InlineData("", "FIX", "CONTAB_CUENTA_CODIGO_INVALIDO")]
    [InlineData("FIX-1", "", "CONTAB_CUENTA_NOMBRE_INVALIDO")]
    public void Invariantes_de_codigo_y_nombre(string codigo, string nombre, string esperado) =>
        ((Action)(() => Nueva(codigo: codigo, nombre: nombre))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be(esperado);

    [Fact]
    public void Baja_y_reactivacion_cambian_solo_el_estatus()
    {
        var c = Nueva();
        c.Desactivar();
        c.Activa.Should().BeFalse();
        c.Reactivar();
        c.Activa.Should().BeTrue();
    }

    [Fact]
    public void Defaults_son_validos_permisivos_y_genericos()
    {
        var o = CatalogoOpciones.Predeterminadas();
        o.Validar().Should().BeEmpty();
        o.NivelMaximo.Should().Be(10);
        o.HerenciaNaturaleza.Should().BeFalse();
        o.CuentasControl.Should().BeEmpty();
        o.Jerarquia.Modo.Should().Be(CatalogoOpciones.ModoPorSegmentos);
        o.Importacion.MaxFilas.Should().Be(5000);
    }

    [Fact]
    public void Configuracion_inconsistente_se_reporta_en_espanol_para_fallar_al_arrancar()
    {
        var o = CatalogoOpciones.Predeterminadas();
        o.Codigo.Patron = "([";
        o.Codigo.LongitudMax = 99;
        o.Jerarquia.Modo = "Otro";
        o.Naturaleza.Aliases["Mixta"] = ["m"];
        o.CuentasControl = [new() { Codigo = "FIX-1", Tipo = "Ninguna" }];
        var e = o.Validar().ToList();
        e.Should().Contain(x => x.Contains("Codigo.Patron")).And.Contain(x => x.Contains("LongitudMin/LongitudMax"))
            .And.Contain(x => x.Contains("Jerarquia.Modo")).And.Contain(x => x.Contains("Naturaleza.Aliases"))
            .And.Contain(x => x.Contains("CuentasControl"));
    }

    [Fact]
    public void Un_codigo_de_CuentasControl_fuera_del_patron_invalida_la_configuracion()
    {
        var o = CatalogoOpciones.Predeterminadas();
        o.CuentasControl = [new() { Codigo = "FIX 1", Tipo = "Clientes" }];
        o.Validar().Should().Contain(x => x.Contains("no cumple Codigo.Patron"));
    }
}
