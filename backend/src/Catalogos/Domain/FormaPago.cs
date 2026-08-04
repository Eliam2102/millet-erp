using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Forma de pago SAT del catálogo cross-empresa
/// <c>compartido.formas_pago</c> (F-Admin-PR5.3). Catálogo oficial
/// <c>c_FormaPago</c> del SAT (2 dígitos): 01 Efectivo, 02 Cheque
/// nominativo, 03 Transferencia electrónica, etc. Usado por
/// Facturación CFDI 4.0 y por CxP para conciliación de pagos.
///
/// <para>
/// Read-mostly: el catálogo se mantiene via migraciones aditivas
/// cuando el SAT publica versiones nuevas; no hay CRUD en UI.
/// </para>
/// </summary>
public sealed class FormaPago : BaseEntity, IAuditable
{
    /// <summary>Clave SAT de 2 dígitos (01, 02, 03, ...).</summary>
    public string ClaveSat { get; private set; } = string.Empty;

    public string Descripcion { get; private set; } = string.Empty;

    public bool Activa { get; private set; } = true;

    private FormaPago() { }

    public FormaPago(Guid id, string claveSat, string descripcion, bool activa = true)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(claveSat) || claveSat.Length != 2)
            throw new BusinessRuleException("FORMA_PAGO_CLAVE_INVALIDA",
                "La clave SAT debe tener exactamente 2 caracteres.");
        if (string.IsNullOrWhiteSpace(descripcion) || descripcion.Length > 254)
            throw new BusinessRuleException("FORMA_PAGO_DESCRIPCION_INVALIDA",
                "La descripción es requerida y no puede exceder 254 caracteres.");

        ClaveSat = claveSat;
        Descripcion = descripcion;
        Activa = activa;
    }
}
