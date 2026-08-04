using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Uso principal del catálogo cross-empresa
/// <c>compartido.usos_principales</c>. Categoriza el destino contable/
/// operacional de una OC (ej. "Mantenimiento general", "Producción",
/// "Servicios profesionales", "Activo fijo"). Usado por OC (campo
/// <c>uso_principal_id</c>) y eventualmente por CxP / Activos para
/// presets de centros de costo y reportes.
///
/// <para>El catálogo nace con seed básico cubriendo los tipos más
/// comunes en non-producción industrial. F-Admin-PR5.2 agrega el CRUD
/// completo para administración desde UI.</para>
/// </summary>
public sealed class UsoPrincipal : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private UsoPrincipal() { }

    public UsoPrincipal(
        Guid id,
        string clave,
        string nombre,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("USO_PRINCIPAL_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("USO_PRINCIPAL_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 100 caracteres.");

        Clave = clave;
        Nombre = nombre;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial (F-Admin-PR5.2). Inmutable: <see cref="Clave"/>
    /// (business key).
    /// </summary>
    public void ActualizarDatos(string? nombre = null)
    {
        if (nombre is not null)
        {
            if (nombre.Length is 0 or > 100)
                throw new BusinessRuleException("USO_PRINCIPAL_NOMBRE_INVALIDO",
                    "El nombre es requerido y no puede exceder 100 caracteres.");
            Nombre = nombre;
        }
    }

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
