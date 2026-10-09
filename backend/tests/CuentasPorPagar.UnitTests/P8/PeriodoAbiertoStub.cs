namespace Millet.CuentasPorPagar.UnitTests.P8;

internal sealed class PeriodoAbiertoStub : Domain.Ports.Contabilidad.IPeriodoContablePort, SharedKernel.Application.IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
}
