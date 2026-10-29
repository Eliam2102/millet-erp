using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Compartido.UnitTests;

/// <summary>86e3qcgyn: reglas de unificación de clientes que comparten RFC (propuesta pendiente de validar con Millet).</summary>
public sealed class RfcRepetidosTests
{
    private static Cliente Nuevo(string clave) => new(Guid.NewGuid(), clave, "CLIENTE " + clave, OrigenMaster.Aw, referenciaExterna: clave);
    private static readonly DateTimeOffset Ahora = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fusionar_sin_historial_deja_inactivo_y_apunta_al_principal()
    {
        var p = Nuevo("1"); var s = Nuevo("2");
        s.UnificarEn(p, conHistorial: false, "mismo cliente duplicado", "ana", Ahora);
        Assert.Equal(p.Id, s.UnificadoEnClienteId);
        Assert.Equal(EstatusCatalogo.Inactivo, s.Estatus);
        Assert.Equal("ana", s.UnificadoPor);
        Assert.Equal(EstatusCatalogo.Activo, p.Estatus);
    }

    [Fact]
    public void Agrupar_con_historial_sigue_activo()
    {
        var p = Nuevo("1"); var s = Nuevo("2");
        s.UnificarEn(p, conHistorial: true, "mismo cliente duplicado", "ana", Ahora);
        Assert.Equal(EstatusCatalogo.Activo, s.Estatus);
        Assert.Equal(p.Id, s.UnificadoEnClienteId);
    }

    [Fact]
    public void No_se_unifica_en_si_mismo_ni_en_un_principal_unificado_ni_dos_veces()
    {
        var a = Nuevo("1"); var b = Nuevo("2"); var c = Nuevo("3");
        Assert.Equal("CLIENTE_UNIFICAR_A_SI_MISMO", Assert.Throws<BusinessRuleException>(() => a.UnificarEn(a, false, "motivo", "x", Ahora)).Code);
        b.UnificarEn(a, false, "motivo", "x", Ahora);
        Assert.Equal("CLIENTE_PRINCIPAL_UNIFICADO", Assert.Throws<BusinessRuleException>(() => c.UnificarEn(b, false, "motivo", "x", Ahora)).Code);
        Assert.Equal("CLIENTE_YA_UNIFICADO", Assert.Throws<BusinessRuleException>(() => b.UnificarEn(c, false, "motivo", "x", Ahora)).Code);
    }

    [Fact]
    public void Un_fusionado_no_se_reactiva_ni_por_a_w_ni_manualmente_pero_un_agrupado_si_sigue_el_bloqueo()
    {
        var p = Nuevo("1"); var f = Nuevo("2"); var g = Nuevo("3");
        f.AplicarBloqueoAw(true);
        f.UnificarEn(p, false, "motivo", "x", Ahora);
        f.AplicarBloqueoAw(false);
        Assert.Equal(EstatusCatalogo.Inactivo, f.Estatus);
        Assert.Equal("CLIENTE_FUSIONADO", Assert.Throws<BusinessRuleException>(() => f.CambiarEstatus(EstatusCatalogo.Activo)).Code);

        g.UnificarEn(p, true, "motivo", "x", Ahora);
        g.AplicarBloqueoAw(true);
        Assert.Equal(EstatusCatalogo.Inactivo, g.Estatus);
        g.AplicarBloqueoAw(false);
        Assert.Equal(EstatusCatalogo.Activo, g.Estatus);
    }

    private static MiembroRfcRepetido M(string r, bool rfc = false, int aw = 0, DateTime? ultimo = null, int erp = 0, bool unificado = false, int cot = 0) =>
        new(Guid.NewGuid(), r, rfc, unificado, aw, ultimo, erp, cot);

    [Fact]
    public void Principal_sugerido_sigue_el_orden_rfc_en_erp_pedido_aw_reciente_mas_pedidos_erp_referencia_baja()
    {
        var a = M("10", aw: 3, ultimo: new DateTime(2026, 1, 1)); var b = M("9", rfc: true); var c = M("11", aw: 1, ultimo: new DateTime(2026, 5, 1), erp: 9);
        Assert.Equal((b.Id, "Ya tiene el RFC en el ERP."), RfcRepetidosReglas.SugerirPrincipal([a, b, c]));
        Assert.Equal((c.Id, "Tiene el pedido más reciente en A+W."), RfcRepetidosReglas.SugerirPrincipal([a, c]));
        var d = M("20", erp: 1); var e = M("21", erp: 4);
        Assert.Equal((e.Id, "Tiene más pedidos en el ERP."), RfcRepetidosReglas.SugerirPrincipal([d, e]));
        var f = M("100"); var g = M("20"); var manual = M(null!);
        Assert.Equal((g.Id, "Es la referencia más baja de A+W."), RfcRepetidosReglas.SugerirPrincipal([f, manual, g]));
    }

    [Fact]
    public void Principal_sugerido_ignora_unificados_y_la_accion_depende_del_historial()
    {
        var p = M("1"); var u = M("2", unificado: true);
        Assert.Equal(p.Id, RfcRepetidosReglas.SugerirPrincipal([u, p]).Id);
        var sin = M("3"); var conAw = M("4", aw: 1); var conErp = M("5", erp: 2);
        Assert.Equal("Principal", RfcRepetidosReglas.AccionSugerida(p, p.Id));
        Assert.Equal("Fusionar", RfcRepetidosReglas.AccionSugerida(sin, p.Id));
        Assert.Equal("Agrupar", RfcRepetidosReglas.AccionSugerida(conAw, p.Id));
        Assert.Equal("Agrupar", RfcRepetidosReglas.AccionSugerida(conErp, p.Id));
    }

    [Fact]
    public void Una_cotizacion_en_a_w_cuenta_como_historial()
    {
        var p = M("1"); var soloCotizacion = M("2", cot: 1);
        Assert.True(RfcRepetidosReglas.ConHistorial(soloCotizacion));
        Assert.Equal("Agrupar", RfcRepetidosReglas.AccionSugerida(soloCotizacion, p.Id));
    }
}
