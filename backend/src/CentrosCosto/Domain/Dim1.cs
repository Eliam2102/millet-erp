using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CentrosCosto.Domain;

/// <summary>
/// Dimensión 1 — Nivel 1 de la jerarquía Dim1 → Dim2 → Dim3 (la
/// planta/sucursal del negocio; UI-config "Dimensión 1"). Tabla
/// <c>centros_costo.dim1</c>.
///
/// <para>
/// Tabla PROPIA del módulo (separación total, 01-diseno §1 / levantamiento
/// §7.4): sin vínculo ni relación con <c>compartido.sucursales</c> — el
/// CONKAL de CeCo y la sucursal Conkal del ERP no se cruzan (consecuencia
/// aceptada). La clave de reportes (ej. "101") ES la <see cref="Clave"/>,
/// única global.
/// </para>
/// <para>
/// Dim1 no tiene padre ni grupo (los grupos clasifican a Dim2/Dim3). La
/// clave consolidada de reportes <c>{Dim1}-{Dim2}-{Dim3}</c> es computada,
/// no almacenada.
/// </para>
/// </summary>
public sealed class Dim1 : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Dim1() { }

    public Dim1(
        Guid id,
        string clave,
        string nombre,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        ValidarClave(clave);
        ValidarNombre(nombre);

        Clave = clave;
        Nombre = nombre;
        Estatus = estatus;
    }

    public void Editar(string clave, string nombre)
    {
        ValidarClave(clave);
        ValidarNombre(nombre);
        Clave = clave;
        Nombre = nombre;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 10)
            throw new BusinessRuleException("CECO_DIM1_CLAVE_INVALIDA",
                "La clave de la Dimensión 1 es requerida y no puede exceder 10 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("CECO_DIM1_NOMBRE_INVALIDO",
                "El nombre de la Dimensión 1 es requerido y no puede exceder 254 caracteres.");
    }
}
