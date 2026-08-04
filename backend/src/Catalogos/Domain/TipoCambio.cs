using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Tipo de cambio histórico para una moneda en una fecha específica
/// (F-Admin-PR5.1). Vive en <c>compartido.tipos_cambio</c>. Restricción
/// UNIQUE (MonedaId, Fecha) garantiza un solo valor por par.
///
/// <para>
/// <see cref="ValorEnMxn"/> es el equivalente en pesos mexicanos de 1
/// unidad de la moneda en la fecha indicada. Precision (15, 6) — 6
/// decimales son suficientes para todas las monedas SAT y cubren JPY/CLP
/// (decimales=0 en Moneda) sin pérdida.
/// </para>
/// </summary>
public sealed class TipoCambio : BaseEntity, IAuditable
{
    public Guid MonedaId { get; private set; }

    public DateOnly Fecha { get; private set; }

    public decimal ValorEnMxn { get; private set; }

    public OrigenTipoCambio Origen { get; private set; }

    private TipoCambio() { }

    public TipoCambio(
        Guid id,
        Guid monedaId,
        DateOnly fecha,
        decimal valorEnMxn,
        OrigenTipoCambio origen = OrigenTipoCambio.Manual) : base(id)
    {
        if (monedaId == Guid.Empty)
            throw new BusinessRuleException("TIPO_CAMBIO_MONEDA_INVALIDA",
                "MonedaId es requerido.");
        if (valorEnMxn <= 0)
            throw new BusinessRuleException("TIPO_CAMBIO_VALOR_INVALIDO",
                "El valor en MXN debe ser mayor a cero.");

        MonedaId = monedaId;
        Fecha = fecha;
        ValorEnMxn = valorEnMxn;
        Origen = origen;
    }
}
