namespace Millet.Compras.Domain.Ports.Administracion;

/// <summary>
/// Puerto de lectura de Compras hacia el catálogo organizacional del
/// módulo Administración. Resuelve si una combinación
/// <c>(SucursalId, DepartamentoId)</c> opera (está asignada y Activa) en
/// la tabla <c>compartido.sucursal_departamentos</c> introducida por
/// PR-A1 (#333).
///
/// <para>
/// Lo invoca <c>CrearRequisicionHandler</c> al validar la captura de una
/// requisición. Si <see cref="OperaAsync"/> devuelve <c>false</c>, el
/// handler levanta <c>BusinessRuleException("RQ_DEPTO_NO_OPERA_EN_SUCURSAL")</c>
/// → HTTP 422 (ver doc 01 §13 Rev. 21).
/// </para>
/// <para>
/// El puerto colapsa los 3 escenarios "no válido" en un solo bool:
/// </para>
/// <list type="bullet">
///   <item>La fila NO existe (el departamento nunca se asignó a la sucursal).</item>
///   <item>La fila existe en estatus <c>Inactivo</c> (asignación desactivada).</item>
///   <item>La fila existe en estatus <c>EnRevision</c> (transitorio).</item>
/// </list>
/// <para>
/// Sólo <c>Activo</c> ⇒ <c>true</c>. El frontend filtra el selector para
/// que el caso unhappy sólo ocurra ante una request manual / replay.
/// </para>
/// </summary>
public interface ISucursalDepartamentoReadPort
{
    Task<bool> OperaAsync(
        Guid sucursalId,
        Guid departamentoId,
        CancellationToken cancellationToken);
}
