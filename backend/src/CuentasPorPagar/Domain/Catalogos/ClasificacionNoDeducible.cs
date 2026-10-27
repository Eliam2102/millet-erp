using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Catalogos;

/// <summary>Clasificación de gasto no deducible (P8.6). Filas DEMO hasta que Laura confirme el catálogo real.</summary>
public sealed class ClasificacionNoDeducible : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = "";
    public string Descripcion { get; private set; } = "";
    public bool Activa { get; private set; }
    private ClasificacionNoDeducible() { }
}
