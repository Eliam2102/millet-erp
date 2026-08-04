using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Transportista del catálogo cross-empresa
/// <c>compartido.transportistas</c> (F9-PR1). Empresas de logística
/// usadas en OCs para tracking de envíos (campo opcional —
/// alternativamente, captura libre vía <c>InfoLogisticaTransportistaTexto</c>).
/// </summary>
public sealed class Transportista : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Telefono { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Transportista() { }

    public Transportista(
        Guid id,
        string clave,
        string nombre,
        string? email = null,
        string? telefono = null,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("TRANSPORTISTA_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("TRANSPORTISTA_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
        if (email is { Length: > 254 })
            throw new BusinessRuleException("TRANSPORTISTA_EMAIL_INVALIDO",
                "El email no puede exceder 254 caracteres.");
        if (telefono is { Length: > 50 })
            throw new BusinessRuleException("TRANSPORTISTA_TELEFONO_INVALIDO",
                "El teléfono no puede exceder 50 caracteres.");

        Clave = clave;
        Nombre = nombre;
        Email = email;
        Telefono = telefono;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial (F-Admin-PR5.2). Inmutable: <see cref="Clave"/>
    /// (business key). Flags <c>limpiarX</c> ponen los nullable a null.
    /// </summary>
    public void ActualizarDatos(
        string? nombre = null,
        string? email = null,
        string? telefono = null,
        bool limpiarEmail = false,
        bool limpiarTelefono = false)
    {
        if (nombre is not null)
        {
            if (nombre.Length is 0 or > 254)
                throw new BusinessRuleException("TRANSPORTISTA_NOMBRE_INVALIDO",
                    "El nombre es requerido y no puede exceder 254 caracteres.");
            Nombre = nombre;
        }
        if (email is not null)
        {
            if (email.Length > 254)
                throw new BusinessRuleException("TRANSPORTISTA_EMAIL_INVALIDO",
                    "El email no puede exceder 254 caracteres.");
            Email = email;
        }
        else if (limpiarEmail) Email = null;

        if (telefono is not null)
        {
            if (telefono.Length > 50)
                throw new BusinessRuleException("TRANSPORTISTA_TELEFONO_INVALIDO",
                    "El teléfono no puede exceder 50 caracteres.");
            Telefono = telefono;
        }
        else if (limpiarTelefono) Telefono = null;
    }

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
