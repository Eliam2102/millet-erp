using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;

/// <summary>
/// M-327: reversa de un anticipo al agente aduanal ya comprobado. La solicita Comercio exterior con motivo y la autoriza el Contador
/// General (otra persona); el anticipo vuelve a «pendiente de comprobar» y todo lo anticipado queda como saldo a favor del agente.
/// </summary>
public sealed class M327ReversaComprobacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 17, 0, 0, TimeSpan.Zero);
    private static readonly Guid Comercio = Guid.NewGuid(), Contador = Guid.NewGuid(), Comprobador = Guid.NewGuid();

    /// <summary>Anticipo de $50,000 pagado y comprobado con una factura del agente por $42,000.</summary>
    private static (Expediente E, AnticipoAgente A, GastoExpediente Gasto) Comprobado()
    {
        var empresa = Guid.NewGuid(); var agente = Guid.NewGuid();
        var e = new Expediente(Guid.NewGuid(), empresa, "CE-M327", TipoExpediente.Importacion,
            Guid.NewGuid(), "M327", Guid.NewGuid(), Guid.NewGuid(), "MXN", Guid.NewGuid(), []);
        var a = e.RegistrarAnticipo(Guid.NewGuid(), agente, "26 24 3984 6001234", new(50000, 0, 0), new(2026, 10, 20), null);
        a.Solicitar(Guid.NewGuid(), 50000); a.RegistrarPago(Guid.NewGuid(), 50000, "SPEI-0001");
        var concepto = new ConceptoGasto(empresa, "M327-HON", "Honorarios del agente");
        var gastoId = Guid.NewGuid();
        e.GuardarGasto(gastoId, new(concepto.Id, agente, "A-1520", "MXN", 42000, new(2026, 10, 25), FacturaProveedorId: Guid.NewGuid()), concepto, 1, "MXN");
        var gasto = e.Gastos.Single(g => g.Id == gastoId);
        a.Comprobar(new(0, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 26), Comprobador); a.AplicarGastos([gasto]);
        return (e, a, gasto);
    }

    [Fact]
    public void CA1_solicitud_y_firma_del_contador_dejan_pendiente_de_comprobar_con_saldo_a_favor_de_50000()
    {
        var (_, a, gasto) = Comprobado();
        Assert.Equal(42000, a.Aplicado); Assert.Equal(8000, a.Saldo);
        var revision = a.RevisionComprobacion;

        a.SolicitarReversaComprobacion("  Facturas del agente aplicadas al pedimento equivocado ", Comercio, Ahora);
        Assert.Equal("Reversa de comprobación por autorizar", a.Estado);
        Assert.Equal(42000, a.Aplicado); // sin firma no cambia ningún saldo

        var reversa = a.AutorizarReversaComprobacion(Contador, Ahora.AddHours(1), [gasto]);

        Assert.NotNull(reversa);
        Assert.Equal("Pendiente de comprobar", a.Estado);
        Assert.Null(a.Aplicado); Assert.Null(a.Saldo); Assert.Null(a.FechaComprobacion);
        Assert.Equal(50000, a.SaldoAFavorAgenteMxn); Assert.Equal(50000, reversa.SaldoAFavorMxn);
        Assert.Equal(42000, reversa.GastosLiberadosMxn);
        Assert.Null(gasto.AnticipoAgenteId);
        Assert.Equal(revision + 1, a.RevisionComprobacion);
        Assert.Null(a.ReversaPendiente);
    }

    [Fact]
    public void CA2_sin_motivo_o_sin_autorizacion_se_rechaza_sin_tocar_saldos()
    {
        var (_, a, gasto) = Comprobado();
        Assert.Equal("CE_REVERSA_MOTIVO_REQUERIDO",
            Assert.Throws<BusinessRuleException>(() => a.SolicitarReversaComprobacion("   ", Comercio, Ahora)).Code);
        Assert.Null(a.ReversaPendiente);
        Assert.Equal("CE_REVERSA_SIN_SOLICITUD",
            Assert.Throws<BusinessRuleException>(() => a.AutorizarReversaComprobacion(Contador, Ahora, [gasto])).Code);
        Assert.Equal(42000, a.Aplicado); Assert.Equal(8000, a.Saldo); Assert.Equal(a.Id, gasto.AnticipoAgenteId);
        Assert.Equal(0, a.SaldoAFavorAgenteMxn);
    }

    [Fact]
    public void Quien_solicita_no_autoriza_su_propia_reversa()
    {
        var (_, a, gasto) = Comprobado();
        a.SolicitarReversaComprobacion("Comprobación duplicada", Comercio, Ahora);
        Assert.Equal("CE_REVERSA_MISMA_PERSONA",
            Assert.Throws<BusinessRuleException>(() => a.AutorizarReversaComprobacion(Comercio, Ahora, [gasto])).Code);
        Assert.Equal("CE_REVERSA_MISMA_PERSONA",
            Assert.Throws<BusinessRuleException>(() => a.RechazarReversaComprobacion(Comercio, Ahora, "No procede")).Code);
        Assert.Equal(42000, a.Aplicado); Assert.NotNull(a.ReversaPendiente);
    }

    [Fact]
    public void CA4_historial_con_usuario_fecha_motivo_y_firma_y_reversa_se_aplica_una_sola_vez()
    {
        var (_, a, gasto) = Comprobado();
        a.SolicitarReversaComprobacion("Comprobación duplicada", Comercio, Ahora);
        a.AutorizarReversaComprobacion(Contador, Ahora.AddHours(2), [gasto]);

        Assert.Null(a.AutorizarReversaComprobacion(Contador, Ahora.AddHours(3), [])); // idempotente
        var r = Assert.Single(a.ReversasComprobacion);
        Assert.True(r.Autorizada);
        Assert.Equal((Comercio, Ahora, "Comprobación duplicada"), (r.SolicitadoPor, r.SolicitadoEn, r.Motivo));
        Assert.Equal((Contador, Ahora.AddHours(2)), (r.FirmadoPor, r.FirmadoEn));
        Assert.Equal([gasto.Id], r.GastoIds); Assert.Equal(new DateOnly(2026, 10, 26), r.FechaComprobacionRevertida);
        Assert.Equal(50000, a.SaldoAFavorAgenteMxn);
        // El saldo ya está con el agente en Cuentas por pagar: no se vuelve a comprobar ni a revertir aquí.
        Assert.Equal("CE_ANTICIPO_REVERTIDO",
            Assert.Throws<BusinessRuleException>(() => a.Comprobar(new(0, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 10), Comprobador)).Code);
        Assert.Equal("CE_REVERSA_SIN_COMPROBACION",
            Assert.Throws<BusinessRuleException>(() => a.SolicitarReversaComprobacion("Otra vez", Comercio, Ahora)).Code);
    }

    [Fact]
    public void No_autorizada_conserva_la_comprobacion_y_queda_en_el_historial()
    {
        var (_, a, gasto) = Comprobado();
        a.SolicitarReversaComprobacion("Comprobación duplicada", Comercio, Ahora);
        Assert.Throws<BusinessRuleException>(() => a.Resolver("devolucion", 8000, new(2026, 11, 10), "Devolver", Guid.NewGuid(), Contador));
        Assert.Equal("CE_REVERSA_MOTIVO_REQUERIDO",
            Assert.Throws<BusinessRuleException>(() => a.RechazarReversaComprobacion(Contador, Ahora, "")).Code);

        var r = a.RechazarReversaComprobacion(Contador, Ahora.AddHours(1), "La comprobación es correcta");

        Assert.False(r.Autorizada); Assert.Equal("La comprobación es correcta", r.MotivoRechazo);
        Assert.Null(a.ReversaPendiente); Assert.Equal(42000, a.Aplicado); Assert.Equal(a.Id, gasto.AnticipoAgenteId);
        Assert.Equal(0, a.SaldoAFavorAgenteMxn); Assert.Equal("Saldo a favor abierto", a.Estado);
    }

    [Fact]
    public void Sin_comprobar_o_con_saldo_ya_resuelto_no_se_revierte()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "CE-M327", TipoExpediente.Importacion,
            Guid.NewGuid(), "M327", Guid.NewGuid(), Guid.NewGuid(), "MXN", Guid.NewGuid(), []);
        var sinComprobar = e.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "PED", new(50000, 0, 0), new(2026, 10, 1), null);
        Assert.Equal("CE_REVERSA_SIN_COMPROBACION",
            Assert.Throws<BusinessRuleException>(() => sinComprobar.SolicitarReversaComprobacion("Motivo", Comercio, Ahora)).Code);

        var (_, a, _) = Comprobado();
        a.Resolver("saldo-al-agente", 8000, new(2026, 11, 1), "Cuenta del agente", Guid.NewGuid(), Contador);
        Assert.Equal("CE_REVERSA_CON_RESOLUCION",
            Assert.Throws<BusinessRuleException>(() => a.SolicitarReversaComprobacion("Motivo", Comercio, Ahora)).Code);
    }

    [Fact]
    public void Expediente_no_queda_bloqueado_por_el_anticipo_revertido_y_avisa_la_reversa_pendiente()
    {
        var (e, a, gasto) = Comprobado();
        a.SolicitarReversaComprobacion("Comprobación duplicada", Comercio, Ahora);
        Assert.Contains("Reversa de comprobación por autorizar", e.MotivosFinancieros());
        a.AutorizarReversaComprobacion(Contador, Ahora, [gasto]);
        Assert.DoesNotContain("Anticipos sin comprobar", e.MotivosFinancieros());
        Assert.DoesNotContain("Reversa de comprobación por autorizar", e.MotivosFinancieros());
    }
}
