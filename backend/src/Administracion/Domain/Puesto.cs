using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Puesto organizacional del catálogo <c>compartido.puestos</c>
/// (doc 10-catalogo-puestos-empleados, ADM-PR1). Es la llave de las
/// reglas de negocio por puesto — hoy las políticas de viáticos de CxP
/// (<c>politicas_viaticos(puesto, tipo_destino, ...)</c>); mañana
/// cualquier tope o autorización por nivel.
///
/// <para>
/// La migración siembra EJEC/GER/OPER con los GUIDs del antiguo
/// <c>NoOpPuestoReadPort</c> de CxP (decisión D6) para no romper las
/// políticas de viáticos capturadas en dev contra ese seed.
/// </para>
///
/// <para>
/// F1-ADM-01: catálogo por empresa vía <see cref="EmpresaId"/> +
/// <see cref="IPerteneceAEmpresa"/>. <see cref="Clave"/> pasa de única
/// global a única por empresa.
/// </para>
/// </summary>
public sealed class Puesto : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; } // public set requerido por IPerteneceAEmpresa

    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Descripción libre opcional del puesto (F1-ADM-01).</summary>
    public string? Descripcion { get; private set; }

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Puesto() { }

    public Puesto(
        Guid id,
        Guid empresaId,
        string clave,
        string nombre,
        string? descripcion = null,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("PUESTO_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("PUESTO_EMPRESA_INVALIDA", "La empresa es obligatoria.");
        ValidarClave(clave);
        ValidarNombre(nombre);
        ValidarDescripcion(descripcion);

        EmpresaId = empresaId;
        Clave = clave;
        Nombre = nombre;
        Descripcion = descripcion?.Trim();
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial. Convención: parámetro <c>null</c> = no tocar;
    /// <paramref name="limpiarDescripcion"/> = <c>true</c> limpia el
    /// campo opcional. Inmutables: <see cref="Clave"/> (business key) y
    /// <see cref="EmpresaId"/>.
    /// </summary>
    public void ActualizarDatos(
        string? nombre = null,
        string? descripcion = null,
        bool limpiarDescripcion = false)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
        }

        if (limpiarDescripcion)
        {
            Descripcion = null;
        }
        else if (descripcion is not null)
        {
            ValidarDescripcion(descripcion);
            Descripcion = descripcion.Trim();
        }
    }

    /// <summary>Reactiva el puesto. Idempotente.</summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva el puesto. Los empleados que lo referencian conservan la
    /// FK (histórico); la validación "no asignar puesto inactivo" vive en
    /// los handlers de Empleado.
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("PUESTO_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("PUESTO_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
    }

    private static void ValidarDescripcion(string? descripcion)
    {
        if (descripcion is { Length: > 500 })
            throw new BusinessRuleException("PUESTO_DESCRIPCION_INVALIDA",
                "La descripción no puede exceder 500 caracteres.");
    }
}
