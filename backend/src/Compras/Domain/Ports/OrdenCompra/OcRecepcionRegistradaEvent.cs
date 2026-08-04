using MediatR;

namespace Millet.Compras.Domain.Ports.OrdenCompra;

/// <summary>
/// Evento de dominio in-proc emitido por el submódulo OC cuando una
/// recepción de material queda registrada contra una OC originada en
/// una requisición. El handler en Compras invoca
/// <c>Requisicion.RegistrarRecepcion(lineaId, cantidadRecibida)</c>:
/// si todas las líneas se cubren, la RQ transiciona a <c>Cerrada</c>
/// (diseño §4.1, §8.2).
///
/// <para>
/// F3-PR1 declara el contrato; el handler real entra en la fase de
/// recepción (post F4) — por ahora ningún emisor lo publica.
/// </para>
///
/// <para>
/// Lleva <see cref="EmpresaId"/> para que handlers multi-tenant puedan
/// resolver contexto sin volver a hidratar la requisición (consistente
/// con <c>RequisicionEnviadaAAutorizacionEvent</c>).
/// </para>
/// </summary>
public sealed record OcRecepcionRegistradaEvent(
    Guid RequisicionId,
    Guid LineaRequisicionId,
    Guid OrdenCompraId,
    Guid EmpresaId,
    decimal CantidadRecibida,
    DateTimeOffset OcurridoEn) : INotification;
