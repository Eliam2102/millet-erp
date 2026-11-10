using Millet.Facturacion.Domain.Facturas;

namespace Millet.Integraciones.Aw.Application.Pinturas;

/// <summary>
/// Opciones del lector de pedidos de Planta Pintura (I13.9) sobre <c>dbo.pinturas_pedido</c> de
/// <c>MILLET_INTEGRACION</c> (script 16 v3, doc integration/11). Sección <c>IntegracionesAw:Pinturas</c>.
/// La conexión es la misma del flujo 2 (<c>ConnectionStrings:AwIntegracionDb</c>, login <c>millet_erp_integracion</c>):
/// sin ella queda el stub de Facturación (0 pedidos) y el worker no lee nada.
/// </summary>
public sealed class PinturasPedidosOptions
{
    public const string SectionName = "IntegracionesAw:Pinturas";

    /// <summary>
    /// Clave (<c>compartido.sucursales.clave</c>) de la sucursal a la que se cargan los pedidos de Pinturas: la tabla
    /// no trae sucursal. Vacía o sin sucursal activa con esa clave → no se lee nada (las filas quedan pendientes).
    /// </summary>
    public string SucursalClave { get; set; } = string.Empty;

    /// <summary>Comportamiento fiscal del pedido (la tabla no lo trae). Supuesto a validar: venta de mostrador.</summary>
    public ComportamientoFiscal ComportamientoFiscal { get; set; } = ComportamientoFiscal.MostradorInmediato;

    /// <summary>Timeout por consulta (segundos).</summary>
    public int SqlQueryTimeoutSeconds { get; set; } = 5;
}

/// <summary>Resuelve la sucursal del ERP por su clave del catálogo compartido (solo activas).</summary>
public interface IPinturasSucursalResolver
{
    Task<Guid?> ResolverAsync(string clave, CancellationToken cancellationToken);
}
