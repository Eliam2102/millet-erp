using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Movimientos;

/// <summary>
/// Liga N:M entre un <see cref="MovimientoBancario"/> de egreso y un
/// pasivo de CxP (§4.3): un pago puede cubrir N pasivos y un pasivo
/// cubrirse en M pagos, pero el evento
/// <c>tesoreria.pago-factura-proveedor.aplicado.v1</c> se emite POR
/// FACTURA (RN-4, contrato congelado).
///
/// <para>
/// El <c>Id</c> de esta fila ES el <c>PagoId</c> del evento
/// <c>aplicado.v1</c> (§5 DDL) — trazabilidad 1:1 con lo que CxP
/// registra. <c>FacturaProveedorId</c> viene del evento de pasivo, sin
/// FK cross-módulo. Única por (movimiento, factura). El comportamiento
/// (registro, reversa RN-10) llega en PR-4.
/// </para>
/// </summary>
public sealed class AplicacionPagoProveedor : BaseEntity
{
    public Guid MovimientoId { get; private set; }
    public Guid FacturaProveedorId { get; private set; }
    public Guid ProveedorId { get; private set; }
    public decimal ImporteAplicado { get; private set; }

    /// <summary>Corrida de pagos que originó esta aplicación (NULL en pago individual).</summary>
    public Guid? CorridaId { get; private set; }

    /// <summary>Marca de reversa (RN-10): nada se borra; el contramovimiento vive en <c>MovimientoBancario</c>.</summary>
    public bool Revertida { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    private AplicacionPagoProveedor() { }

    /// <summary>
    /// Nueva aplicación (TES-PR4). El <c>Id</c> generado aquí ES el
    /// <c>PagoId</c> que viaja en <c>aplicado.v1</c> y que CxP correlaciona
    /// en reversas (<c>PagoOriginalId</c>).
    /// </summary>
    public AplicacionPagoProveedor(
        Guid movimientoId,
        Guid facturaProveedorId,
        Guid proveedorId,
        decimal importeAplicado,
        DateTimeOffset creadoEn,
        Guid? corridaId = null) : base(Guid.CreateVersion7())
    {
        if (movimientoId == Guid.Empty)
            throw new BusinessRuleException("APL_MOVIMIENTO_VACIO", "El movimiento es obligatorio.");
        if (facturaProveedorId == Guid.Empty)
            throw new BusinessRuleException("APL_FACTURA_VACIA", "La factura del pasivo es obligatoria.");
        if (importeAplicado <= 0)
            throw new BusinessRuleException("APL_IMPORTE_INVALIDO", "El importe aplicado debe ser mayor a cero.");

        MovimientoId = movimientoId;
        FacturaProveedorId = facturaProveedorId;
        ProveedorId = proveedorId;
        ImporteAplicado = importeAplicado;
        CorridaId = corridaId;
        CreadoEn = creadoEn;
    }

    /// <summary>Marca de reversa (RN-10): nada se borra; el contramovimiento compensa.</summary>
    public void Revertir()
    {
        if (Revertida)
            throw new BusinessRuleException("APL_YA_REVERTIDA",
                $"La aplicación '{Id}' ya fue revertida.");
        Revertida = true;
    }
}
