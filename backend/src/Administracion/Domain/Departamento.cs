using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Departamento del catálogo <c>compartido.departamentos</c> (B.1).
/// Representa una unidad funcional (Compras, Almacén, Mantenimiento,
/// Ingeniería, Calidad, etc.) — NO geográfica. Las requisiciones
/// referencian <c>DepartamentoId</c> del solicitante; los aprobadores
/// (F9-PR1) están scopeados por departamento via
/// <c>compras.aprobadores_departamento</c>.
///
/// <para>
/// F1-ADM-01: catálogo por empresa vía <see cref="EmpresaId"/> +
/// <see cref="IPerteneceAEmpresa"/>. <see cref="Clave"/> pasa de única
/// global a única por empresa.
/// </para>
/// </summary>
public sealed class Departamento : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; } // public set requerido por IPerteneceAEmpresa

    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Departamento() { }

    public Departamento(
        Guid id,
        Guid empresaId,
        string clave,
        string nombre,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("DEPARTAMENTO_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("DEPARTAMENTO_EMPRESA_INVALIDA", "La empresa es obligatoria.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        EmpresaId = empresaId;
        Clave = clave;
        Nombre = nombre;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables del departamento
    /// (F-Admin-PR2.2). Convención: parámetro <c>null</c> = no tocar.
    /// Inmutables: <see cref="Clave"/> (business key) y
    /// <see cref="EmpresaId"/>.
    /// </summary>
    public void ActualizarDatos(string? nombre = null)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
        }
    }

    /// <summary>
    /// Reactiva un departamento. Idempotente.
    /// </summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva el departamento. La validación cross-entity (no
    /// desactivar si tiene usuarios asignados con <c>departamento_id</c>
    /// matching) vive en el handler <c>DesactivarDepartamentoCommand</c>
    /// de F-Admin-PR2.3 porque requiere consultar
    /// <c>identidad.usuarios</c> (cross-módulo).
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    /// <summary>
    /// Mueve a estado <see cref="EstatusCatalogo.EnRevision"/> para
    /// flujos internos. El endpoint público solo expone Activar/Desactivar.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("DEPARTAMENTO_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("DEPARTAMENTO_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
    }
}
