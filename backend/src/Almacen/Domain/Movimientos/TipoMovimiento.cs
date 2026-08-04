namespace Millet.Almacen.Domain.Movimientos;

/// <summary>
/// Discriminador del tipo de movimiento de inventario (01-diseno §4.2,
/// §5.1). La tabla <c>movimientos_inventario</c> es polimórfica con la
/// columna <c>tipo</c> como discriminador. Cada tipo activa columnas
/// específicas (oc_id, rq_id, vale_blob_ref, etc.).
///
/// <para>
/// El prefijo del folio (<see cref="FolioMovimiento"/>) se deriva de
/// este enum: ENT (entrada), SAL (salida), DEV (devolución), AJP/AJN
/// (ajuste positivo/negativo), AJF (ajuste por factura), BAJ
/// (baja por daño), REI (reincorporación).
/// </para>
/// </summary>
public enum TipoMovimiento : short
{
    /// <summary>Entrada por OC (recepción variante A o B). Folio ENT.</summary>
    EntradaCompra = 0,

    /// <summary>Salida con RQ aprobada (variante A normal). Folio SAL.</summary>
    SalidaConsumo = 1,

    /// <summary>Salida urgente por Vale firmado (variante B; regularización en 48h). Folio SAL.</summary>
    SalidaPorVale = 2,

    /// <summary>Devolución interna (sub-flujo 8.A — solicitante regresa material). Folio DEV.</summary>
    DevolucionSalida = 3,

    /// <summary>Devolución a proveedor (sub-flujo 8.B). Folio DEV.</summary>
    SalidaPorDevolucionAProveedor = 4,

    /// <summary>Ajuste positivo (inventario físico, +stock). Folio AJP.</summary>
    AjustePositivo = 5,

    /// <summary>Ajuste negativo (inventario físico, -stock). Folio AJN.</summary>
    AjusteNegativo = 6,

    /// <summary>Ajuste por diferencia de precio variante B (A11). Folio AJF.</summary>
    AjustePrecioFactura = 7,

    /// <summary>Baja del material en MAT-REV decidida por Calidad. Folio BAJ.</summary>
    BajaPorDano = 8,

    /// <summary>Reincorporación al inventario activo desde MAT-REV. Folio REI.</summary>
    ReincorporacionTrasRevision = 9,
}

internal static class TipoMovimientoExtensions
{
    /// <summary>
    /// Prefijo de 3 letras del folio para cada tipo. Usado por
    /// <see cref="FolioMovimiento"/> al armar <c>M-{prefijo}{año}-{secuencial}</c>.
    /// </summary>
    public static string PrefijoFolio(this TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.EntradaCompra => "ENT",
        TipoMovimiento.SalidaConsumo => "SAL",
        TipoMovimiento.SalidaPorVale => "SAL",
        TipoMovimiento.DevolucionSalida => "DEV",
        TipoMovimiento.SalidaPorDevolucionAProveedor => "DEV",
        TipoMovimiento.AjustePositivo => "AJP",
        TipoMovimiento.AjusteNegativo => "AJN",
        TipoMovimiento.AjustePrecioFactura => "AJF",
        TipoMovimiento.BajaPorDano => "BAJ",
        TipoMovimiento.ReincorporacionTrasRevision => "REI",
        _ => "MOV",
    };

    /// <summary>
    /// Indica si el movimiento es de signo positivo (suma al saldo) —
    /// usado por el trigger transaccional al actualizar
    /// <c>saldos_inventario</c>.
    /// </summary>
    public static bool EsEntrada(this TipoMovimiento tipo) => tipo switch
    {
        TipoMovimiento.EntradaCompra => true,
        TipoMovimiento.DevolucionSalida => true,
        TipoMovimiento.AjustePositivo => true,
        TipoMovimiento.ReincorporacionTrasRevision => true,
        _ => false,
    };
}
