using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Anticipos;

/// <summary>
/// M-344: reversa interna de una aplicación de anticipo a una factura. Rastro append-only: guarda quién aplicó,
/// quién autorizó la reversa, el motivo y el importe devuelto al saldo del anticipo. «Interna» = no cancela la NC de
/// amortización ante el SAT (solo saldos; la conciliación fiscal de esa NC queda a validar con Fiscal). Su
/// <see cref="Id"/> viaja en el evento a CxC para ligar el movimiento de la factura con este.
/// </summary>
public sealed class ReversaAplicacionAnticipo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const int MotivoMin = 10;
    public const int MotivoMax = 400;

    public Guid EmpresaId { get; set; }
    public Guid AnticipoId { get; private set; }
    public Guid FacturaVentaId { get; private set; }
    public Guid NcAmortizacionId { get; private set; }
    public decimal Importe { get; private set; }
    public string Motivo { get; private set; } = default!;
    public Guid? AplicadoPorId { get; private set; }
    public Guid AutorizadoPorId { get; private set; }
    public string AutorizadoPorNombre { get; private set; } = default!;
    public DateTimeOffset ReversadoEn { get; private set; }

    private ReversaAplicacionAnticipo() { }

    internal static ReversaAplicacionAnticipo Crear(
        Guid empresaId, Guid anticipoId, Guid facturaVentaId, Guid ncAmortizacionId, decimal importe, string motivo,
        Guid? aplicadoPorId, Guid autorizadoPorId, string autorizadoPorNombre, DateTimeOffset ahora)
    {
        var limpio = motivo?.Trim() ?? "";
        if (limpio.Length is < MotivoMin or > MotivoMax)
            throw new BusinessRuleException("ANTICIPO_REVERSA_MOTIVO",
                $"El motivo de la reversa debe tener de {MotivoMin} a {MotivoMax} caracteres.");
        if (autorizadoPorId == Guid.Empty)
            throw new BusinessRuleException("ANTICIPO_REVERSA_AUTORIZADOR", "No se identificó a quien autoriza la reversa.");
        if (aplicadoPorId is not null && aplicadoPorId == autorizadoPorId)
            throw new BusinessRuleException("ANTICIPO_REVERSA_MISMO_USUARIO",
                "Quien aplicó el anticipo no puede autorizar su reversa. Solicítala a otra persona.");
        return new ReversaAplicacionAnticipo
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            AnticipoId = anticipoId,
            FacturaVentaId = facturaVentaId,
            NcAmortizacionId = ncAmortizacionId,
            Importe = importe,
            Motivo = limpio,
            AplicadoPorId = aplicadoPorId,
            AutorizadoPorId = autorizadoPorId,
            AutorizadoPorNombre = autorizadoPorNombre,
            ReversadoEn = ahora,
        };
    }
}
