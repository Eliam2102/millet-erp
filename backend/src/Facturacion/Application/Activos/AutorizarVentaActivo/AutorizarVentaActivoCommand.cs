using MediatR;

namespace Millet.Facturacion.Application.Activos.AutorizarVentaActivo;

/// <summary>
/// El Contador General autoriza la venta de un activo fijo (§7.8, F9): valida que
/// el activo esté dado de alta (IActivosFijosReadPort) y registra la autorización
/// con su valor en libros y depreciación (para el asiento de baja). Sin esta
/// autorización, la factura de venta de activo no se timbra.
/// </summary>
public sealed record AutorizarVentaActivoCommand(
    string ActivoRef,
    decimal PrecioVenta) : IRequest<AutorizarVentaActivoResponse>;

public sealed record AutorizarVentaActivoResponse(
    Guid AutorizacionId,
    string Descripcion,
    decimal ValorNetoEnLibros,
    decimal UtilidadOPerdida);
