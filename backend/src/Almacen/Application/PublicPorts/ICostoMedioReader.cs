using Millet.Almacen.Domain.Saldos;

namespace Millet.Almacen.Application.PublicPorts;

/// <summary>
/// Costo medio por m² (o por pieza) que quedaría tras una compra (M-149). ESTIMADO: COMPRA_RECIBIDA se emite al autorizar la OC,
/// antes de la recepción; las salidas y entradas entre la autorización y la recepción mueven la existencia real, así que A+W
/// conserva este valor tal cual solo hasta que llegue la recepción.
/// </summary>
public interface ICostoMedioReader
{
    /// <param name="costoCompra">Costo unitario de la compra en su moneda; se convierte a MXN con <paramref name="tipoCambio"/> (1 si MXN).</param>
    Task<decimal> EstimarTrasCompraAsync(Guid almacenId, Guid articuloId, Guid? varianteId, decimal piezasOM2Entran,
        decimal costoCompra, decimal tipoCambio, AgrupacionCostoMedio agrupacion, CancellationToken ct);
}
