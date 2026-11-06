namespace Millet.Compartido.Application.Ports;

public enum TipoMaestro { Proveedor, Cliente, Articulo }

/// <summary>
/// Lo que un módulo tiene abierto con un maestro (OC, saldo, pedidos). Cada módulo dueño del dato
/// implementa su parte y devuelve una línea legible por cada pendiente; si no aplica al tipo, lista vacía.
/// Los consume <c>MaestroEstatusService</c> antes de inactivar un proveedor, cliente o artículo.
/// </summary>
public interface IPendientesMaestroReadPort
{
    Task<IReadOnlyList<string>> ObtenerAsync(TipoMaestro tipo, Guid id, CancellationToken cancellationToken);
}
