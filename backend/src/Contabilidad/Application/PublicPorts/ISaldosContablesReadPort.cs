namespace Millet.Contabilidad.Application.PublicPorts;
/// <summary>Saldo deudor (cargo menos abono) autorizado, incluida la reversa de pólizas anuladas.</summary>
public interface ISaldosContablesReadPort
{
    Task<IReadOnlyDictionary<Guid, decimal>> ObtenerAsync(DateOnly corte, IReadOnlyCollection<Guid> cuentas, IReadOnlyCollection<Guid>? sucursales, CancellationToken ct);
}
