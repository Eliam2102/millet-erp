using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Ports.OrdenCompra;

/// <summary>
/// DTO de entrada de <see cref="IGenerarSolicitudCompraPort"/>: línea de
/// requisición que NO quedó cubierta por reserva de almacén y debe
/// canalizarse a una orden de compra borrador (diseño §8.2).
///
/// <para>
/// Lleva todo lo que OC necesita para crear su línea sin volver a
/// llamar a Compras: ids referenciales (línea de origen, artículo),
/// cantidad de saldo, unidad, precio estimado, y los campos contables
/// opcionales que viajan con la requisición original (cuenta, centro
/// de costo, proyecto).
/// </para>
/// </summary>
public sealed record LineaSaldo(
    Guid LineaRequisicionId,
    Guid ArticuloId,
    decimal CantidadSaldo,
    string UnidadMedida,
    Money PrecioEstimado,
    Guid? CuentaContableId,
    Guid? CentroCostoId,
    string? Proyecto);
