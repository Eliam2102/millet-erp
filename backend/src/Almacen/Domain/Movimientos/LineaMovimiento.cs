using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Movimientos;

/// <summary>
/// Línea de un <see cref="MovimientoInventario"/> (01-diseno §5.1).
/// Tabla <c>almacen.lineas_movimiento</c>. CHECK <c>cantidad > 0</c>
/// (un movimiento con cantidad 0 carece de sentido). Las semánticas
/// de signo (suma o resta al saldo) las da el tipo del movimiento padre,
/// no la línea.
///
/// <para>
/// Snapshot de costo: <see cref="CostoUnitarioMxn"/> se congela al
/// momento de registrar el movimiento. Para entradas, es el precio de
/// OC (variante A) o el costo ajustado tras conciliación (variante B,
/// A11). Para salidas, es el costo promedio ponderado vigente del
/// saldo (snapshot necesario para devoluciones a costo histórico — A9,
/// A10).
/// </para>
/// </summary>
public sealed class LineaMovimiento : BaseEntity
{
    public Guid MovimientoId { get; private set; }
    public int Posicion { get; private set; }
    public Guid ArticuloId { get; private set; }
    public decimal Cantidad { get; private set; }
    public string UnidadMedida { get; private set; } = string.Empty;
    public decimal CostoUnitarioMxn { get; private set; }
    public decimal MontoTotalMxn { get; private set; }
    public string MonedaOriginal { get; private set; } = "MXN";
    public decimal? TipoCambioAplicado { get; private set; }

    // F2-PR2 / F3-PR1: lineas_factura_id (variante A/B). Nullable hasta llegar la factura.
    public Guid? LineaFacturaId { get; private set; }

    // F4: imputación a centro de costo / proyecto en salidas.
    public Guid? CentroCostoId { get; private set; }
    public Guid? ProyectoId { get; private set; }

    // ADR-0043: línea de RQ que esta línea de salida surte. NULL cuando la
    // salida no es contra RQ (p.ej. vale urgente) o no es una salida. Viaja
    // al evento almacen.salida_requisicion.registrada.v1 para que Compras
    // acumule CantidadEntregada por línea de RQ (canal de entrega).
    public Guid? LineaRqId { get; private set; }

    // F7: cantidades capturadas en conteo físico (para movimientos AjustePositivo/Negativo).
    public decimal? CantidadTeoricaAlContar { get; private set; }
    public decimal? CantidadRealContada { get; private set; }

    public string? UbicacionReferencia { get; private set; }
    public string? ComentarioLinea { get; private set; }

    /// <summary>
    /// ADR-0047 C7.2a: ubicación N4 (bin/rack) explícita de la línea. NULL =
    /// "enruta a la ubicación default (ÚNICA) del sub-almacén del movimiento" —
    /// el path de compatibilidad del trigger mientras los flujos migran (C7.2b
    /// entradas, C7.2c salidas). El histórico queda NULL a propósito (todo el
    /// saldo previo vive en las ÚNICA); no hay backfill.
    /// </summary>
    public Guid? UbicacionId { get; private set; }

    internal LineaMovimiento() { }

    public LineaMovimiento(
        Guid id,
        Guid movimientoId,
        int posicion,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        decimal costoUnitarioMxn,
        string monedaOriginal = "MXN",
        decimal? tipoCambioAplicado = null,
        Guid? centroCostoId = null,
        Guid? proyectoId = null,
        string? ubicacionReferencia = null,
        string? comentarioLinea = null,
        Guid? lineaRqId = null,
        Guid? ubicacionId = null) : base(id)
    {
        if (movimientoId == Guid.Empty)
            throw new BusinessRuleException("LINEA_MOV_SIN_PADRE",
                "La línea de movimiento requiere movimiento padre.");
        if (articuloId == Guid.Empty)
            throw new BusinessRuleException("LINEA_MOV_SIN_ARTICULO",
                "La línea requiere articulo_id.");
        if (cantidad <= 0)
            throw new BusinessRuleException("LINEA_MOV_CANT_NO_POSITIVA",
                "La cantidad de la línea debe ser positiva.");
        if (costoUnitarioMxn < 0)
            throw new BusinessRuleException("LINEA_MOV_COSTO_NEGATIVO",
                "El costo unitario no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(unidadMedida) || unidadMedida.Length > 20)
            throw new BusinessRuleException("LINEA_MOV_UM_INVALIDA",
                "La unidad de medida es requerida y no puede exceder 20 caracteres.");

        MovimientoId = movimientoId;
        Posicion = posicion;
        ArticuloId = articuloId;
        Cantidad = cantidad;
        UnidadMedida = unidadMedida;
        CostoUnitarioMxn = costoUnitarioMxn;
        MontoTotalMxn = Math.Round(cantidad * costoUnitarioMxn, 2);
        MonedaOriginal = monedaOriginal;
        TipoCambioAplicado = tipoCambioAplicado;
        CentroCostoId = centroCostoId;
        ProyectoId = proyectoId;
        UbicacionReferencia = ubicacionReferencia;
        ComentarioLinea = comentarioLinea;
        LineaRqId = lineaRqId;
        UbicacionId = ubicacionId;
    }

    /// <summary>
    /// F7: para movimientos generados desde conteo físico, persiste la
    /// pareja teórico/real que originó el ajuste.
    /// </summary>
    public void AsentarConteo(decimal cantidadTeorica, decimal cantidadReal)
    {
        CantidadTeoricaAlContar = cantidadTeorica;
        CantidadRealContada = cantidadReal;
    }

    /// <summary>
    /// F3-PR1: al conciliar variante B con factura, se vincula la línea
    /// de factura específica para trazabilidad.
    /// </summary>
    public void VincularLineaFactura(Guid lineaFacturaId)
    {
        LineaFacturaId = lineaFacturaId;
    }
}
