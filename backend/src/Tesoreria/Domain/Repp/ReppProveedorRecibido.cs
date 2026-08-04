using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Repp;

/// <summary>
/// REPP recibido del proveedor (§4.7, TES-4 sabor b): complemento de pago
/// que el proveedor emite a Millet por pagos PPD. Tesorería lo registra
/// (UUID + fecha; XML a Blob, ADR-0024) y publica
/// <c>tesoreria.repp-proveedor.recibido.v1</c> → CxP libera el motivo de
/// revisión <c>FALTA_REPP</c>. Inmutable post-registro.
///
/// <para>
/// TES-PR8: registro vía <c>RegistrarReppRecibidoCommand</c> (UUID único —
/// índice <c>ux_repp_recibido_uuid</c> como red final). Validación fiscal
/// del UUID post-MVP [T-G10, PLATFORM-TODO(&lt;ValidacionReppRecibido&gt;)].
/// </para>
/// </summary>
public sealed class ReppProveedorRecibido : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid FacturaProveedorId { get; private set; }
    public Guid UuidComplemento { get; private set; }
    public DateOnly FechaComplemento { get; private set; }
    public string? XmlBlobRef { get; private set; }
    public Guid RegistradoPor { get; private set; }
    public DateTimeOffset RegistradoEn { get; private set; }

    private ReppProveedorRecibido() { }

    public static ReppProveedorRecibido Registrar(
        Guid empresaId,
        Guid facturaProveedorId,
        Guid uuidComplemento,
        DateOnly fechaComplemento,
        string? xmlBlobRef,
        Guid registradoPor,
        DateTimeOffset ahora)
    {
        if (facturaProveedorId == Guid.Empty)
            throw new BusinessRuleException("REPP_FACTURA_VACIA", "La factura del proveedor es obligatoria.");
        if (uuidComplemento == Guid.Empty)
            throw new BusinessRuleException("REPP_UUID_VACIO", "El UUID del complemento de pago es obligatorio.");
        if (registradoPor == Guid.Empty)
            throw new BusinessRuleException("REPP_USUARIO_VACIO", "El usuario que registra es obligatorio.");

        return new ReppProveedorRecibido
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            FacturaProveedorId = facturaProveedorId,
            UuidComplemento = uuidComplemento,
            FechaComplemento = fechaComplemento,
            XmlBlobRef = xmlBlobRef,
            RegistradoPor = registradoPor,
            RegistradoEn = ahora,
        };
    }
}
