using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.cfdi.ingresado.v1</c>. Publicado al
/// outbox cuando un <see cref="Domain.Cfdi.CfdiRecibido"/> se ingresa al
/// repositorio (cualquier canal: descarga SAT, mailbox o carga manual).
///
/// <para>
/// Consumidor: <b>Almacén</b> — enlace diferido de la recepción variante A
/// (levantamiento §5.4). Cuando la recepción se registró con el folio
/// fiscal capturado del impreso (<c>cfdi_uuid_fiscal</c>, sin
/// <c>CfdiRecibidoId</c> porque el XML aún no estaba en el ERP), Almacén
/// correlaciona por <see cref="UuidCfdi"/> y backfillea la referencia.
/// </para>
///
/// <para>
/// Idempotencia del consumidor por <c>MessageId</c> del outbox
/// (<c>eventos_procesados</c>), y el enlace en sí es idempotente: solo
/// aplica sobre movimientos con <c>cfdi_recibido_id IS NULL</c>.
/// </para>
/// </summary>
public sealed record CfdiRecibidoIngresadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid CfdiRecibidoId,
    // UUID fiscal del SAT, normalizado a mayúsculas (VO UuidCfdi).
    string UuidCfdi,
    string RfcEmisor)
    : IntegrationEvent("cuentas_por_pagar.cfdi.ingresado.v1", EmpresaId, OcurridoEn);
