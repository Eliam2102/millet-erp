namespace Millet.CuentasPorPagar.Domain.Ports.Tesoreria;
public sealed record PagoProveedorLectura(Guid EmpresaId, Guid FacturaId, Guid PagoId, DateOnly Fecha, decimal Importe, decimal CubiertoRepp, bool Revertido);
public interface IPagosProveedorReadPort
{
    Task<IReadOnlyList<PagoProveedorLectura>> ListarAsync(IReadOnlyCollection<Guid> facturas, CancellationToken ct);
}
