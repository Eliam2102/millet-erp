using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Almacén físico bajo responsabilidad del Jefe Almacén (F1-PR1). Tabla
/// <c>almacen.almacenes</c>. Cada almacén pertenece a UNA sucursal (FK
/// lógica a <c>compartido.sucursales</c>); un proveedor puede tener
/// almacén en CDMX y otro en Monterrey — son registros distintos.
///
/// <para>
/// <b>Re-localización del placeholder:</b> reemplaza a la entidad MVP-light
/// <c>Millet.Almacen.Domain.Almacen</c> que vivía en <c>compartido.almacenes</c>.
/// La migración F1-PR1 hace copy-from aditivo (ambas tablas conviven);
/// F1-PR2 hace el DROP del placeholder. Ver
/// <c>docs/modulos/almacen/03-pr-breakdown.md</c> §Fase 1.
/// </para>
/// <para>
/// Contiene <see cref="SubAlmacen"/>es como entidades hijas — el agregado
/// raíz es responsable de mantener la coherencia (no se permite SubAlmacén
/// huérfano; alta y desactivación pasan por el agregado).
/// </para>
/// </summary>
public sealed class Almacen : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public Guid SucursalId { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private readonly List<SubAlmacen> _subAlmacenes = new();
    public IReadOnlyCollection<SubAlmacen> SubAlmacenes => _subAlmacenes.AsReadOnly();

    private Almacen() { }

    public Almacen(
        Guid id,
        string clave,
        string nombre,
        Guid sucursalId,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        ValidarClave(clave);
        ValidarNombre(nombre);
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("ALMACEN_SUCURSAL_INVALIDA",
                "El almacén debe pertenecer a una sucursal.");

        Clave = clave;
        Nombre = nombre;
        SucursalId = sucursalId;
        Estatus = estatus;
    }

    public void Editar(string clave, string nombre, Guid sucursalId)
    {
        ValidarClave(clave);
        ValidarNombre(nombre);
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("ALMACEN_SUCURSAL_INVALIDA",
                "El almacén debe pertenecer a una sucursal.");

        Clave = clave;
        Nombre = nombre;
        SucursalId = sucursalId;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("ALMACEN_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("ALMACEN_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
    }
}
