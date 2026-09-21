using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Implementación de <see cref="IAuditOriginContext"/> vía <see cref="AsyncLocal{T}"/>,
/// mismo mecanismo que <see cref="CurrentEmpresaContext.Bypass"/>. El estado
/// vive en el <c>AsyncLocal</c> estático, no en la instancia, por lo que el
/// ciclo de vida de registro en DI (scoped o singleton) es indistinto.
/// </summary>
public sealed class AuditOriginContext : IAuditOriginContext
{
    private static readonly AsyncLocal<string?> OriginValue = new();

    public string? Origin => OriginValue.Value;

    public IDisposable SetOrigin(string origin) => new OriginScope(origin);

    private sealed class OriginScope : IDisposable
    {
        private readonly string? _previous;

        public OriginScope(string origin)
        {
            _previous = OriginValue.Value;
            OriginValue.Value = origin;
        }

        public void Dispose() => OriginValue.Value = _previous;
    }
}
