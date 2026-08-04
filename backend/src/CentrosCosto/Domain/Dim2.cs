using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CentrosCosto.Domain;

/// <summary>
/// Dimensión 2 — Nivel 2 de la jerarquía Dim1 → Dim2 → Dim3 (UI-config
/// "Dimensión 2"). Tabla <c>centros_costo.dim2</c>.
///
/// <para>
/// Nada que ver con <c>compartido.departamentos</c> (separación total,
/// levantamiento §7.4 — la nomenclatura Dim elimina esa colisión de
/// nombres). El padre (<see cref="Dim1Id"/>) es INMUTABLE: reubicar = baja
/// + alta. Clave ÚNICA GLOBAL. <see cref="GrupoDim2Id"/> clasifica, no
/// anida.
/// </para>
/// <para>
/// Table-per-level standalone (DbSet propio + FK física por id, molde
/// Almacén N2–N4); la coherencia jerárquica la dan la FK a <c>dim1</c> y
/// la validación del padre en el handler.
/// </para>
/// </summary>
public sealed class Dim2 : BaseEntity, IAuditable
{
    public Guid Dim1Id { get; private set; }
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public Guid GrupoDim2Id { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Dim2() { }

    public Dim2(
        Guid id,
        Guid dim1Id,
        string clave,
        string nombre,
        Guid grupoDim2Id,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (dim1Id == Guid.Empty)
            throw new BusinessRuleException("CECO_DIM2_PADRE_INVALIDO",
                "La Dimensión 2 debe pertenecer a una Dimensión 1 del catálogo.");
        if (grupoDim2Id == Guid.Empty)
            throw new BusinessRuleException("CECO_GRUPO_DIM2_INVALIDO",
                "La Dimensión 2 debe clasificarse en un grupo de dimensión 2.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        Dim1Id = dim1Id;
        Clave = clave;
        Nombre = nombre;
        GrupoDim2Id = grupoDim2Id;
        Estatus = estatus;
    }

    /// <summary>Edita clave, nombre y grupo. El padre NO se mueve (reubicar = baja + alta).</summary>
    public void Editar(string clave, string nombre, Guid grupoDim2Id)
    {
        if (grupoDim2Id == Guid.Empty)
            throw new BusinessRuleException("CECO_GRUPO_DIM2_INVALIDO",
                "La Dimensión 2 debe clasificarse en un grupo de dimensión 2.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        Clave = clave;
        Nombre = nombre;
        GrupoDim2Id = grupoDim2Id;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("CECO_DIM2_CLAVE_INVALIDA",
                "La clave de la Dimensión 2 es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("CECO_DIM2_NOMBRE_INVALIDO",
                "El nombre de la Dimensión 2 es requerido y no puede exceder 254 caracteres.");
    }
}
