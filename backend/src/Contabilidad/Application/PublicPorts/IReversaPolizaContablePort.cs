namespace Millet.Contabilidad.Application.PublicPorts;
/// <summary>Reversa autorizada de una póliza concreta; conserva dimensiones, moneda y movimientos de cuenta puente.</summary>
public interface IReversaPolizaContablePort
{
    Task<ReversaPolizaContable> RevertirAsync(Guid polizaId, string motivo, DateOnly destino, Guid actor, CancellationToken ct);
}

public sealed record ReversaPolizaContable(Guid Id, DateOnly Fecha);
