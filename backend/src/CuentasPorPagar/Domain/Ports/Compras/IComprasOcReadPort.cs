namespace Millet.CuentasPorPagar.Domain.Ports.Compras;

/// <summary>
/// Puerto de lectura sobre Órdenes de Compra del módulo Compras (§6.1
/// del 01-diseno). CxP lo consume al capturar factura con OC para
/// validar tolerancia, listar líneas y resolver el saldo facturable.
///
/// <para>
/// F0-PR1 introduce el contrato + stub <c>NoOpComprasOcReadPort</c>.
/// Adapter real en F5-PR1 cuando se cablea contra el módulo Compras
/// (que sí está en runtime).
/// </para>
/// </summary>
public interface IComprasOcReadPort
{
    Task<OrdenCompraDto?> ObtenerAsync(Guid ordenCompraId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(
        Guid proveedorId,
        CancellationToken cancellationToken);
}

public sealed record OrdenCompraDto(
    Guid Id,
    string Folio,
    Guid ProveedorId,
    Guid EmpresaId,
    Guid SucursalId,
    decimal Total,
    string Estado,
    IReadOnlyList<LineaOcDto> Lineas);

public sealed record LineaOcDto(
    Guid Id,
    Guid ArticuloId,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal CantidadFacturada,
    decimal CantidadRecibida);
