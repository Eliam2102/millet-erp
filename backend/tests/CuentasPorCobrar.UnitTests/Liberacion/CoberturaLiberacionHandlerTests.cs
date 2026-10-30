using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

/// <summary>
/// C7.3 (ClickUp 86e3jvv6n) — cobertura 50–99 % → pendiente de
/// autorización de CxC. Cobertura = crédito DISPONIBLE ÷ monto del pedido.
/// <c>liberado_sin_factura</c> sigue en 0 (dependencia C7.1), así que el
/// disponible de los criterios se simula con facturas abiertas.
/// </summary>
public sealed class CoberturaLiberacionHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Coordinador = Guid.NewGuid(); // registra la decisión
    private static readonly Guid Nestor = Guid.NewGuid();      // gerente CxC que autoriza
    private static readonly Guid ClienteId = Guid.NewGuid();
    private const string RfcCliente = "VGL860910IU4";

    // ------------------------------------------------ CA7.2

    [Fact]
    public async Task CA7_2_cobertura_66_queda_pendiente_de_autorizacion()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);

        var (res, eventos) = await Decidir(db, Comando("9000101", 60_000m));

        res.Resultado.Should().Be(ResultadoLiberacion.PendienteAutorizacion);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Cobertura);
        res.PorcentajeCobertura.Should().Be(66.66m); // supuesto (b): trunca
        res.CreditoDisponibleSnapshot.Should().Be(40_000m);
        res.OverrideId.Should().BeNull();
        eventos.OfType<DecisionLiberacionEmitidaEvent>().Single()
            .Should().Match<DecisionLiberacionEmitidaEvent>(e =>
                e.Resultado == "PendienteAutorizacion" && e.PorcentajeCobertura == 66.66m);
    }

    [Fact]
    public async Task CA7_2_Nestor_autoriza_con_motivo_y_se_libera_con_override_ligado_a_la_pendiente()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000102", 60_000m));

        var (res, eventos) = await Autorizar(db, pendiente.Id, "Pago en tránsito confirmado", Nestor);

        res.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Override);
        res.DecisionOrigenId.Should().Be(pendiente.Id);
        res.PorcentajeCobertura.Should().Be(66.66m);
        res.PedidoRef.Should().Be("9000102");
        res.DecididoPor.Should().Be(Nestor);
        res.OverrideId.Should().NotBeNull();
        res.Motivo.Should().Be("Pago en tránsito confirmado");

        var autorizacion = await db.AutorizacionesCredito.SingleAsync();
        autorizacion.Id.Should().Be(res.OverrideId!.Value);
        autorizacion.Estado.Should().Be(EstadoAutorizacionCredito.Usada);
        autorizacion.SupervisorUsuarioId.Should().Be(Nestor);
        autorizacion.BeneficiarioUsuarioId.Should().Be(Coordinador);
        autorizacion.ClienteOPedidoRef.Should().Be("9000102");
        autorizacion.Motivo.Should().Be("Pago en tránsito confirmado");
        autorizacion.DecisionLiberacionId.Should().Be(res.Id);

        // La pendiente es inmutable: sigue registrada tal cual.
        (await db.DecisionesLiberacion.SingleAsync(d => d.Id == pendiente.Id)).Resultado
            .Should().Be(ResultadoLiberacion.PendienteAutorizacion);

        eventos.OfType<DecisionLiberacionEmitidaEvent>().Single()
            .Should().Match<DecisionLiberacionEmitidaEvent>(e =>
                e.Resultado == "LiberadoConOverride"
                && e.DecisionOrigenId == pendiente.Id
                && e.PorcentajeCobertura == 66.66m);
    }

    [Fact]
    public async Task CA7_2_segunda_autorizacion_de_la_misma_pendiente_es_conflicto()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000103", 60_000m));
        await Autorizar(db, pendiente.Id, "Primera", Nestor);

        var act = () => Autorizar(db, pendiente.Id, "Segunda", Guid.NewGuid());

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == "DL_PENDIENTE_YA_RESUELTA");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(2);
        (await db.AutorizacionesCredito.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Listar_marca_la_pendiente_resuelta_con_su_resolucion()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000104", 60_000m));
        var (otra, _) = await Decidir(db, Comando("9000105", 60_000m));
        var (resolucion, _) = await Autorizar(db, pendiente.Id, "Ok", Nestor);

        var page = await new ListarDecisionesLiberacionHandler(db).Handle(
            new ListarDecisionesLiberacionQuery(Resultado: ResultadoLiberacion.PendienteAutorizacion),
            CancellationToken.None);

        page.Items.Should().HaveCount(2);
        page.Items.Single(d => d.Id == pendiente.Id).ResolucionId.Should().Be(resolucion.Id);
        page.Items.Single(d => d.Id == otra.Id).ResolucionId.Should().BeNull();
    }

    [Fact]
    public async Task Pendiente_superada_por_una_decision_posterior_del_mismo_pedido_es_conflicto()
    {
        // Supuesto (c): la pendiente no vence, pero si el crédito cambia se
        // decide de nuevo; la nueva decisión deja sin efecto a la anterior.
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000106", 60_000m));
        await Decidir(db, Comando("9000106", 60_000m), ahora: Ahora.AddHours(2));

        var act = () => Autorizar(db, pendiente.Id, "Tarde", Nestor, ahora: Ahora.AddHours(3));

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == "DL_PENDIENTE_SUPERADA");
    }

    [Fact]
    public async Task Pendiente_con_override_vigente_del_mismo_pedido_se_libera_al_decidir()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000107");

        var (res, _) = await Decidir(db, Comando("9000107", 60_000m, aut.Id));

        res.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        res.OverrideId.Should().Be(aut.Id);
        res.PorcentajeCobertura.Should().Be(66.66m);
        res.DecisionOrigenId.Should().BeNull();
    }

    // ------------------------------------------------ CA7.3

    [Fact]
    public async Task CA7_3_cobertura_30_queda_retenido_sin_ofrecer_autorizacion()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 30_000m);

        var (res, _) = await Decidir(db, Comando("9000201", 100_000m));

        res.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        res.PorcentajeCobertura.Should().Be(30m);
    }

    [Fact]
    public async Task CA7_3_override_con_cobertura_menor_a_50_se_rechaza_y_no_se_consume()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 30_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000202");

        var act = () => Decidir(db, Comando("9000202", 100_000m, aut.Id));

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "CXC_COBERTURA_INSUFICIENTE");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(0);
        (await db.AutorizacionesCredito.SingleAsync()).Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    [Fact]
    public async Task CA7_3_no_se_puede_autorizar_una_decision_retenida()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 30_000m);
        var (retenida, _) = await Decidir(db, Comando("9000203", 100_000m));

        var act = () => Autorizar(db, retenida.Id, "Intento", Nestor);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_NO_PENDIENTE");
    }

    [Fact]
    public async Task Supuesto_d_serie_que_exige_autorizacion_sigue_aceptando_override_con_cobertura_baja()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 30_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "3000204");

        var (res, _) = await Decidir(db, Comando("3000204", 100_000m, aut.Id));

        res.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Override);
    }

    [Fact]
    public async Task Supuesto_e_cliente_sin_linea_queda_retenido_sin_porcentaje()
    {
        using var db = CrearDbContext();

        var (res, _) = await Decidir(db, Comando("9000205", 10_000m));

        res.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        res.PorcentajeCobertura.Should().BeNull();
    }

    // ------------------------------------------------ CA7.6 / C7.3-a

    [Fact]
    public async Task CA7_6_autorizacion_ya_usada_se_rechaza()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000301");
        await Decidir(db, Comando("9000301", 60_000m, aut.Id));

        var act = () => Decidir(db, Comando("9000301", 60_000m, aut.Id));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_NO_DISPONIBLE");
    }

    [Fact]
    public async Task CA7_6_quien_otorgo_la_autorizacion_no_puede_usarla_para_un_pedido_propio()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000302", supervisor: Nestor);

        var act = () => Decidir(db, Comando("9000302", 60_000m, aut.Id), usuario: Nestor);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_BENEFICIARIO_DISTINTO");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CA7_6_quien_registro_la_pendiente_no_puede_autorizarla()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000303", 60_000m));

        var act = () => Autorizar(db, pendiente.Id, "Me autorizo", Coordinador);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_AUTOCONSUMO");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(1);
        (await db.AutorizacionesCredito.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task C7_3_a_autorizacion_vencida_se_rechaza()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000304");

        var act = () => Decidir(db, Comando("9000304", 60_000m, aut.Id), ahora: Ahora.AddHours(25));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_VENCIDA");
    }

    // ------------------------------------------------ Referencia del override (punto 4)

    [Fact]
    public async Task Override_de_otro_pedido_se_rechaza()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000999");

        var act = () => Decidir(db, Comando("9000401", 60_000m, aut.Id));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_REFERENCIA_DISTINTA");
        (await db.AutorizacionesCredito.SingleAsync()).Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    [Theory]
    [InlineData(RfcCliente)]
    [InlineData("VIDRIOS DEL GOLFO SA DE CV")]
    public async Task E10_override_otorgado_al_cliente_por_RFC_o_razon_social_ya_no_aplica(string referencia)
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: referencia);

        var act = () => Decidir(db, Comando("9000402", 60_000m, aut.Id));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_REFERENCIA_DISTINTA");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(0);
        (await db.AutorizacionesCredito.SingleAsync()).Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    [Fact]
    public async Task E10_override_con_el_folio_exacto_del_pedido_aplica()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000403");

        var (res, _) = await Decidir(db, Comando(" 9000403 ", 60_000m, aut.Id));

        res.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        (await db.AutorizacionesCredito.SingleAsync()).Estado.Should().Be(EstadoAutorizacionCredito.Usada);
    }

    // ------------------------------------------------ Supuesto (e): sin línea / bloqueada

    [Fact]
    public async Task Supuesto_e_sin_linea_con_override_se_rechaza_sin_consumir()
    {
        using var db = CrearDbContext();
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000601");

        var act = () => Decidir(db, Comando("9000601", 10_000m, aut.Id));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXC_SIN_LINEA_ACTIVA");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(0);
        (await db.AutorizacionesCredito.SingleAsync()).Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    [Fact]
    public async Task Supuesto_e_linea_bloqueada_con_override_se_rechaza_sin_consumir()
    {
        using var db = CrearDbContext();
        var linea = await LineaConDisponible(db, limite: 100_000m, disponible: 80_000m);
        linea.Bloquear("Cartera vencida");
        await db.SaveChangesAsync();
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "9000602");

        var act = () => Decidir(db, Comando("9000602", 100_000m, aut.Id));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXC_SIN_LINEA_ACTIVA");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(0);
        (await db.AutorizacionesCredito.SingleAsync()).Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    [Fact]
    public async Task Supuesto_e_linea_bloqueada_sin_override_queda_retenido_por_credito()
    {
        using var db = CrearDbContext();
        var linea = await LineaConDisponible(db, limite: 100_000m, disponible: 80_000m);
        linea.Bloquear("Cartera vencida");
        await db.SaveChangesAsync();

        var (res, _) = await Decidir(db, Comando("9000603", 100_000m));

        res.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        res.PorcentajeCobertura.Should().BeNull();
    }

    [Fact]
    public async Task Serie_NuncaLibera_sin_linea_con_override_sigue_liberando()
    {
        using var db = CrearDbContext();
        var aut = await AgregarAutorizacion(db, beneficiario: Coordinador, referencia: "3000604");

        var (res, _) = await Decidir(db, Comando("3000604", 10_000m, aut.Id));

        res.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Override);
    }

    // ------------------------------------------------ Autorizar: recálculo del crédito

    [Fact]
    public async Task Autorizar_si_el_credito_ya_alcanza_libera_por_credito_sin_autorizacion()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000701", 60_000m));
        db.FacturasCartera.RemoveRange(db.FacturasCartera); // entró el pago
        await db.SaveChangesAsync();

        var (res, eventos) = await Autorizar(db, pendiente.Id, "Ya pagó", Nestor, Ahora.AddHours(1));

        res.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        res.OverrideId.Should().BeNull();
        res.CreditoDisponibleSnapshot.Should().Be(100_000m);
        res.PorcentajeCobertura.Should().Be(100m);
        res.DecisionOrigenId.Should().Be(pendiente.Id);
        res.Motivo.Should().Be("Ya pagó");
        (await db.AutorizacionesCredito.CountAsync()).Should().Be(0);
        eventos.OfType<DecisionLiberacionEmitidaEvent>().Single().Motivo.Should().Be("Ya pagó");
    }

    [Fact]
    public async Task Autorizar_si_la_cobertura_bajo_de_50_queda_retenido()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000702", 60_000m));
        await AgregarFacturaAbierta(db, 20_000m); // disponible 20,000 → 33.33 %

        var (res, _) = await Autorizar(db, pendiente.Id, "Intento", Nestor, Ahora.AddHours(1));

        res.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        res.PorcentajeCobertura.Should().Be(33.33m);
        res.CreditoDisponibleSnapshot.Should().Be(20_000m);
        res.DecisionOrigenId.Should().Be(pendiente.Id);
        res.OverrideId.Should().BeNull();
        (await db.AutorizacionesCredito.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Autorizar_con_linea_bloqueada_queda_retenido()
    {
        using var db = CrearDbContext();
        var linea = await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000703", 60_000m));
        linea.Bloquear("Cartera vencida");
        await db.SaveChangesAsync();

        var (res, _) = await Autorizar(db, pendiente.Id, "Intento", Nestor, Ahora.AddHours(1));

        res.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        res.PorcentajeCobertura.Should().BeNull();
        res.CreditoDisponibleSnapshot.Should().Be(0m);
        (await db.AutorizacionesCredito.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Autorizar_con_cobertura_parcial_recalculada_libera_con_override_y_nuevo_snapshot()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000704", 60_000m));
        await AgregarFacturaAbierta(db, 5_000m); // disponible 35,000 → 58.33 %

        var (res, _) = await Autorizar(db, pendiente.Id, "Pago en tránsito", Nestor, Ahora.AddHours(1));

        res.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        res.PorcentajeCobertura.Should().Be(58.33m);
        res.CreditoDisponibleSnapshot.Should().Be(35_000m);
        res.Motivo.Should().Be("Pago en tránsito");
        var autorizacion = await db.AutorizacionesCredito.SingleAsync();
        autorizacion.ClienteOPedidoRef.Should().Be("9000704");
        autorizacion.Estado.Should().Be(EstadoAutorizacionCredito.Usada);
    }

    // ------------------------------------------------ Rechazar

    [Fact]
    public async Task Rechazar_emite_retenido_por_cobertura_ligado_a_la_pendiente_con_motivo()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000801", 60_000m));

        var (res, eventos) = await Rechazar(db, pendiente.Id, "Cliente con atraso en pagos", Nestor);

        res.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        res.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Cobertura);
        res.DecisionOrigenId.Should().Be(pendiente.Id);
        res.DecididoPor.Should().Be(Nestor);
        res.Motivo.Should().Be("Cliente con atraso en pagos");
        res.PorcentajeCobertura.Should().Be(66.66m);
        res.CreditoDisponibleSnapshot.Should().Be(40_000m);
        res.OverrideId.Should().BeNull();
        (await db.AutorizacionesCredito.CountAsync()).Should().Be(0);
        eventos.OfType<DecisionLiberacionEmitidaEvent>().Single()
            .Should().Match<DecisionLiberacionEmitidaEvent>(e =>
                e.Resultado == "Retenido" && e.DecisionOrigenId == pendiente.Id
                && e.Motivo == "Cliente con atraso en pagos");

        var page = await new ListarDecisionesLiberacionHandler(db).Handle(
            new ListarDecisionesLiberacionQuery(Resultado: ResultadoLiberacion.PendienteAutorizacion),
            CancellationToken.None);
        page.Items.Single().ResolucionId.Should().Be(res.Id);
    }

    [Fact]
    public async Task Rechazar_quien_registro_la_pendiente_es_autoconsumo()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000802", 60_000m));

        var act = () => Rechazar(db, pendiente.Id, "No", Coordinador);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_AUTOCONSUMO");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Rechazar_pendiente_ya_resuelta_es_conflicto()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000803", 60_000m));
        await Autorizar(db, pendiente.Id, "Ok", Nestor);

        var act = () => Rechazar(db, pendiente.Id, "Tarde", Guid.NewGuid());

        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "DL_PENDIENTE_YA_RESUELTA");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Autorizar_pendiente_ya_rechazada_es_conflicto()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 40_000m);
        var (pendiente, _) = await Decidir(db, Comando("9000804", 60_000m));
        await Rechazar(db, pendiente.Id, "No procede", Nestor);

        var act = () => Autorizar(db, pendiente.Id, "Sí procede", Guid.NewGuid());

        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "DL_PENDIENTE_YA_RESUELTA");
        (await db.AutorizacionesCredito.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rechazar_decision_que_no_esta_pendiente_es_422()
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 100_000m, disponible: 30_000m);
        var (retenida, _) = await Decidir(db, Comando("9000805", 100_000m));

        var act = () => Rechazar(db, retenida.Id, "No", Nestor);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_NO_PENDIENTE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rechazar_exige_motivo(string motivo)
    {
        new RechazarLiberacionPendienteValidator()
            .Validate(new RechazarLiberacionPendienteCommand(Guid.NewGuid(), motivo))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rechazar_motivo_maximo_254()
    {
        new RechazarLiberacionPendienteValidator()
            .Validate(new RechazarLiberacionPendienteCommand(Guid.NewGuid(), new string('x', 255)))
            .IsValid.Should().BeFalse();
        new RechazarLiberacionPendienteValidator()
            .Validate(new RechazarLiberacionPendienteCommand(Guid.NewGuid(), new string('x', 254)))
            .IsValid.Should().BeTrue();
    }

    // ------------------------------------------------ Bordes

    [Theory]
    [InlineData(50_000, ResultadoLiberacion.PendienteAutorizacion, 50.00)]   // (a)
    [InlineData(99_990, ResultadoLiberacion.PendienteAutorizacion, 99.99)]
    [InlineData(100_000, ResultadoLiberacion.Liberado, 100.00)]
    [InlineData(49_990, ResultadoLiberacion.Retenido, 49.99)]
    [InlineData(-5_000, ResultadoLiberacion.Retenido, 0.00)]                 // disponible negativo
    public async Task Bordes_de_cobertura(decimal disponible, ResultadoLiberacion esperado, decimal porcentaje)
    {
        using var db = CrearDbContext();
        await LineaConDisponible(db, limite: 200_000m, disponible: disponible);

        var (res, _) = await Decidir(db, Comando("9000501", 100_000m));

        res.Resultado.Should().Be(esperado);
        res.PorcentajeCobertura.Should().Be(porcentaje);
    }

    // ------------------------------------------------ Validador

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Autorizar_exige_motivo(string motivo)
    {
        var result = new AutorizarLiberacionPendienteValidator()
            .Validate(new AutorizarLiberacionPendienteCommand(Guid.NewGuid(), motivo));
        result.IsValid.Should().BeFalse();
    }

    // ------------------------------------------------ helpers

    private static DecidirLiberacionCommand Comando(string pedidoRef, decimal monto, Guid? overrideId = null) =>
        new(pedidoRef, ClienteId, "MXN", monto, overrideId);

    private static async Task<(DecisionLiberacionResponse, List<object>)> Decidir(
        CuentasPorCobrarDbContext db, DecidirLiberacionCommand command,
        Guid? usuario = null, DateTimeOffset? ahora = null)
    {
        var publisher = new FakePublisher();
        var handler = new DecidirLiberacionHandler(
            db, publisher, new FakeEmpresaContext(EmpresaId), new FakeUserContext(usuario ?? Coordinador),
            new FakeClock(ahora ?? Ahora));
        var res = await handler.Handle(command, CancellationToken.None);
        return (res, publisher.Publicados);
    }

    private static async Task<(DecisionLiberacionResponse, List<object>)> Autorizar(
        CuentasPorCobrarDbContext db, Guid decisionId, string motivo, Guid usuario, DateTimeOffset? ahora = null)
    {
        var publisher = new FakePublisher();
        var handler = new AutorizarLiberacionPendienteHandler(
            db, publisher, new FakeEmpresaContext(EmpresaId), new FakeUserContext(usuario),
            new FakeClock(ahora ?? Ahora));
        var res = await handler.Handle(
            new AutorizarLiberacionPendienteCommand(decisionId, motivo), CancellationToken.None);
        return (res, publisher.Publicados);
    }

    private static async Task<(DecisionLiberacionResponse, List<object>)> Rechazar(
        CuentasPorCobrarDbContext db, Guid decisionId, string motivo, Guid usuario, DateTimeOffset? ahora = null)
    {
        var publisher = new FakePublisher();
        var handler = new RechazarLiberacionPendienteHandler(
            db, publisher, new FakeEmpresaContext(EmpresaId), new FakeUserContext(usuario),
            new FakeClock(ahora ?? Ahora));
        var res = await handler.Handle(
            new RechazarLiberacionPendienteCommand(decisionId, motivo), CancellationToken.None);
        return (res, publisher.Publicados);
    }

    /// <summary>Línea activa con el disponible dado (limite − facturas abiertas).</summary>
    private static async Task<LineaCreditoAggregate> LineaConDisponible(
        CuentasPorCobrarDbContext db, decimal limite, decimal disponible)
    {
        var linea = LineaCreditoAggregate.Crear(
            EmpresaId, ClienteId, "MXN", limite, OrigenLineaCredito.Solunion, plazoDias: 45);
        db.LineasCredito.Add(linea);
        var facturado = limite - disponible;
        if (facturado > 0) await AgregarFacturaAbierta(db, facturado);
        await db.SaveChangesAsync();
        return linea;
    }

    private static async Task AgregarFacturaAbierta(CuentasPorCobrarDbContext db, decimal total)
    {
        db.FacturasCartera.Add(FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), ClienteId, RfcCliente, "Cliente", Guid.NewGuid().ToString(),
            "FV-1", total, "MXN", "PPD", Ahora, Ahora.AddDays(45)));
        await db.SaveChangesAsync();
    }

    private static async Task<AutorizacionCredito> AgregarAutorizacion(
        CuentasPorCobrarDbContext db, Guid beneficiario, string referencia, Guid? supervisor = null)
    {
        var a = AutorizacionCredito.Crear(
            EmpresaId, supervisor ?? Nestor, beneficiario, "Cliente estratégico", referencia,
            Ahora, TimeSpan.FromHours(24));
        db.AutorizacionesCredito.Add(a);
        await db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task Reglas_serie_lista_solo_activas_con_su_comportamiento()
    {
        await using var db = CrearDbContext();
        db.ReglasLiberacionSerie.Add(new ReglaLiberacionSerie(Guid.NewGuid(), "9100", ComportamientoSerie.NuncaLibera, activo: false));
        await db.SaveChangesAsync();

        var reglas = await new ListarReglasLiberacionSerieHandler(db)
            .Handle(new ListarReglasLiberacionSerieQuery(), CancellationToken.None);

        Assert.DoesNotContain(reglas, r => r.Prefijo == "9100");
        Assert.Contains(reglas, r => r.Prefijo == "3000" && r.Comportamiento == ComportamientoSerie.NuncaLibera);
        Assert.Contains(reglas, r => r.Prefijo == "5000" && r.Comportamiento == ComportamientoSerie.SiempreLibera);
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_cobertura_{Guid.NewGuid()}")
            .Options;
        var db = new CuentasPorCobrarDbContext(options, new FakeEmpresaContext(EmpresaId));
        db.ReglasLiberacionSerie.AddRange(
            ReglaLiberacionSerieConfiguration.SeedReglas.Select(r =>
                new ReglaLiberacionSerie(r.Id, r.Prefijo, r.Comportamiento)));
        db.SaveChanges();
        return db;
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? UserName => "test";
    }

    private sealed class FakeClock(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
