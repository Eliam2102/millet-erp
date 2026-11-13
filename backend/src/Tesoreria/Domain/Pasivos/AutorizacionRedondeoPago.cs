using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Pasivos;

public enum EstadoAutorizacionRedondeoPago { Solicitada = 1, Autorizada = 2, Rechazada = 3, Consumida = 4 }

/// <summary>
/// M-326 parte 2 (b): autorización del redondeo de hasta $0.99 al pagar una factura de proveedor. El pago real difiere del saldo por
/// centavos; Tesorería no puede cerrar la factura con esa diferencia hasta que el Gerente de Área —distinto de quien la solicita— la
/// autoriza. <c>Redondeo</c> con signo: positivo = el saldo se liquida con menos efectivo (abono a REDONDEO); negativo = se pagó de
/// más (cargo a REDONDEO). Una autorización se consume una sola vez, al registrar el pago.
/// </summary>
public sealed class AutorizacionRedondeoPago : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const decimal Tolerancia = 0.99m;

    public Guid EmpresaId { get; set; }
    public Guid FacturaProveedorId { get; private set; }
    public decimal Redondeo { get; private set; }
    public string Motivo { get; private set; } = default!;
    public Guid SolicitadoPor { get; private set; }
    public EstadoAutorizacionRedondeoPago Estado { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public Guid? PagoId { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }

    private AutorizacionRedondeoPago() { }

    public AutorizacionRedondeoPago(Guid empresaId, Guid facturaProveedorId, decimal redondeo, string motivo, Guid solicitadoPor, DateTimeOffset ahora)
        : base(Guid.CreateVersion7())
    {
        ValidarMonto(redondeo);
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("RPP_MOTIVO_VACIO", "Indica el motivo del redondeo.");
        EmpresaId = empresaId; FacturaProveedorId = facturaProveedorId; Redondeo = redondeo; Motivo = motivo.Trim();
        SolicitadoPor = solicitadoPor; Estado = EstadoAutorizacionRedondeoPago.Solicitada; CreadoEn = ahora;
    }

    public static void ValidarMonto(decimal redondeo)
    {
        if (redondeo == 0 || Math.Abs(redondeo) > Tolerancia)
            throw new BusinessRuleException("RPP_REDONDEO_FUERA_DE_TOLERANCIA",
                $"El redondeo debe ser distinto de cero y de máximo ${Tolerancia:0.00} (en valor absoluto).");
        if (decimal.Round(redondeo, 2) != redondeo)
            throw new BusinessRuleException("RPP_REDONDEO_DECIMALES", "El redondeo lleva como máximo dos decimales.");
    }

    public void Autorizar(Guid usuarioId, DateTimeOffset ahora)
    {
        ExigirSolicitada();
        if (usuarioId == SolicitadoPor)
            throw new BusinessRuleException("RPP_MISMO_USUARIO", "Quien autoriza el redondeo debe ser distinto de quien lo solicitó.");
        Estado = EstadoAutorizacionRedondeoPago.Autorizada; ResueltoPor = usuarioId; ResueltoEn = ahora;
    }

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        ExigirSolicitada();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("RPP_MOTIVO_RECHAZO_VACIO", "Indica el motivo del rechazo.");
        Estado = EstadoAutorizacionRedondeoPago.Rechazada; ResueltoPor = usuarioId; ResueltoEn = ahora; MotivoRechazo = motivo.Trim();
    }

    public void Consumir(Guid pagoId)
    {
        if (Estado != EstadoAutorizacionRedondeoPago.Autorizada)
            throw new BusinessRuleException("RPP_NO_AUTORIZADO", "El redondeo no está autorizado por el Gerente de Área.");
        Estado = EstadoAutorizacionRedondeoPago.Consumida; PagoId = pagoId;
    }

    private void ExigirSolicitada()
    {
        if (Estado != EstadoAutorizacionRedondeoPago.Solicitada)
            throw new BusinessRuleException("RPP_YA_RESUELTO", "La solicitud de redondeo ya fue resuelta.");
    }
}
