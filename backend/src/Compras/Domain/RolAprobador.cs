namespace Millet.Compras.Domain;

/// <summary>
/// Rol de aprobación dentro del flujo de Requisiciones (F9-PR1, A1
/// multidimensional). Cada `(empresa_id, departamento_id, rol)` puede
/// tener a lo más UN aprobador vigente a la vez (UNIQUE filtered index
/// con <c>vigente_hasta IS NULL</c>).
///
/// <list type="bullet">
///   <item><see cref="JefeDpto"/>: autoriza Nivel 1 (RQs del depto, monto bajo).</item>
///   <item><see cref="JefeAlmacen"/>: visa stock-aware (transversal a deptos; uno por sucursal).</item>
///   <item><see cref="AutorizadorN2"/>: autoriza Nivel 2 (montos altos / artículos críticos).</item>
/// </list>
///
/// Persistido como <c>smallint</c> con CHECK constraint <c>0..2</c>.
/// </summary>
public enum RolAprobador : short
{
    /// <summary>Autoriza Nivel 1.</summary>
    JefeDpto = 0,

    /// <summary>Visa stock-aware; transversal a deptos.</summary>
    JefeAlmacen = 1,

    /// <summary>Autoriza Nivel 2 (montos altos / críticos).</summary>
    AutorizadorN2 = 2,
}
