using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-207 (86e3zr7nf): el TC de un CFDI en moneda extranjera se compara contra el catálogo común; fuera de tolerancia va a
/// Rechazos, donde se captura un TC sustituto que autoriza otra persona. Valores de la ficha (FIX 18.4520, tolerancia 1 %).
/// </summary>
public class TipoCambioCfdiFuenteComunTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static EventoContable FacturaUsd(decimal? tc) =>
        N.Normalizar(N.PagoProveedorAplicado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, FacturaProveedorId = Guid.NewGuid(), PagoId = Guid.NewGuid(),
            Monto = 1000m, Moneda = "USD", FechaPago = new DateOnly(2026, 11, 13), ReferenciaBancaria = "DEMO", TipoCambio = 18m,
        })) with { Tipo = N.FacturaProveedorRegistrada, Moneda = "USD", TipoCambio = tc };

    [Fact]
    public void CA1_fuera_de_tolerancia_rechaza_con_los_dos_TC_y_la_diferencia()
    {
        var act = () => GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(18.95m), true, null, null, 18.4520m, 1.00m);

        var ex = act.Should().Throw<RechazoMotorException>().Which;
        ex.Causa.Should().Be(CausaRechazoContable.TipoCambioFueraDeTolerancia);
        ex.Detalle.Should().Be("18.9500|18.4520");
        ex.Message.Should().Contain("TC del documento 18.9500, catálogo 18.4520 (2.70 %)");
    }

    [Fact]
    public void CA2_dentro_de_tolerancia_usa_el_TC_del_documento()
    {
        GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(18.50m), true, null, null, 18.4520m, 1.00m).Should().Be(18.50m);
        GenerarPolizaDesdeEventoHandler.DiferenciaPct(18.50m, 18.4520m).Should().Be(0.26m);
    }

    [Fact]
    public void Sin_TC_en_el_documento_usa_el_del_catalogo_y_sin_ninguno_rechaza_con_causa_propia()
    {
        GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(null), true, null, null, 18.4520m, 1.00m).Should().Be(18.4520m);

        var act = () => GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(null), true, null, null, null, 1.00m);
        act.Should().Throw<RechazoMotorException>().Which.Causa.Should().Be(CausaRechazoContable.TipoCambioFaltante);
    }

    [Fact]
    public void Catalogo_sin_valor_o_tolerancia_cero_no_bloquea_el_TC_del_documento()
    {
        GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(18.95m), true, null, null, null, 1.00m).Should().Be(18.95m);
        GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(18.95m), true, null, null, 18.4520m, 0m).Should().Be(18.95m);
    }

    [Fact]
    public void Eventos_que_no_son_de_un_CFDI_no_se_comparan_y_conservan_la_regla_anterior()
    {
        GenerarPolizaDesdeEventoHandler.EventosConTcDeCfdi.Should().Contain(N.FacturaProveedorRegistrada).And.Contain(N.FacturaVentaTimbrada)
            .And.NotContain(N.PagoProveedorAplicado).And.NotContain(N.PagoClienteConfirmado).And.NotContain(N.RevaluacionCierreRegistrada);
        GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(18.95m), false, null, null, 18.4520m, 1.00m).Should().Be(18.95m);
        var act = () => GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(null), false, null, null, 18.4520m, 1.00m);
        act.Should().Throw<RechazoMotorException>().Which.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
    }

    [Fact]
    public void CA4_con_sustituto_autorizado_usa_ese_valor_sin_comparar()
    {
        GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(18.95m), true, AccionTipoCambioRechazo.Sustituto, 18.4600m, 18.4520m, 1.00m)
            .Should().Be(18.4600m);
        GenerarPolizaDesdeEventoHandler.ResolverTipoCambio(FacturaUsd(18.95m), true, AccionTipoCambioRechazo.AceptarDocumento, null, 18.4520m, 1.00m)
            .Should().Be(18.95m);
    }

    // ---- Dominio del rechazo ----

    private static RechazoContable RechazoTc(CausaRechazoContable causa = CausaRechazoContable.TipoCambioFueraDeTolerancia) =>
        RechazoContable.Crear(Empresa, N.FacturaProveedorRegistrada, Guid.NewGuid(), "{}", causa, "18.9500|18.4520", "fuera", DateTimeOffset.UtcNow);

    private static readonly Guid Analista = Guid.NewGuid(), Contador = Guid.NewGuid();

    [Fact]
    public void Captura_exige_motivo_y_valor_valido()
    {
        var r = RechazoTc();
        var corto = () => r.CapturarTipoCambio(AccionTipoCambioRechazo.Sustituto, 18.46m, "FIX", Analista, "Analista", DateTimeOffset.UtcNow);
        corto.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECHAZO_MOTIVO");
        var sinValor = () => r.CapturarTipoCambio(AccionTipoCambioRechazo.Sustituto, null, "FIX DOF del día", Analista, "Analista", DateTimeOffset.UtcNow);
        sinValor.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_TC_SUSTITUTO_INVALIDO");
        var aceptarSinDoc = () => RechazoTc(CausaRechazoContable.TipoCambioFaltante)
            .CapturarTipoCambio(AccionTipoCambioRechazo.AceptarDocumento, null, "Usar el del documento", Analista, "Analista", DateTimeOffset.UtcNow);
        aceptarSinDoc.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_TC_SIN_DOCUMENTO");
    }

    [Fact]
    public void CA3_y_CA5_capturado_queda_por_autorizar_y_no_lo_autoriza_quien_lo_capturo()
    {
        var r = RechazoTc();
        r.CapturarTipoCambio(AccionTipoCambioRechazo.Sustituto, 18.4600m, "FIX DOF del 13-nov", Analista, "Analista", DateTimeOffset.UtcNow);

        r.TcPorAutorizar.Should().BeTrue();
        r.TcAutorizado.Should().BeFalse();
        var mismo = () => r.AutorizarTipoCambio(Analista, "Analista", DateTimeOffset.UtcNow);
        mismo.Should().Throw<BusinessRuleException>().Which.Message.Should().Be("Debe autorizar otra persona.");

        r.AutorizarTipoCambio(Contador, "Contador General", DateTimeOffset.UtcNow);
        (r.TcAutorizado, r.TcValor, r.TcAutorizadoPor).Should().Be((true, 18.4600m, "Contador General"));
        r.Estado.Should().Be(EstadoRechazoContable.Pendiente);
    }

    [Fact]
    public void Recapturar_anula_la_autorizacion_y_un_rechazo_que_no_es_de_TC_no_admite_captura()
    {
        var r = RechazoTc();
        r.CapturarTipoCambio(AccionTipoCambioRechazo.Sustituto, 18.4600m, "FIX DOF del 13-nov", Analista, "Analista", DateTimeOffset.UtcNow);
        r.AutorizarTipoCambio(Contador, "Contador General", DateTimeOffset.UtcNow);
        r.CapturarTipoCambio(AccionTipoCambioRechazo.AceptarDocumento, null, "Se acepta el del proveedor", Analista, "Analista", DateTimeOffset.UtcNow);
        r.TcPorAutorizar.Should().BeTrue();

        var otro = RechazoContable.Crear(Empresa, N.FacturaProveedorRegistrada, Guid.NewGuid(), "{}", CausaRechazoContable.RolSinCuenta, null, "x", DateTimeOffset.UtcNow);
        var act = () => otro.CapturarTipoCambio(AccionTipoCambioRechazo.Sustituto, 18.46m, "FIX DOF del día", Analista, "Analista", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_RECHAZO_NO_ES_TC");
    }
}
