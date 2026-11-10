using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Viaticos;

/// <summary>
/// M-449 (CA4): una comprobación de viáticos que excede la política queda «Fuera de política» por el excedente y pide la autorización de
/// Dirección de Finanzas antes de liberarse. Datos ficticios.
/// </summary>
public sealed class M449ComprobacionFueraPoliticaTests
{
    private static readonly Guid Empleado = Guid.NewGuid();
    private static readonly Guid Jefe = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);

    /// <summary>Viaje de 3 días a destino nacional con tope de 1,200/día (3,600), anticipado por 3,000.</summary>
    private static SolicitudViaticos Anticipada(decimal monto = 3_000m, decimal tope = 3_600m)
    {
        var s = SolicitudViaticos.Solicitar(Guid.NewGuid(), Empleado, Guid.NewGuid(), Jefe, "Monterrey", TipoDestinoViatico.Nacional,
            new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5), "MXN", monto, tope, 10,
            monto > tope ? "Feria comercial" : null, Ahora);
        s.AutorizarPorJefe(Jefe, Ahora);
        if (s.Estado == EstadoSolicitudViaticos.RequiereDireccionFinanzas) s.AutorizarPorDireccionFinanzas(Guid.NewGuid(), Ahora);
        s.MarcarAnticipoPagado(Ahora);
        return s;
    }

    private static void Comprobar(SolicitudViaticos s, params decimal[] totales) =>
        s.CapturarComprobacion(totales.Select(t => new LineaComprobacionViaticosInput(null, null, null, null, Ahora,
            t, 0m, 0m, t, "MXN", "Hospedaje y alimentos", true, "VIATICOS_SIN_CFDI")), Ahora);

    [Fact]
    public void CA4_comprobacion_de_4100_con_tope_de_1200_por_3_dias_queda_fuera_de_politica_por_500_y_no_se_libera()
    {
        var s = Anticipada();
        Comprobar(s, 2_500m, 1_600m);

        (s.ComprobacionFueraDePolitica, s.ExcesoComprobacion, s.LimiteComprobacion).Should().Be((true, 500m, 3_600m));
        var liberar = () => s.LiberarComprobacion(Ahora);
        liberar.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_COMPROBACION_FUERA_POLITICA" && e.Message.Contains("500.00"));
    }

    [Fact]
    public void Con_el_exceso_autorizado_por_otra_persona_se_libera()
    {
        var s = Anticipada();
        Comprobar(s, 4_100m);
        var df = Guid.NewGuid();

        s.AutorizarExcesoComprobacion(df, Ahora);
        s.LiberarComprobacion(Ahora);

        (s.Estado, s.ExcesoAutorizadoPor, s.MontoComprobado).Should().Be((EstadoSolicitudViaticos.Liquidada, df, 4_100m));
    }

    [Fact]
    public void El_empleado_no_autoriza_su_propio_exceso()
    {
        var s = Anticipada();
        Comprobar(s, 4_100m);

        var act = () => s.AutorizarExcesoComprobacion(Empleado, Ahora);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_EXCESO_AUTOAUTORIZACION");
    }

    [Fact]
    public void Dentro_de_politica_no_hay_exceso_que_autorizar_y_se_libera_directo()
    {
        var s = Anticipada();
        Comprobar(s, 3_600m);

        s.ComprobacionFueraDePolitica.Should().BeFalse();
        var autorizar = () => s.AutorizarExcesoComprobacion(Guid.NewGuid(), Ahora);
        autorizar.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_COMPROBACION_DENTRO_POLITICA");
        s.LiberarComprobacion(Ahora);
        s.Estado.Should().Be(EstadoSolicitudViaticos.Liquidada);
    }

    [Fact]
    public void Volver_a_capturar_recalcula_el_exceso_y_deja_sin_efecto_la_autorizacion()
    {
        var s = Anticipada();
        Comprobar(s, 4_100m);
        s.AutorizarExcesoComprobacion(Guid.NewGuid(), Ahora);

        Comprobar(s, 4_400m);

        (s.ExcesoComprobacion, s.ExcesoAutorizadoPor).Should().Be((800m, (Guid?)null));
        var liberar = () => s.LiberarComprobacion(Ahora);
        liberar.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_COMPROBACION_FUERA_POLITICA");
    }

    [Fact]
    public void Si_DF_autorizo_un_monto_mayor_al_solicitar_el_limite_es_ese_monto()
    {
        var s = Anticipada(monto: 5_000m);   // DF autorizó 5,000 con tope de 3,600
        Comprobar(s, 4_800m);

        (s.LimiteComprobacion, s.ComprobacionFueraDePolitica).Should().Be((5_000m, false));
    }

    [Fact]
    public void O2_al_capturar_el_monto_comprobado_ya_es_la_suma_y_la_diferencia_sigue_vacia()
    {
        var s = Anticipada();
        Comprobar(s, 2_500m, 1_600m);

        (s.Estado, s.MontoComprobado, s.DiferenciaLiquidacion)
            .Should().Be((EstadoSolicitudViaticos.ComprobacionCapturada, 4_100m, (decimal?)null));
    }

    [Fact]
    public void O2_volver_a_capturar_actualiza_el_monto_y_al_liberar_se_calcula_la_diferencia()
    {
        var s = Anticipada(tope: 5_000m);
        Comprobar(s, 4_100m);
        Comprobar(s, 4_300m);

        s.MontoComprobado.Should().Be(4_300m);
        s.LiberarComprobacion(Ahora);
        (s.Estado, s.MontoComprobado, s.DiferenciaLiquidacion)
            .Should().Be((EstadoSolicitudViaticos.Liquidada, 4_300m, 1_300m));
    }
}
