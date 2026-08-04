using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Cartera;

/// <summary>
/// Movimiento individual aplicado a una <see cref="FacturaCartera"/>
/// (CXC-PR3). Correlaciona cada pago/NC con el comprobante fiscal que lo
/// originó (<see cref="OrigenComprobanteId"/> = id del REPP, cobro de
/// mostrador o NC) — es lo que permite REVERTIR con exactitud cuando
/// llega <c>ComprobanteCanceladoIntegrationEvent</c> del origen, sin
/// depender del payload de la cancelación (que solo trae el id).
///
/// <para>Append-only: la reversa marca <see cref="Revertido"/>, nunca
/// borra — auditabilidad del levantamiento §1.</para>
/// </summary>
public sealed class MovimientoCartera : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid FacturaCarteraId { get; private set; }
    public TipoMovimientoCartera Tipo { get; private set; }

    /// <summary>Id del comprobante fiscal origen (REPP / CobroMostrador / NC).</summary>
    public Guid OrigenComprobanteId { get; private set; }

    public decimal Importe { get; private set; }
    public DateTimeOffset FechaMovimiento { get; private set; }

    public bool Revertido { get; private set; }
    public DateTimeOffset? RevertidoEn { get; private set; }

    private MovimientoCartera() { }

    public MovimientoCartera(
        Guid empresaId,
        Guid facturaCarteraId,
        TipoMovimientoCartera tipo,
        Guid origenComprobanteId,
        decimal importe,
        DateTimeOffset fechaMovimiento) : base(Guid.CreateVersion7())
    {
        if (importe <= 0)
            throw new BusinessRuleException("MC_IMPORTE_INVALIDO", "El importe del movimiento debe ser > 0.");

        EmpresaId = empresaId;
        FacturaCarteraId = facturaCarteraId;
        Tipo = tipo;
        OrigenComprobanteId = origenComprobanteId;
        Importe = importe;
        FechaMovimiento = fechaMovimiento;
    }

    public void Revertir(DateTimeOffset cuando)
    {
        if (Revertido)
            throw new BusinessRuleException("MC_YA_REVERTIDO", "El movimiento ya fue revertido.");
        Revertido = true;
        RevertidoEn = cuando;
    }
}
