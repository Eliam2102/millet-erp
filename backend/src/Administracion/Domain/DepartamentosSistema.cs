namespace Millet.Administracion.Domain;

/// <summary>
/// Identificadores fijos de departamentos de sistema (no capturados por humanos),
/// sembrados por HasData en <c>CompartidoDbContext</c>. Guid fijo para que el seed
/// y los consumidores (p. ej. el motor de reorden en PR5.C) lo referencien sin
/// ambigüedad entre ambientes.
/// </summary>
public static class DepartamentosSistema
{
    /// <summary>
    /// Departamento "Reabastecimiento Automático" — es el <c>DepartamentoId</c> de
    /// las RQ generadas por el motor de reorden (ADR-0047 PR5). La RQ de origen
    /// sistema omite la validación "opera en la sucursal", así que este depto no
    /// requiere filas <c>sucursal_departamentos</c>.
    /// </summary>
    public static readonly Guid ReabastecimientoAutomatico =
        Guid.Parse("0000000d-0001-0000-0000-000000000001");

    /// <summary>Clave (business key) del departamento de reabastecimiento automático.</summary>
    public const string ReabastecimientoAutomaticoClave = "SIS-REAB";
}
