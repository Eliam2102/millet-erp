using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Domain;

/// <summary>
/// Tests del state machine de <see cref="EntidadExterna"/>. Pure domain —
/// no toca DB ni DI.
///
/// <para>
/// <b>PR #201 — cleanup post-callback flow:</b> retirados los tests de
/// <c>MarcarEntregadoAAw</c>, <c>MarcarCorrelacionado</c> y
/// <c>MarcarCorrelacionExpirada</c> (métodos eliminados). El nuevo
/// state machine es:
/// <list type="bullet">
///   <item><c>Submitted</c> → <c>MarcarCorrelacionadaDirectamente</c> → <c>Correlated</c></item>
///   <item><c>Submitted</c> → <c>MarcarCorrelacionFallidaDirectamente</c> → <c>FailedCorrelation</c></item>
///   <item><c>Submitted</c> → <c>MarcarStuck</c> → <c>FailedDrop</c> (kind aw_processing_timeout)</item>
///   <item><c>Submitted</c> → <c>MarcarDropFalladoTerminal</c> → <c>FailedDrop</c> (HTTP terminal)</item>
///   <item><c>FailedDrop|ManuallyResolved</c> → <c>Reintentar</c> → <c>Submitted</c></item>
///   <item><c>FailedDrop|FailedCorrelation</c> → <c>MarcarResueltoManual</c> → <c>ManuallyResolved</c></item>
/// </list>
/// </para>
/// </summary>
public sealed class EntidadExternaStateMachineTests
{
    private static readonly string[] CodesMixed = ["1550", "1603", "1555"];
    private static readonly string[] CodesOnly1603 = ["1603"];
    private static readonly string[] CodesOnly1555 = ["1555"];
    private static readonly string[] CodesOnly1550 = ["1550"];

    private static EntidadExterna NewSubmitted() => new(
        id: Guid.NewGuid(),
        tipoEntidad: TipoEntidad.Cotizacion,
        referenciaExterna: "Q-TEST-001",
        empresaId: Guid.NewGuid(),
        payloadOriginal: "{\"q\":1}",
        ediContent: "EDI",
        submittedBySpnId: Guid.NewGuid(),
        submittedAt: DateTimeOffset.UtcNow,
        sucursal: "CIR");

    // ─── Constructor ───

    [Fact]
    public void Constructor_DejaEstadoEnSubmitted()
    {
        var entidad = NewSubmitted();
        entidad.Estado.Should().Be(EstadoEntidad.Submitted);
        entidad.RetryCount.Should().Be((short)0);
        entidad.DeliveredToAwAt.Should().BeNull();
        entidad.CorrelatedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_SinReferenciaExterna_LanzaArgumentException()
    {
        var act = () => new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "  ",
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: null,
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_SinEmpresaId_LanzaArgumentException()
    {
        var act = () => new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-1",
            empresaId: Guid.Empty,
            payloadOriginal: "{}",
            ediContent: null,
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_SinPayloadOriginal_LanzaArgumentException()
    {
        var act = () => new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-1",
            empresaId: Guid.NewGuid(),
            payloadOriginal: " ",
            ediContent: null,
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_SinSucursal_LanzaArgumentException()
    {
        var act = () => new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-1",
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: null,
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "  ");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Sucursal*");
    }

    [Fact]
    public void Constructor_SucursalDemasiadoLarga_LanzaArgumentException()
    {
        var act = () => new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-1",
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: null,
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "ABCDEFGHIJK"); // 11 chars > max 10
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Sucursal*");
    }

    // ─── IncrementarRetry ───

    [Fact]
    public void IncrementarRetry_EnSubmitted_IncrementaContador_NoCambiaEstado()
    {
        var entidad = NewSubmitted();
        entidad.IncrementarRetry("timeout", "transient");
        entidad.IncrementarRetry("dns", "transient");

        entidad.RetryCount.Should().Be((short)2);
        entidad.Estado.Should().Be(EstadoEntidad.Submitted);
        entidad.LastError.Should().Contain("transient").And.Contain("dns");
    }

    [Fact]
    public void IncrementarRetry_EnCorrelated_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);

        var act = () => entidad.IncrementarRetry("e", "k");
        act.Should().Throw<InvalidStateTransitionException>();
    }

    // ─── MarcarDropFalladoTerminal ───

    [Fact]
    public void MarcarDropFalladoTerminal_DesdeSubmitted_TransicionaAFailedDrop()
    {
        var entidad = NewSubmitted();

        entidad.MarcarDropFalladoTerminal("max attempts", "permanent");

        entidad.Estado.Should().Be(EstadoEntidad.FailedDrop);
        entidad.LastError.Should().Contain("permanent").And.Contain("max attempts");
    }

    [Fact]
    public void MarcarDropFalladoTerminal_DesdeCorrelated_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);

        var act = () => entidad.MarcarDropFalladoTerminal("x", "permanent");
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarcarDropFalladoTerminal_DesdeFailedDrop_PermiteReintento()
    {
        var entidad = NewSubmitted();
        entidad.MarcarDropFalladoTerminal("first", "transient");
        // Vuelve a fallar tras retry → segundo terminal va al mismo estado.
        entidad.MarcarDropFalladoTerminal("second", "transient");

        entidad.Estado.Should().Be(EstadoEntidad.FailedDrop);
        entidad.LastError.Should().Contain("second");
    }

    // ─── MarcarResueltoManual ───

    [Fact]
    public void MarcarResueltoManual_DesdeFailedDrop_TransicionaAManuallyResolved()
    {
        var entidad = NewSubmitted();
        entidad.MarcarDropFalladoTerminal("x", "permanent");

        var operador = Guid.NewGuid();
        entidad.MarcarResueltoManual("revisado y rechazado", operador);

        entidad.Estado.Should().Be(EstadoEntidad.ManuallyResolved);
        entidad.ResolutionNote.Should().Contain(operador.ToString());
        entidad.ResolutionNote.Should().Contain("revisado y rechazado");
    }

    [Fact]
    public void MarcarResueltoManual_DesdeFailedCorrelation_TransicionaAManuallyResolved()
    {
        // Operador cierra administrativamente un rechazo de A+W cuando
        // decide no reintentar (ej. cotización inválida que ya no aplica).
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionFallidaDirectamente(CodesOnly1555, "rechazado", DateTimeOffset.UtcNow);

        entidad.MarcarResueltoManual("cliente canceló, no reintentar", Guid.NewGuid());

        entidad.Estado.Should().Be(EstadoEntidad.ManuallyResolved);
    }

    [Fact]
    public void MarcarResueltoManual_DesdeCorrelated_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);

        var act = () => entidad.MarcarResueltoManual("nota", Guid.NewGuid());
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarcarResueltoManual_SinNota_LanzaArgumentException()
    {
        var entidad = NewSubmitted();
        entidad.MarcarDropFalladoTerminal("x", "permanent");

        var act = () => entidad.MarcarResueltoManual("", Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    // ─── Reintentar ───

    [Fact]
    public void Reintentar_DesdeFailedDrop_TransicionaASubmitted_ResetCounters()
    {
        var entidad = NewSubmitted();
        entidad.IncrementarRetry("x", "transient");
        entidad.IncrementarRetry("y", "transient");
        entidad.MarcarDropFalladoTerminal("max", "permanent");
        entidad.RetryCount.Should().Be((short)2);

        entidad.Reintentar();

        entidad.Estado.Should().Be(EstadoEntidad.Submitted);
        entidad.RetryCount.Should().Be((short)0);
        entidad.LastError.Should().BeNull();
    }

    [Fact]
    public void Reintentar_DesdeManuallyResolved_TransicionaASubmitted_ResetCounters()
    {
        var entidad = NewSubmitted();
        entidad.MarcarDropFalladoTerminal("x", "permanent");
        entidad.MarcarResueltoManual("revisado", Guid.NewGuid());

        entidad.Reintentar();

        entidad.Estado.Should().Be(EstadoEntidad.Submitted);
        entidad.RetryCount.Should().Be((short)0);
    }

    [Fact]
    public void Reintentar_DesdeSubmitted_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        var act = () => entidad.Reintentar();
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Reintentar_DesdeCorrelated_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);

        var act = () => entidad.Reintentar();
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Reintentar_DesdeFailedCorrelation_LanzaInvalidStateTransition()
    {
        // Para reintentar desde FailedCorrelation, primero hay que pasar
        // por ManuallyResolved (decisión consciente del operador).
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionFallidaDirectamente(CodesOnly1555, "rechazado", DateTimeOffset.UtcNow);

        var act = () => entidad.Reintentar();
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Reintentar_LimpiaAwErrorCodesYMessageYDiagnostic()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionFallidaDirectamente(
            CodesOnly1555, "rechazado", DateTimeOffset.UtcNow, "diag log content");
        entidad.MarcarResueltoManual("revisado por operador", Guid.NewGuid());

        entidad.AwErrorCodes.Should().NotBeNull();
        entidad.AwErrorMessage.Should().NotBeNull();
        entidad.AwDiagnosticLog.Should().NotBeNull();

        entidad.Reintentar();

        entidad.Estado.Should().Be(EstadoEntidad.Submitted);
        entidad.AwErrorCodes.Should().BeNull();
        entidad.AwErrorMessage.Should().BeNull();
        entidad.AwDiagnosticLog.Should().BeNull();
    }

    // ─── PR #198: callback model directo ───

    [Fact]
    public void MarcarCorrelacionadaDirectamente_DesdeSubmitted_TransicionaACorrelated()
    {
        var entidad = NewSubmitted();
        var when = DateTimeOffset.UtcNow;

        entidad.MarcarCorrelacionadaDirectamente(
            awDocId: 10427538L,
            correlatedAt: when,
            diagnosticLog: "(4614) [Documento]=10427538 [Tipo]=Pedido");

        entidad.Estado.Should().Be(EstadoEntidad.Correlated);
        entidad.AwDocId.Should().Be(10427538L);
        entidad.CorrelatedAt.Should().Be(when);
        entidad.DeliveredToAwAt.Should().Be(when);
        entidad.AwDiagnosticLog.Should().Contain("10427538");
        entidad.LastError.Should().BeNull();
        entidad.AwErrorCodes.Should().BeNull();
        entidad.AwErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarcarCorrelacionadaDirectamente_SinAwDocId_TransicionaConNull()
    {
        // Defensa: A+W debería emitir (4614) siempre per project memory.
        // Pero si el parser falla a extraerlo, no rompemos el flow —
        // marcamos Correlated con AwDocId=null y el operador investiga.
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(awDocId: null, DateTimeOffset.UtcNow);

        entidad.Estado.Should().Be(EstadoEntidad.Correlated);
        entidad.AwDocId.Should().BeNull();
    }

    [Fact]
    public void MarcarCorrelacionadaDirectamente_YaCorrelatedMismoDoc_EsIdempotente()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(42L, DateTimeOffset.UtcNow);

        var act = () => entidad.MarcarCorrelacionadaDirectamente(42L, DateTimeOffset.UtcNow.AddMinutes(1));

        act.Should().NotThrow();
        entidad.AwDocId.Should().Be(42L);
    }

    [Fact]
    public void MarcarCorrelacionadaDirectamente_YaCorrelatedOtroDoc_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(42L, DateTimeOffset.UtcNow);

        var act = () => entidad.MarcarCorrelacionadaDirectamente(43L, DateTimeOffset.UtcNow);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarcarCorrelacionadaDirectamente_DesdeFailedDrop_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarDropFalladoTerminal("network", "permanent");

        var act = () => entidad.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarcarCorrelacionadaDirectamente_DesdeFailedDropConTimeoutYFlag_TransicionaACorrelated()
    {
        // PR-4 late-reconciliation: FailedDrop con kind aw_processing_timeout
        // es el caso "A+W procesó tarde". Permite recuperar la cotización
        // cuando el flag permitirDesdeFailedDrop=true.
        var entidad = NewSubmitted();
        entidad.MarcarStuck(waitedMs: 120000, lane: "default");
        entidad.Estado.Should().Be(EstadoEntidad.FailedDrop);

        entidad.MarcarCorrelacionadaDirectamente(
            awDocId: 42L,
            correlatedAt: DateTimeOffset.UtcNow,
            diagnosticLog: "late-reconcile",
            permitirDesdeFailedDrop: true);

        entidad.Estado.Should().Be(EstadoEntidad.Correlated);
        entidad.AwDocId.Should().Be(42L);
        entidad.LastError.Should().BeNull();
    }

    [Fact]
    public void MarcarCorrelacionadaDirectamente_DesdeFailedDropConKindDistinto_LanzaAunConFlag()
    {
        // El flag SOLO aplica con kind=aw_processing_timeout. Un FailedDrop
        // por error HTTP terminal no debe transitar a Correlated por
        // late-reconcile — son escenarios distintos.
        var entidad = NewSubmitted();
        entidad.MarcarDropFalladoTerminal("401", "http_401");

        var act = () => entidad.MarcarCorrelacionadaDirectamente(
            1L, DateTimeOffset.UtcNow, permitirDesdeFailedDrop: true);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarcarCorrelacionFallidaDirectamente_DesdeFailedDropConTimeoutYFlag_TransicionaAFailedCorrelation()
    {
        // PR-4: si A+W procesa tarde Y rechaza, el late-reconciler aplica
        // la transición a FailedCorrelation.
        var entidad = NewSubmitted();
        entidad.MarcarStuck(waitedMs: 120000);

        entidad.MarcarCorrelacionFallidaDirectamente(
            errorCodes: CodesOnly1555,
            errorMessage: "rechazado tarde",
            failedAt: DateTimeOffset.UtcNow,
            diagnosticLog: "late",
            permitirDesdeFailedDrop: true);

        entidad.Estado.Should().Be(EstadoEntidad.FailedCorrelation);
        entidad.AwErrorCodes.Should().Be("1555");
        entidad.LastError.Should().Contain("aw_rejected");
    }

    [Fact]
    public void MarcarCorrelacionFallidaDirectamente_DesdeFailedDropConKindDistinto_LanzaAunConFlag()
    {
        var entidad = NewSubmitted();
        entidad.MarcarDropFalladoTerminal("forbidden", "http_403");

        var act = () => entidad.MarcarCorrelacionFallidaDirectamente(
            CodesOnly1555, "x", DateTimeOffset.UtcNow, permitirDesdeFailedDrop: true);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarcarCorrelacionFallidaDirectamente_DesdeSubmitted_TransicionaAFailedCorrelation()
    {
        var entidad = NewSubmitted();
        var when = DateTimeOffset.UtcNow;
        entidad.MarcarCorrelacionFallidaDirectamente(
            errorCodes: CodesMixed,
            errorMessage: "Artículo M7916 no existe; posiciones incompletas",
            failedAt: when,
            diagnosticLog: "(1555) Importación del pedido imposible.");

        entidad.Estado.Should().Be(EstadoEntidad.FailedCorrelation);
        entidad.AwErrorCodes.Should().Be("1550,1603,1555");
        entidad.AwErrorMessage.Should().Contain("M7916");
        entidad.AwDiagnosticLog.Should().Contain("1555");
        entidad.LastError.Should().Contain("aw_rejected");
        entidad.DeliveredToAwAt.Should().Be(when);
    }

    [Fact]
    public void MarcarCorrelacionFallidaDirectamente_YaFailedCorrelation_RefrescaCodigosYMensaje()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionFallidaDirectamente(
            CodesOnly1550, "viejo error", DateTimeOffset.UtcNow);

        var act = () => entidad.MarcarCorrelacionFallidaDirectamente(
            CodesOnly1555, "nuevo error", DateTimeOffset.UtcNow);

        act.Should().NotThrow();
        entidad.AwErrorCodes.Should().Be("1555");
        entidad.AwErrorMessage.Should().Be("nuevo error");
    }

    [Fact]
    public void MarcarCorrelacionFallidaDirectamente_DesdeCorrelated_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);

        var act = () => entidad.MarcarCorrelacionFallidaDirectamente(
            CodesOnly1555, "rechazado", DateTimeOffset.UtcNow);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void MarcarCorrelacionFallidaDirectamente_SinCodigos_LanzaArgumentException()
    {
        var entidad = NewSubmitted();

        var act = () => entidad.MarcarCorrelacionFallidaDirectamente(
            errorCodes: Array.Empty<string>(),
            errorMessage: "x",
            failedAt: DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarcarCorrelacionFallidaDirectamente_SinMensaje_LanzaArgumentException()
    {
        var entidad = NewSubmitted();

        var act = () => entidad.MarcarCorrelacionFallidaDirectamente(
            errorCodes: CodesOnly1555,
            errorMessage: "  ",
            failedAt: DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarcarStuck_DesdeSubmitted_TransicionaAFailedDropConKindAwProcessingTimeout()
    {
        var entidad = NewSubmitted();

        entidad.MarcarStuck(waitedMs: 120000, lane: "default");

        entidad.Estado.Should().Be(EstadoEntidad.FailedDrop);
        entidad.LastError.Should().Contain("aw_processing_timeout");
        entidad.LastError.Should().Contain("120000");
        entidad.LastError.Should().Contain("default");
    }

    [Fact]
    public void MarcarStuck_SinLane_OmiteSufijo()
    {
        var entidad = NewSubmitted();

        entidad.MarcarStuck(waitedMs: 5000);

        entidad.Estado.Should().Be(EstadoEntidad.FailedDrop);
        entidad.LastError.Should().Contain("aw_processing_timeout");
        entidad.LastError.Should().NotContain("lane=");
    }

    [Fact]
    public void MarcarStuck_DesdeCorrelated_LanzaInvalidStateTransition()
    {
        var entidad = NewSubmitted();
        entidad.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);

        var act = () => entidad.MarcarStuck(waitedMs: 1000);
        act.Should().Throw<InvalidStateTransitionException>();
    }
}
