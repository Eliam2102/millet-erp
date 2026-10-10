using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.UnitTests;

/// <summary>Base aislada P6 con periodos ficticios abiertos; no modifica la validación contable P8.</summary>
internal static class CxpP6TestContext
{
    public static CuentasPorPagarDbContext Crear(ICurrentEmpresaContext empresa) => new(
        new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        empresa, new PeriodosAbiertos(), new Reloj());

    private sealed class PeriodosAbiertos : IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
