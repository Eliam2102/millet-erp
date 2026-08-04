using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

/// <summary>
/// Helper para tests unit que necesitan un <see cref="IntegracionesFiscalDbContext"/>
/// con EF Core InMemory provider. Útil para handlers que verifican lógica
/// de queries + mutaciones; constraints PG (UNIQUE, CHECK) se cubren en
/// integration tests con Postgres real.
/// </summary>
internal static class InMemoryFiscalDb
{
    public static IntegracionesFiscalDbContext Create(ICurrentEmpresaContext? empresaContext = null)
    {
        var options = new DbContextOptionsBuilder<IntegracionesFiscalDbContext>()
            .UseInMemoryDatabase($"fiscal-tests-{Guid.NewGuid():N}")
            .Options;
        return new IntegracionesFiscalDbContext(options, empresaContext ?? new BypassedEmpresaContext());
    }

    public static FiscalSecretCipher Cipher() => new(new EphemeralDataProtectionProvider());

    public sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public FakeEmpresaContext(Guid? current)
        {
            Current = current;
        }
        public Guid? Current { get; }
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    public sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    public sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }
        public DateTimeOffset UtcNow { get; set; }
    }

    public sealed class CapturingPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = new();
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    public sealed class TrackingResolver : IConfiguracionPacResolver
    {
        public List<(Guid EmpresaId, ProveedorPac Proveedor)> InvalidacionesRecibidas { get; } = new();

        public Task<ConfiguracionPacResuelta?> ResolverAsync(
            Guid empresaId, ProveedorPac proveedor, CancellationToken cancellationToken) =>
            Task.FromResult<ConfiguracionPacResuelta?>(null);

        public void Invalidar(Guid empresaId, ProveedorPac proveedor)
        {
            InvalidacionesRecibidas.Add((empresaId, proveedor));
        }
    }
}
