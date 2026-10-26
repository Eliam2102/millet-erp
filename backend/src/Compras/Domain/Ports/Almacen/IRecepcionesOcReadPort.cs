namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>
/// Una línea de movimiento de Almacén atribuida a una línea de OC: recepción
/// (<c>EntradaCompra</c>, cantidad positiva) o devolución a proveedor 8.B
/// (<c>SalidaPorDevolucionAProveedor</c>, cantidad negativa).
/// </summary>
public sealed record RecepcionOcLinea(
    Guid MovimientoId,
    string? Folio,
    Guid OcId,
    Guid LineaOcId,
    DateOnly Fecha,
    decimal Cantidad,
    bool EsDevolucion);

/// <summary>Lectura de solo consulta de recepciones y devoluciones a proveedor por OC (reporte de cumplimiento).</summary>
public interface IRecepcionesOcReadPort
{
    /// <summary>Movimientos Registrados de las OC pedidas, una fila por línea de movimiento con línea de OC conocida.</summary>
    Task<IReadOnlyList<RecepcionOcLinea>> ListarPorOcAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken ct);
}
