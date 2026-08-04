using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Application;

/// <summary>
/// Helper para tests unit que necesitan un <see cref="IntegracionesAwDbContext"/>
/// real (handlers que persisten cambios). Usa EF Core InMemory provider —
/// no SQL real, pero suficiente para verificar lógica de queries +
/// mutaciones del handler.
///
/// <para>
/// Limitaciones del InMemory provider: no transacciones, no constraints
/// de Postgres (UNIQUE, CHECK), no JSON queries. Para tests E2E de esos
/// aspectos usar <c>Aw.IntegrationTests</c> con Postgres real.
/// </para>
/// </summary>
internal static class InMemoryDb
{
    public static Task<IntegracionesAwDbContext> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<IntegracionesAwDbContext>()
            .UseInMemoryDatabase($"aw-tests-{Guid.NewGuid():N}")
            .Options;
        var ctx = new IntegracionesAwDbContext(options, new BypassedEmpresaContext());
        return Task.FromResult(ctx);
    }

    /// <summary>
    /// Implementación de <see cref="ICurrentEmpresaContext"/> en estado
    /// <c>IsBypassed=true</c> permanente — los tests de handler no llegan
    /// vía request HTTP, así que el global query filter del DbContext
    /// debe estar desactivado.
    /// </summary>
    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
