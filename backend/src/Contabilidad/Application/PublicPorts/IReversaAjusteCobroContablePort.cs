namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// M-240: CxC pide a Contabilidad la póliza contraria del ajuste de un cobro que acaba de revertir. Contabilidad lee la reversa por
/// <see cref="IAjusteCobroContablePort"/> (mismo camino que el ajuste original) y la liga a la póliza del pago. Idempotente por
/// propuesta: llamarla otra vez devuelve la misma póliza.
/// </summary>
public interface IReversaAjusteCobroContablePort
{
    Task<PolizaRegistrada> RegistrarAsync(Guid empresaId, Guid propuestaId, CancellationToken ct);
}
