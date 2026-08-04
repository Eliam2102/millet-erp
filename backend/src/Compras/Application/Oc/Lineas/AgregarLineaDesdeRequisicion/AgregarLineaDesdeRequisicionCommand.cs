using MediatR;

namespace Millet.Compras.Application.Oc.Lineas.AgregarLineaDesdeRequisicion;

/// <summary>
/// Agrega líneas de una RQ Autorizada a una OC en Borrador (flujo §4.2
/// del mapa funcional — consolidación N:1). La RQ se compromete con la
/// OC en la misma TX.
///
/// Validaciones: RQ Autorizada + no comprometida + sucursal coincide
/// con OC.SucursalDestinoId. Política §3.bis.4: mismo articulo_id de
/// RQs distintas crea líneas separadas (no se suman).
/// </summary>
public sealed record AgregarLineaDesdeRequisicionCommand(
    Guid OrdenCompraId,
    Guid RequisicionId) : IRequest<AgregarLineaDesdeRequisicionResponse>;

public sealed record AgregarLineaDesdeRequisicionResponse(
    int LineasAgregadas);
