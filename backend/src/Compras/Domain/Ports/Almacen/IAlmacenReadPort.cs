namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>
/// Puerto de lectura de Compras hacia el catálogo de almacenes físicos
/// del módulo Almacén (<c>almacen.almacenes</c>, F1-PR1). Devuelve la
/// metadata mínima que Compras necesita para validar coherencia
/// cross-table.
///
/// <para>
/// Lo invoca <c>CrearRequisicionHandler</c> al validar la captura de una
/// requisición:
/// </para>
/// <list type="number">
///   <item>Si <see cref="ObtenerAsync"/> devuelve <c>null</c> ⇒
///         <c>EntityNotFoundException("ALMACEN_NO_ENCONTRADO")</c> → HTTP 404.</item>
///   <item>Si la lectura existe pero <c>SucursalId</c> no coincide con
///         el de la RQ ⇒
///         <c>BusinessRuleException("RQ_ALMACEN_NO_PERTENECE_A_SUCURSAL")</c>
///         → HTTP 422 (ver doc 01 §13 Rev. 21).</item>
/// </list>
/// <para>
/// El DTO <see cref="AlmacenLectura"/> expone sólo lo que Compras
/// consume hoy; ampliar campos cuando aparezca un caso de uso. Mismo
/// patrón cross-bounded-context que <c>IProveedorReadPort</c>
/// (Almacén ⇢ Compartido).
/// </para>
/// </summary>
public interface IAlmacenReadPort
{
    Task<AlmacenLectura?> ObtenerAsync(
        Guid almacenId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot de un almacén físico para validación cross-table desde
/// Compras. <c>EsActivo</c> queda como <c>true</c> cuando
/// <c>Estatus = Activo</c>; Compras no diferencia hoy entre Inactivo y
/// EnRevision, pero el campo está disponible si emerge la necesidad.
/// </summary>
public sealed record AlmacenLectura(
    Guid Id,
    string Clave,
    Guid SucursalId,
    bool EsActivo);
