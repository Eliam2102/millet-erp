namespace Millet.SharedKernel.Application;

/// <summary>
/// Validación de <c>Auth:Mode</c> en arranque. El modo <see cref="AuthMode.FakeForLocalDev"/>
/// solo está permitido cuando <c>ASPNETCORE_ENVIRONMENT == "Development"</c>:
/// fuera de eso, fail-fast en arranque para evitar que jamás llegue a un
/// ambiente no-dev por accidente. Ver ADR-0015.
/// </summary>
public static class AuthModeValidator
{
    /// <summary>
    /// Lanza <see cref="InvalidOperationException"/> si <paramref name="mode"/>
    /// es <see cref="AuthMode.FakeForLocalDev"/> y <paramref name="isDevelopment"/>
    /// es false. No-op en otros casos.
    /// </summary>
    public static void EnsureAllowedForEnvironment(AuthMode mode, bool isDevelopment)
    {
        if (mode == AuthMode.FakeForLocalDev && !isDevelopment)
        {
            throw new InvalidOperationException(
                "Auth:Mode='FakeForLocalDev' no está permitido fuera del ambiente Development. " +
                "El modo fake solo debe activarse para desarrollo local; QA y Producción deben usar EntraId. " +
                "Ver ADR-0015.");
        }
    }
}
