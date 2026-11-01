namespace Millet.CuentasPorPagar.Domain.Ports.Tesoreria;
public sealed record PagoProveedorLectura(Guid EmpresaId, Guid FacturaId, Guid PagoId, DateOnly Fecha, decimal Importe, decimal CubiertoRepp, bool Revertido);
public interface IPagosProveedorReadPort
{
    Task<IReadOnlyList<PagoProveedorLectura>> ListarAsync(IReadOnlyCollection<Guid> facturas, CancellationToken ct);

    /// <summary>P8.3: fecha programada de pago (corrida autorizada) por factura; solo las que están en una. Sin adapter: ninguna.</summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> FechasProgramadasAsync(IReadOnlyCollection<Guid> facturas, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateOnly>>(new Dictionary<Guid, DateOnly>());
}
