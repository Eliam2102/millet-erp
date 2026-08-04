using MediatR;

namespace Millet.Integraciones.Aw.Application.Commands.MarcarResueltoManual;

/// <summary>
/// Comando del endpoint <c>POST /cotizaciones/{id}/marcar-resuelto</c>.
/// Cierra administrativamente una cotización en estado terminal no
/// deseado (<c>FailedDrop</c> / <c>FailedCorrelation</c>) marcándola
/// como <c>ManuallyResolved</c> con una nota del operador.
///
/// <para>
/// <b>Diferencia con Reintentar:</b> esta acción NO reactiva el flujo
/// del worker — declara que el operador decidió no reintentar (ej. el
/// cliente acordó usar otra cotización, hubo error de input que no se
/// va a corregir, etc.). El estado <c>ManuallyResolved</c> es terminal
/// pero distinto de <c>Correlated</c>.
/// </para>
///
/// <para>
/// El handler valida que <c>Estado ∈ {FailedDrop, FailedCorrelation}</c>.
/// El método de dominio <c>EntidadExterna.MarcarResueltoManual</c> permite
/// la transición desde más estados (cualquier activo no-terminal), pero
/// el caso de uso administrativo exige esperar el desenlace natural
/// antes de intervenir manualmente.
/// </para>
/// </summary>
public sealed record MarcarResueltoManualCommand(
    Guid CotizacionId,
    string Nota
) : IRequest<MarcarResueltoManualResponse>;
