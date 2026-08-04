using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Condiciones de pago del catálogo cross-empresa
/// <c>compartido.condiciones_pago</c> (F9-PR1). Define plazos de
/// pago a proveedor (CONTADO, 30 días, 60 días, 90 días, etc.). Usado
/// por OC (campo <c>condiciones_pago_id</c>) para calcular fechas de
/// vencimiento y por CxP para programar pagos.
/// </summary>
public sealed class CondicionesPago : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Días de crédito a partir de la recepción de la factura. 0 = contado.</summary>
    public int DiasCredito { get; private set; }

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private CondicionesPago() { }

    public CondicionesPago(
        Guid id,
        string clave,
        string nombre,
        int diasCredito,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("CONDICIONES_PAGO_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("CONDICIONES_PAGO_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 100 caracteres.");
        if (diasCredito is < 0 or > 365)
            throw new BusinessRuleException("CONDICIONES_PAGO_DIAS_INVALIDOS",
                "Los días de crédito deben estar entre 0 y 365.");

        Clave = clave;
        Nombre = nombre;
        DiasCredito = diasCredito;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial (F-Admin-PR5.2). Inmutable: <see cref="Clave"/>
    /// (business key).
    /// </summary>
    public void ActualizarDatos(string? nombre = null, int? diasCredito = null)
    {
        if (nombre is not null)
        {
            if (nombre.Length is 0 or > 100)
                throw new BusinessRuleException("CONDICIONES_PAGO_NOMBRE_INVALIDO",
                    "El nombre es requerido y no puede exceder 100 caracteres.");
            Nombre = nombre;
        }
        if (diasCredito is int d)
        {
            if (d is < 0 or > 365)
                throw new BusinessRuleException("CONDICIONES_PAGO_DIAS_INVALIDOS",
                    "Los días de crédito deben estar entre 0 y 365.");
            DiasCredito = d;
        }
    }

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
