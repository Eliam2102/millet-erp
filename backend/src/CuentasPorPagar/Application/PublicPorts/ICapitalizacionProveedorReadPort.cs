namespace Millet.CuentasPorPagar.Application.PublicPorts;
/// <summary>Valida el documento del proveedor que respalda un movimiento de valor de activo.</summary>
public interface ICapitalizacionProveedorReadPort
{
    Task<IReadOnlyList<DocumentoCapitalizacionOpcion>> BuscarAsync(bool notaCredito, Guid? facturaId, Guid sucursalId, string? q, CancellationToken ct) => Task.FromResult<IReadOnlyList<DocumentoCapitalizacionOpcion>>([]);
    Task<bool> ValidarMantenimientoAsync(Guid factura, Guid proveedor, Guid sucursal, CancellationToken ct) => Task.FromResult(false);
    Task<DocumentoCapitalizacion?> ObtenerAsync(Guid documentoId, Guid facturaId, bool notaCredito, CancellationToken ct);
}
public sealed record DocumentoCapitalizacion(Guid Id, Guid FacturaId, decimal BaseMxn);

public sealed record DocumentoCapitalizacionOpcion(Guid Id, Guid FacturaId, string Referencia, decimal BaseMxn);
