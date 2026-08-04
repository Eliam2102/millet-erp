using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CentrosCosto.Domain;

/// <summary>
/// Grupo de la Dimensión 2 (UI-config "Grupo dimensión 2"; ej. OPERACIONES,
/// COMERCIAL). Tabla <c>centros_costo.grupos_dim2</c>.
///
/// <para>
/// Catálogo GLOBAL (la fuente lo usa consistente entre plantas): clasifica
/// a <see cref="Dim2"/>, NO es un nivel. En el árbol de configuración es un
/// chip; en el árbol de asignación actúa acotado al padre (misma data, dos
/// árboles — 01-diseno §3/§7).
/// </para>
/// </summary>
public sealed class GrupoDim2 : BaseEntity, IAuditable
{
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private GrupoDim2() { }

    public GrupoDim2(Guid id, string nombre, EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
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
            throw new BusinessRuleException("CECO_GRUPO_DIM2_NOMBRE_INVALIDO",
                "El nombre del grupo de dimensión 2 es requerido y no puede exceder 100 caracteres.");
    }
}
