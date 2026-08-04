using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CentrosCosto.Domain;

/// <summary>
/// Dimensión 3 — Nivel 3, la HOJA de la jerarquía Dim1 → Dim2 → Dim3
/// (UI-config "Dimensión 3"; UI-documentos "Máquina"). Tabla
/// <c>centros_costo.dim3</c>.
///
/// <para>
/// Es el ÚNICO nivel seleccionable en documentos (Fase E): un solo campo
/// por línea; los niveles superiores se heredan. De cara al usuario
/// siempre "Clave - Nombre (Dim2 · Dim1)", nunca el Guid.
/// </para>
/// <para>
/// El padre (<see cref="Dim2Id"/>) es INMUTABLE: reubicar = baja + alta.
/// Clave ÚNICA GLOBAL. <see cref="GrupoDim3Id"/> clasifica, no anida. El
/// alcance de la Fase D se congela en ESTE nivel
/// (<c>usuario → dim3_id</c>, 01-diseno §7).
/// </para>
/// </summary>
public sealed class Dim3 : BaseEntity, IAuditable
{
    public Guid Dim2Id { get; private set; }
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public Guid GrupoDim3Id { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Dim3() { }

    public Dim3(
        Guid id,
        Guid dim2Id,
        string clave,
        string nombre,
        Guid grupoDim3Id,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (dim2Id == Guid.Empty)
            throw new BusinessRuleException("CECO_DIM3_PADRE_INVALIDO",
                "La Dimensión 3 debe pertenecer a una Dimensión 2 del catálogo.");
        if (grupoDim3Id == Guid.Empty)
            throw new BusinessRuleException("CECO_GRUPO_DIM3_INVALIDO",
                "La Dimensión 3 debe clasificarse en un grupo de dimensión 3.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        Dim2Id = dim2Id;
        Clave = clave;
        Nombre = nombre;
        GrupoDim3Id = grupoDim3Id;
        Estatus = estatus;
    }

    /// <summary>Edita clave, nombre y grupo. El padre NO se mueve (reubicar = baja + alta).</summary>
    public void Editar(string clave, string nombre, Guid grupoDim3Id)
    {
        if (grupoDim3Id == Guid.Empty)
            throw new BusinessRuleException("CECO_GRUPO_DIM3_INVALIDO",
                "La Dimensión 3 debe clasificarse en un grupo de dimensión 3.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        Clave = clave;
        Nombre = nombre;
        GrupoDim3Id = grupoDim3Id;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("CECO_DIM3_CLAVE_INVALIDA",
                "La clave de la Dimensión 3 es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("CECO_DIM3_NOMBRE_INVALIDO",
                "El nombre de la Dimensión 3 es requerido y no puede exceder 254 caracteres.");
    }
}
