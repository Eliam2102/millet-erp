namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>
/// Puerto de salida de Compras hacia el módulo Almacén de no-producción.
/// Consulta la disponibilidad de un artículo agregada a nivel <b>sucursal</b>
/// (rollup de todos los almacenes de la sucursal, diseño §8.1).
///
/// <para>
/// Lo invoca el handler de <c>Autorizar</c> (F4-PR1) cuando la matriz de
/// aprobación queda satisfecha, para calcular el cubrimiento por línea
/// antes de transicionar a <c>Autorizada</c> o <c>EnSurtido</c>.
/// </para>
/// <para>
/// <b>Workstream almacén-por-línea PR1:</b> la clave del rollup pasó de
/// <c>AlmacenDestinoId</c> (heredado de la RQ) a <c>SucursalId</c>. La RQ ya
/// no exige un almacén destino; el cubrimiento suma el stock de todos los
/// almacenes de la sucursal. Hoy el resultado no cambia (MILLET opera 1
/// almacén activo por sucursal), pero deja el sistema correcto para
/// sucursales con 2+ almacenes.
/// </para>
/// <para>
/// Implementación in-proc (mismo Bounded Context monolítico, distinto
/// schema). Stubs en F3-PR2; adapter real en la fase del módulo Almacén.
/// </para>
/// </summary>
public interface IConsultarStockPort
{
    Task<DisponibilidadStock> ConsultarPorSucursalAsync(
        Guid sucursalId,
        Guid articuloId,
        CancellationToken cancellationToken);
}
