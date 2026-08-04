using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CentrosCosto.Domain;

/// <summary>
/// Grupo de la Dimensión 3 (UI-config "Grupo dimensión 3"; ej. GENERAL,
/// HORNO 1, LINEA / CORTE 1). Tabla <c>centros_costo.grupos_dim3</c>.
///
/// <para>
/// Catálogo GLOBAL: clasifica a <see cref="Dim3"/>, NO es un nivel. Chip en
/// configuración; acotado al padre en el árbol de asignación (01-diseno
/// §3/§7).
/// </para>
/// </summary>
public sealed class GrupoDim3 : BaseEntity, IAuditable
{
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private GrupoDim3() { }

    public GrupoDim3(Guid id, string nombre, EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        ValidarNombre(nombre);
        Nombre = nombre;
        Estatus = estatus;
    }

    public void Renombrar(string nombre)
    {
        ValidarNombre(nombre);
        Nombre = nombre;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("CECO_GRUPO_DIM3_NOMBRE_INVALIDO",
                "El nombre del grupo de dimensión 3 es requerido y no puede exceder 100 caracteres.");
    }
}
