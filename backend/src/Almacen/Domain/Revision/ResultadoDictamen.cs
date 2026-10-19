namespace Millet.Almacen.Domain.Revision;

/// <summary>Resultado del dictamen sobre material en revisión (ALM-04, G1.3).</summary>
public enum ResultadoDictamen : short
{
    /// <summary>Libera al inventario disponible (salida de MAT-REV + reincorporación).</summary>
    Liberar = 0,

    /// <summary>Rechaza al proveedor (abre una devolución a proveedor).</summary>
    Rechazar = 1,

    /// <summary>Baja por daño del material en MAT-REV.</summary>
    Baja = 2,
}
