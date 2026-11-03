using Millet.SharedKernel.Domain;
using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Línea de corrida de pagos (§4.5): un pasivo programado dentro del
/// lote. Única por (corrida, factura). Al ejecutarse (PR-5, reutilizando
/// el flujo de pago de PR-4) se marca <see cref="Ejecutada"/> y se emite
/// <c>aplicado.v1</c> por la factura cubierta (RN-4).
///
/// <para>
/// T9.1-A: guarda el desglose al armar la corrida. <see cref="ImporteProgramado"/>
/// es el neto (lo que se transfiere); <see cref="Bruto"/> y
/// <see cref="Retenciones"/> salen del CFDI (prorrata si el pasivo ya tiene
/// pagos). <see cref="Viva"/> sostiene el índice único parcial: un pasivo no
/// puede estar en dos corridas vivas.
/// </para>
/// </summary>
public sealed class CorridaPagoLinea : BaseEntity, IAuditable
{
    public Guid CorridaId { get; private set; }
    public Guid FacturaProveedorId { get; private set; }
    public Guid ProveedorId { get; private set; }
    public decimal Bruto { get; private set; }
    public decimal Retenciones { get; private set; }
    public decimal ImporteProgramado { get; private set; }
    public bool Ejecutada { get; private set; }
    public bool Viva { get; private set; } = true;

    private CorridaPagoLinea() { }

    internal CorridaPagoLinea(Guid corridaId, PasivoPendientePago pasivo) : base(Guid.CreateVersion7())
    {
        CorridaId = corridaId;
        FacturaProveedorId = pasivo.FacturaProveedorId;
        ProveedorId = pasivo.ProveedorId;
        ImporteProgramado = pasivo.SaldoPendiente;
        (Bruto, Retenciones) = pasivo.Desglose(pasivo.SaldoPendiente);
    }

    /// <summary>Referencia bancaria y fecha del pago que cubrió la línea (T9.1-B).</summary>
    public string? ReferenciaBancaria { get; private set; }
    public DateOnly? FechaEjecucion { get; private set; }

    internal void Liberar() => Viva = false;

    internal void MarcarEjecutada(string referencia, DateOnly fecha)
    {
        Ejecutada = true; ReferenciaBancaria = referencia; FechaEjecucion = fecha;
    }

    internal void RevertirEjecucion()
    {
        Ejecutada = false; ReferenciaBancaria = null; FechaEjecucion = null;
    }
}
