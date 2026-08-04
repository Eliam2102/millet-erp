using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Sub-almacén dentro de un <see cref="Almacen"/> (01-diseno §5.1). Tabla
/// <c>almacen.sub_almacenes</c>. Es la unidad de localización física a la
/// que se vinculan los movimientos de inventario.
///
/// <para>
/// Unicidad: <c>(almacen_id, clave)</c>. Un mismo <c>ACC-CIR</c> puede
/// existir en distintos almacenes (uno por sucursal).
/// </para>
/// </summary>
public sealed class SubAlmacen : BaseEntity, IAuditable
{
    public Guid AlmacenId { get; private set; }
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public TipoSubAlmacen Tipo { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private SubAlmacen() { }

    public SubAlmacen(
        Guid id,
        Guid almacenId,
        string clave,
        string nombre,
        TipoSubAlmacen tipo,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (almacenId == Guid.Empty)
            throw new BusinessRuleException("SUBALMACEN_ALMACEN_INVALIDO",
                "El sub-almacén debe pertenecer a un almacén.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        AlmacenId = almacenId;
        Clave = clave;
        Nombre = nombre;
        Tipo = tipo;
        Estatus = estatus;
    }

    public void Editar(string clave, string nombre, TipoSubAlmacen tipo)
    {
        ValidarClave(clave);
        ValidarNombre(nombre);

        Clave = clave;
        Nombre = nombre;
        Tipo = tipo;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("SUBALMACEN_CLAVE_INVALIDA",
                "La clave del sub-almacén es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("SUBALMACEN_NOMBRE_INVALIDO",
                "El nombre del sub-almacén es requerido y no puede exceder 254 caracteres.");
    }
}
