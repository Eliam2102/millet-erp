using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// FAC-08: puerto de lectura de las salidas parciales de pedidos de Producto terminado
/// (PT-01/PT-03). El <c>SalidasParcialesWorker</c> las registra como <see cref="SalidaFacturable"/>.
/// Mientras PT no las publique es un stub vacío (<c>PLATFORM-TODO(&lt;SalidasPt&gt;)</c>). Los
/// contenedores de Comercio Exterior ya no pasan por aquí: llegan por el evento
/// <c>comercio_exterior.contenedor.embarcado.v1</c> (Fase 1). El registro manual por la API sigue
/// como respaldo.
/// </summary>
public interface ISalidasParcialesReader
{
    /// <summary>Lee hasta <paramref name="max"/> salidas. Reentregar una ya registrada es seguro.</summary>
    Task<IReadOnlyList<SalidaParcial>> LeerPendientesAsync(int max, CancellationToken cancellationToken);
}

/// <summary>Salida parcial de un pedido A+W: piezas por posición del pedido.</summary>
public sealed record SalidaParcial(
    string NumeroPedidoAw,
    OrigenSalidaFacturable Origen,
    string Referencia,
    IReadOnlyList<(int Posicion, decimal Cantidad)> Lineas);
