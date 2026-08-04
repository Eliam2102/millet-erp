using MediatR;

namespace Millet.Compras.Application.CerrarManual;

/// <summary>
/// Comando del cierre manual de una requisición (ADR-0043 R3): el jefe de
/// almacén / almacenista cierra una RQ autorizada o en surtido que el
/// requisitante ya no necesita → <c>CerradaSinSurtir</c> (nada entregado) o
/// <c>CerradaSurtidaParcial</c> (algo entregado, derivado por el agregado).
///
/// <para>NO aborta OCs: el material pedido en vuelo llega como stock (permitir con
/// aviso). (PR4/ADR-0047: las RQ ya no reservan stock.) El permiso
/// <c>compras.requisiciones.cerrar-manual</c> se valida en el endpoint; el actor
/// sale del JWT vía <c>ICurrentUserContext</c>.</para>
/// </summary>
public sealed record CerrarManualRequisicionCommand(
    Guid RequisicionId,
    Guid MotivoId,
    string? MotivoTexto = null) : IRequest<Unit>;
