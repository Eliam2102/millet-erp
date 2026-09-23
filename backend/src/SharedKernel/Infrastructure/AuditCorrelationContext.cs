using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Implementación de <see cref="IAuditCorrelationContext"/> vía
/// <see cref="AsyncLocal{T}"/>, mismo mecanismo que <see cref="AuditOriginContext"/>.
/// </summary>
public sealed class AuditCorrelationContext : IAuditCorrelationContext
{
    private static readonly AsyncLocal<Guid?> CorrelationValue = new();

    public Guid? CorrelationId => CorrelationValue.Value;

    public IDisposable Begin(Guid correlationId) => new CorrelationScope(correlationId);

    private sealed class CorrelationScope : IDisposable
    {
        private readonly Guid? _previous;

        public CorrelationScope(Guid correlationId)
        {
            _previous = CorrelationValue.Value;
            CorrelationValue.Value = correlationId;
        }

        public void Dispose() => CorrelationValue.Value = _previous;
    }
}
