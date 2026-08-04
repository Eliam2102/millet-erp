namespace Millet.Identidad.Application.Ports;

/// <summary>
/// Out-port que mapea un token validado de Entra (claim <c>appid</c>) a
/// un <see cref="ResolvedServicePrincipal"/> con sus permisos efectivos.
///
/// <para>
/// La resolución se hace exclusivamente por <c>EntraAppId</c> (D-RES del
/// prompt PR A): en single-tenant el AppId es suficiente. El
/// <c>EntraObjectId</c> del token se persiste pero NO se valida contra el
/// guardado en BD; eso queda como
/// <c>PLATFORM-TODO(&lt;MultiTenantSpResolution&gt;)</c> para multi-tenant futuro.
/// </para>
///
/// <para>
/// Devuelve <c>null</c> en 3 casos: (a) ningún SP registrado tiene ese
/// <c>EntraAppId</c>, (b) existe pero <c>Activo=false</c>, (c) la empresa
/// asociada ya no existe / está borrada. Los 2 primeros se distinguen
/// vía <see cref="ServicePrincipalResolutionFailure"/> para que el handler
/// de auth devuelva el Problem Details type correcto
/// (<c>auth/unknown_service_principal</c> vs
/// <c>auth/service_principal_disabled</c>).
/// </para>
/// </summary>
public interface IServicePrincipalResolver
{
    Task<ServicePrincipalResolutionResult> ResolveAsync(
        Guid entraAppId,
        Guid entraObjectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultado de la resolución: <see cref="ResolvedServicePrincipal"/> si
/// se resolvió OK, o <see cref="ServicePrincipalResolutionFailure"/> con
/// el motivo de la falla si no.
/// </summary>
public sealed record ServicePrincipalResolutionResult(
    ResolvedServicePrincipal? Principal,
    ServicePrincipalResolutionFailure? Failure)
{
    public static ServicePrincipalResolutionResult Found(ResolvedServicePrincipal principal) =>
        new(principal, null);

    public static ServicePrincipalResolutionResult NotFound() =>
        new(null, ServicePrincipalResolutionFailure.UnknownAppId);

    public static ServicePrincipalResolutionResult Disabled() =>
        new(null, ServicePrincipalResolutionFailure.Disabled);
}

/// <summary>
/// Datos efectivos del SP que el handler de auth pone en los claims del
/// <c>ClaimsPrincipal</c>. Inmutable; refleja el snapshot al momento de
/// la resolución (la cache puede invalidarlo hasta 5 min después).
/// </summary>
public sealed record ResolvedServicePrincipal(
    Guid Id,
    string Nombre,
    Guid EntraAppId,
    Guid EmpresaId,
    IReadOnlyList<string> Permisos);

public enum ServicePrincipalResolutionFailure
{
    UnknownAppId = 1,
    Disabled = 2,
}
