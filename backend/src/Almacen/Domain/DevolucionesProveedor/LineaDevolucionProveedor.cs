using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.DevolucionesProveedor;

/// <summary>
/// Línea de una <see cref="DevolucionAProveedor"/>. Tabla
/// <c>almacen.lineas_devolucion_proveedor</c>. Costo unitario es
/// snapshot del costo de la recepción original (A10 del 01-diseno).
/// </summary>
public sealed class LineaDevolucionProveedor : BaseEntity
{
    public Guid DevolucionId { get; private set; }
    public int Posicion { get; private set; }
    public Guid ArticuloId { get; private set; }
    public decimal Cantidad { get; private set; }
    public string UnidadMedida { get; private set; } = string.Empty;
    public decimal CostoUnitarioMxn { get; private set; }
    public decimal MontoTotalMxn { get; private set; }
    public Guid? LineaRecepcionOrigenId { get; private set; }

    internal LineaDevolucionProveedor() { }

    public LineaDevolucionProveedor(
        Guid id,
        Guid devolucionId,
        int posicion,
        Guid articuloId,
        decimal cantidad,
        string unidadMedida,
        decimal costoUnitarioMxn,
        Guid? lineaRecepcionOrigenId = null) : base(id)
    {
        if (devolucionId == Guid.Empty)
            throw new BusinessRuleException("LINEA_DEV_PROV_SIN_PADRE",
                "La línea de devolución requiere agregado padre.");
        if (articuloId == Guid.Empty)
            throw new BusinessRuleException("LINEA_DEV_PROV_SIN_ARTICULO",
                "La línea requiere artículo.");
        if (cantidad <= 0)
            throw new BusinessRuleException("LINEA_DEV_PROV_CANT_NO_POSITIVA",
                "La cantidad de devolución debe ser positiva.");
        if (costoUnitarioMxn < 0)
            throw new BusinessRuleException("LINEA_DEV_PROV_COSTO_NEGATIVO",
                "El costo unitario no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(unidadMedida) || unidadMedida.Length > 20)
            throw new BusinessRuleException("LINEA_DEV_PROV_UM_INVALIDA",
                "La unidad de medida es requerida y no puede exceder 20 caracteres.");

        DevolucionId = devolucionId;
        Posicion = posicion;
        ArticuloId = articuloId;
        Cantidad = cantidad;
        UnidadMedida = unidadMedida;
        CostoUnitarioMxn = costoUnitarioMxn;
        MontoTotalMxn = Math.Round(cantidad * costoUnitarioMxn, 2);
        LineaRecepcionOrigenId = lineaRecepcionOrigenId;
    }
}
