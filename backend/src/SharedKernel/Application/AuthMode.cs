namespace Millet.SharedKernel.Application;

/// <summary>
/// Modo de autenticación del backend, leído desde <c>Auth:Mode</c> en
/// configuración. Determina si el JWT viene de Entra ID real o de un
/// firmador local de desarrollo (ver ADR-0015).
/// </summary>
public enum AuthMode
{
    /// <summary>Producción/QA: el backend valida JWTs de Microsoft Entra ID.</summary>
    EntraId = 0,

    /// <summary>
    /// Desarrollo local: el backend acepta JWTs firmados con clave simétrica
    /// conocida y expone <c>POST /api/dev/fake-login</c>. Compilación
    /// condicional + validación en arranque garantizan que NUNCA llegue a
    /// QA o Producción.
    /// </summary>
    FakeForLocalDev = 1,
}
