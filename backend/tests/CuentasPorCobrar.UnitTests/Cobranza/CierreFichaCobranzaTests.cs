using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Cobranza;

/// <summary>M-345 (CXC-GESTION/s06): la ficha no cierra con saldo sin explicar. Datos genéricos.</summary>
public sealed class CierreFichaCobranzaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Usuario = Guid.NewGuid();

    private static FichaCobranza NuevaFicha() =>
        FichaCobranza.Abrir(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Usuario, "Cobrador", null, Usuario, "Cobrador", Ahora).Ficha;

    [Fact]
    public void Con_saldo_sin_explicar_no_cierra()
    {
        var ficha = NuevaFicha();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            ficha.Cerrar(MotivoCierreFicha.AcuerdoDePago, 2000m, "  ", Usuario, "Cobrador", Ahora));
        Assert.Equal("FCO_SALDO_SIN_EXPLICAR", ex.Code);
        Assert.False(ficha.Cerrada);
    }

    [Fact]
    public void Con_saldo_explicado_cierra_y_guarda_motivo_saldo_y_explicacion()
    {
        var ficha = NuevaFicha();
        var evento = ficha.Cerrar(MotivoCierreFicha.AcuerdoDePago, 2000m, " Convenio en 2 pagos ", Usuario, "Cobrador", Ahora);
        Assert.True(ficha.Cerrada);
        Assert.Equal(2000m, ficha.SaldoAlCerrar);
        Assert.Equal("Convenio en 2 pagos", ficha.ExplicacionSaldo);
        Assert.Equal(TipoEventoFicha.Cierre, evento.Tipo);
    }

    [Fact]
    public void Pagada_solo_vale_sin_saldo_y_sin_saldo_solo_se_cierra_como_pagada()
    {
        Assert.Equal("FCO_PAGADA_CON_SALDO",
            Assert.Throws<BusinessRuleException>(() => NuevaFicha().Cerrar(MotivoCierreFicha.Pagada, 1m, "x", Usuario, null, Ahora)).Code);
        Assert.Equal("FCO_CIERRE_SIN_SALDO",
            Assert.Throws<BusinessRuleException>(() => NuevaFicha().Cerrar(MotivoCierreFicha.Disputa, 0m, null, Usuario, null, Ahora)).Code);
        var ficha = NuevaFicha();
        ficha.Cerrar(MotivoCierreFicha.Pagada, 0m, null, Usuario, null, Ahora);
        Assert.True(ficha.Cerrada);
    }

    [Fact]
    public void Ficha_cerrada_no_admite_gestion_ni_segundo_cierre()
    {
        var ficha = NuevaFicha();
        ficha.Cerrar(MotivoCierreFicha.Pagada, 0m, null, Usuario, null, Ahora);
        Assert.Equal("FCO_FICHA_CERRADA", Assert.Throws<BusinessRuleException>(() =>
            ficha.RegistrarGestion(CanalCobranza.Llamada, ResultadoCobranza.PromesaPago, "x", 10m, new DateOnly(2026, 11, 20), null, Usuario, null, Ahora)).Code);
        Assert.Equal("FCO_FICHA_CERRADA", Assert.Throws<BusinessRuleException>(() =>
            ficha.Cerrar(MotivoCierreFicha.Pagada, 0m, null, Usuario, null, Ahora)).Code);
    }
}
