using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// CXC-06 (86e3qddef): la póliza del pago de cliente (#12) abona a clientes lo aplicado en cartera; la diferencia contra el depósito
/// solo entra como ajuste autorizado en CxC, a la cuenta de su tipo. Datos FIX.
/// </summary>
public class Cxc06AjusteCobroTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Cliente = Guid.NewGuid(), Propuesta = Guid.NewGuid();

    private static EventoContable Pago(decimal monto, decimal aplicado, Guid? propuesta = null, decimal saldoAFavor = 0m) =>
        N.Normalizar(N.PagoClienteConfirmado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, PropuestaId = propuesta ?? Propuesta, ClienteId = Cliente,
            MovimientoBancarioId = Guid.NewGuid(), CuentaBancariaId = Guid.NewGuid(), Monto = monto, Moneda = "MXN",
            FechaValor = new DateOnly(2026, 11, 3), Referencia = "SPEI-FIX", SaldoAFavorPorIdentificar = saldoAFavor,
            Facturas = new[] { new { FacturaVentaId = Guid.NewGuid(), ImporteAplicado = aplicado } },
        }));

    private sealed class PuertoFijo(AjusteCobroContable? ajuste) : IAjusteCobroContablePort
    {
        public Guid? Consultada { get; private set; }
        public Task<AjusteCobroContable?> ObtenerAsync(Guid empresaId, Guid propuestaId, CancellationToken ct)
        {
            Consultada = propuestaId;
            return Task.FromResult(ajuste);
        }

        public Task<ReversaAjusteCobroContable?> ObtenerReversaAsync(Guid empresaId, Guid propuestaId, CancellationToken ct) =>
            Task.FromResult<ReversaAjusteCobroContable?>(null);
    }

    [Fact]
    public void Pago_exacto_abona_a_clientes_el_deposito_sin_ajuste()
    {
        var e = Pago(10_000m, 10_000m);
        e.Importes["Total"].Should().Be(10_000m);
        e.Importes["Aplicado"].Should().Be(10_000m);
        e.Importes["Ajuste"].Should().Be(0m);
    }

    [Fact]
    public void Saldo_a_favor_no_es_ajuste()
    {
        var e = Pago(10_100m, 10_000m, saldoAFavor: 100m);
        e.Importes["Aplicado"].Should().Be(10_100m);
        e.Importes["Ajuste"].Should().Be(0m);
    }

    [Fact]
    public void Pago_corto_lleva_el_ajuste_y_abona_a_clientes_lo_aplicado()
    {
        var e = Pago(9_952m, 10_000m);
        e.Importes["Total"].Should().Be(9_952m);
        e.Importes["Aplicado"].Should().Be(10_000m);
        e.Importes["Ajuste"].Should().Be(48m);
    }

    [Fact]
    public async Task Ajuste_autorizado_como_comision_va_a_comisiones_bancarias()
    {
        var puerto = new PuertoFijo(new AjusteCobroContable(TipoAjusteCobroContable.ComisionBancaria, 48m, "Comisión", Guid.NewGuid(), DateTimeOffset.UtcNow));
        var e = await new EnriquecedorAjusteCobro(puerto).EnriquecerAsync(Pago(9_952m, 10_000m), default);

        puerto.Consultada.Should().Be(Propuesta);
        e.Importes["AjusteComision"].Should().Be(48m);
        e.Importes["AjusteDiferencia"].Should().Be(0m);
        // La póliza cuadra: Bancos + ajuste = clientes.
        (e.Importes["Total"]! + e.Importes["AjusteComision"]! + e.Importes["AjusteDiferencia"]!).Should().Be(e.Importes["Aplicado"]);
    }

    [Fact]
    public async Task Ajuste_autorizado_como_diferencia_menor_va_a_diferencias_en_cobro()
    {
        var puerto = new PuertoFijo(new AjusteCobroContable(TipoAjusteCobroContable.DiferenciaMenor, 5m, "Redondeo", Guid.NewGuid(), DateTimeOffset.UtcNow));
        var e = await new EnriquecedorAjusteCobro(puerto).EnriquecerAsync(Pago(9_995m, 10_000m), default);
        e.Importes["AjusteDiferencia"].Should().Be(5m);
        e.Importes["AjusteComision"].Should().Be(0m);
    }

    [Fact]
    public async Task Faltante_sin_ajuste_autorizado_no_se_absorbe()
    {
        var act = () => new EnriquecedorAjusteCobro(new PuertoFijo(null)).EnriquecerAsync(Pago(9_952m, 10_000m), default);
        (await act.Should().ThrowAsync<RechazoMotorException>())
            .Which.Should().Match<RechazoMotorException>(r => r.Causa == CausaRechazoContable.DatoFaltante && r.Detalle == "AjusteAutorizado");
    }

    [Fact]
    public async Task Sin_ajuste_no_consulta_a_CxC()
    {
        var puerto = new PuertoFijo(null);
        var e = await new EnriquecedorAjusteCobro(puerto).EnriquecerAsync(Pago(10_000m, 10_000m), default);
        puerto.Consultada.Should().BeNull();
        e.Importes["AjusteComision"].Should().Be(0m);
    }

    // ---------------------------------------------------------------- M-326: redondeo de hasta $0.99

    [Fact]
    public async Task M326_Redondeo_va_a_su_campo_y_la_poliza_cuadra()
    {
        var puerto = new PuertoFijo(new AjusteCobroContable(TipoAjusteCobroContable.Redondeo, 0.40m, "Redondeo de hasta $0.99", null, DateTimeOffset.UtcNow));
        var e = await new EnriquecedorAjusteCobro(puerto).EnriquecerAsync(Pago(9_999.60m, 10_000m), default);

        e.Importes["AjusteRedondeo"].Should().Be(0.40m);
        e.Importes["AjusteComision"].Should().Be(0m);
        e.Importes["AjusteDiferencia"].Should().Be(0m);
        (e.Importes["Total"]! + e.Importes["AjusteComision"]! + e.Importes["AjusteDiferencia"]! + e.Importes["AjusteRedondeo"]!)
            .Should().Be(e.Importes["Aplicado"]);
    }

    [Fact]
    public void M326_El_evento_trae_el_campo_de_redondeo_en_cero_si_no_hay_ajuste()
    {
        var e = Pago(10_000m, 10_000m);
        e.Importes["AjusteRedondeo"].Should().Be(0m);
    }

    [Fact]
    public async Task M326_Faltante_mayor_a_099_sin_ajuste_autorizado_no_se_absorbe_como_redondeo()
    {
        // 1.20 sin ajuste documentado: ni redondeo ni nada; el evento va a rechazos hasta que haya ajuste autorizado.
        var act = () => new EnriquecedorAjusteCobro(new PuertoFijo(null)).EnriquecerAsync(Pago(9_998.80m, 10_000m), default);
        (await act.Should().ThrowAsync<RechazoMotorException>()).Which.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
    }

    [Theory]
    [InlineData(TipoAjusteCobroContable.Redondeo, "REDONDEO")]
    [InlineData(TipoAjusteCobroContable.ComisionBancaria, "COMISIONES_BANCARIAS")]
    [InlineData(TipoAjusteCobroContable.DiferenciaMenor, "DIFERENCIAS_EN_COBRO")]
    public void M326_La_reversa_del_ajuste_usa_el_rol_de_su_tipo(TipoAjusteCobroContable tipo, string rol) =>
        ReversaAjusteCobroContableServicio.RolDe(tipo).Should().Be(rol);
}
