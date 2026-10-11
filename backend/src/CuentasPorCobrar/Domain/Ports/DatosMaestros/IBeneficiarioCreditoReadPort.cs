namespace Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
public sealed record BeneficiarioCreditoDto(Guid Id, string Nombre, string Email);
public interface IBeneficiarioCreditoReadPort
{
    Task<bool> PuedeDecidirAsync(Guid usuarioId, Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<BeneficiarioCreditoDto>> ListarAsync(Guid empresaId, CancellationToken ct);
}
