using Microsoft.AspNetCore.Http;
using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Implementación de <see cref="ICurrentEmpresaContext"/> que lee la empresa
/// activa desde el claim <see cref="MilletClaimTypes.CurrentEmpresaId"/> del
/// JWT validado por el bearer middleware. El soporte de
/// <see cref="ICurrentEmpresaContext.Bypass"/> (background jobs, migrations,
/// tests) usa <see cref="AsyncLocal{T}"/> y NO lee del HttpContext.
///
/// <para>
/// Bypass usa <see cref="AsyncLocal{T}"/> para que múltiples requests/tests
/// concurrentes no interfieran. El IDisposable retornado restaura el estado
/// previo (soporta bypass anidado correctamente).
/// </para>
/// </summary>
public sealed class CurrentEmpresaContext : ICurrentEmpresaContext
{
    private static readonly AsyncLocal<bool> BypassFlag = new();

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentEmpresaContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? Current
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var raw = user.FindFirst(MilletClaimTypes.CurrentEmpresaId)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool IsBypassed => BypassFlag.Value;

    public IDisposable Bypass() => new BypassScope();

    private sealed class BypassScope : IDisposable
    {
        private readonly bool _previous;

        public BypassScope()
        {
            _previous = BypassFlag.Value;
            BypassFlag.Value = true;
        }

        public void Dispose() => BypassFlag.Value = _previous;
    }
}
