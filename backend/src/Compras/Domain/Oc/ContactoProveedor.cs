using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable que representa el snapshot del contacto
/// principal del proveedor al momento de capturar la OC (diseño §4.5).
/// Si el proveedor cambia su contacto después, la OC mantiene este
/// snapshot histórico — útil para auditoría y reimpresión.
///
/// Los tres campos son opcionales individualmente; el VO entero también
/// puede ser <c>null</c> en el agregado (OC sin contacto capturado).
/// </summary>
public readonly record struct ContactoProveedor
{
    public string? Nombre { get; }
    public string? Email { get; }
    public string? Telefono { get; }

    public ContactoProveedor(string? nombre, string? email, string? telefono)
    {
        if (nombre is { Length: > 200 })
        {
            throw new BusinessRuleException(
                "CONTACTO_NOMBRE_DEMASIADO_LARGO",
                "El nombre del contacto no puede exceder 200 caracteres.");
        }
        if (email is { Length: > 200 })
        {
            throw new BusinessRuleException(
                "CONTACTO_EMAIL_DEMASIADO_LARGO",
                "El email del contacto no puede exceder 200 caracteres.");
        }
        if (telefono is { Length: > 50 })
        {
            throw new BusinessRuleException(
                "CONTACTO_TELEFONO_DEMASIADO_LARGO",
                "El teléfono del contacto no puede exceder 50 caracteres.");
        }

        Nombre = string.IsNullOrWhiteSpace(nombre) ? null : nombre;
        Email = string.IsNullOrWhiteSpace(email) ? null : email;
        Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono;
    }

    /// <summary>True si todos los campos son null (snapshot vacío).</summary>
    public bool EsVacio => Nombre is null && Email is null && Telefono is null;
}
