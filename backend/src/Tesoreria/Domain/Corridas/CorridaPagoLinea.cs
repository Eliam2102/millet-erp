using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Línea de corrida de pagos (§4.5): un pasivo programado dentro del
/// lote. Única por (corrida, factura). Al ejecutarse (PR-5, reutilizando
/// el flujo de pago de PR-4) se marca <see cref="Ejecutada"/> y se emite
/// <c>aplicado.v1</c> por la factura cubierta (RN-4).
/// </summary>
public sealed class CorridaPagoLinea : BaseEntity, IAuditable
{
    public Guid CorridaId { get; private set; }
    public Guid FacturaProveedorId { get; private set; }
    public decimal ImporteProgramado { get; private set; }
    public bool Ejecutada { get; private set; }

    private CorridaPagoLinea() { }
}
