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
/// </summary>
public sealed class Puesto : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Puesto() { }

    public Puesto(
        Guid id,
        string clave,
        string nombre,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("PUESTO_ID_INVALIDO", "El id es obligatorio.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        Clave = clave;
        Nombre = nombre;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial. Convención: parámetro <c>null</c> = no tocar.
    /// Inmutable: <see cref="Clave"/> (business key).
    /// </summary>
    public void ActualizarDatos(string? nombre = null)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
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
}
