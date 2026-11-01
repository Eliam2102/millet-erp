using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// FAC-08: puerto de lectura de las salidas parciales de pedidos (salida de Producto
/// terminado PT-01/PT-03 o contenedor de Comercio Exterior CE-02). El
/// <c>SalidasParcialesWorker</c> las registra como <see cref="SalidaFacturable"/>. Mientras
/// PT y CE no las publiquen es un stub vacío (<c>PLATFORM-TODO(&lt;SalidasPt&gt;)</c>) y las
/// salidas se registran a mano por la API.
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
