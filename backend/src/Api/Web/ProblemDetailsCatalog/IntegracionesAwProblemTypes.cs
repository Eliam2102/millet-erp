namespace Millet.Api.Web.ProblemDetailsCatalog;

/// <summary>
/// URLs de Problem Details (RFC 7807) emitidas por endpoints del módulo
/// Integraciones.Aw (PR D). Útil como referencia documental + para
/// asserts en integration tests.
///
/// <para>
/// El URL se genera automáticamente desde el <c>Code</c> de la
/// excepción de dominio en <c>GlobalExceptionHandler.CreateProblem</c>
/// vía <c>$"https://millet-erp/errors/{code.ToLowerInvariant()}"</c>.
/// Los valores aquí son los que el handler producirá para cada caso.
/// </para>
///
/// <para>
/// Mapeo a HTTP status (heredado del switch del handler):
/// <list type="bullet">
///   <item>404 — <see cref="CotizacionNoEncontrada"/> (EntityNotFoundException)</item>
///   <item>409 — <see cref="QuoteReferenceDuplicada"/>, <see cref="EstadoInvalido"/>,
///         <see cref="ReintentoNoPermitido"/> (todas ConflictException)</item>
///   <item>409 — <see cref="IdempotencyConflict"/> (IdempotencyInProgressException — middleware)</item>
///   <item>403 — <see cref="PermisoInsuficiente"/>, <see cref="EmpresaInvalida"/>
///         (ForbiddenException / CrossTenantViolationException)</item>
///   <item>422 — <see cref="NotaRequerida"/> (BusinessRuleException)</item>
/// </list>
/// </para>
/// </summary>
public static class IntegracionesAwProblemTypes
{
    private const string Prefix = "https://millet-erp/errors/";

    public const string CotizacionNoEncontrada = Prefix + "aw_cotizacion_no_encontrada";

    /// <summary>409 — QuoteReferenceDuplicadaException.</summary>
    public const string QuoteReferenceDuplicada = Prefix + "aw_quote_reference_duplicada";

    /// <summary>409 — InvalidStateTransitionException (genérico — incluye reintento no permitido).</summary>
    public const string EstadoInvalido = Prefix + "aw_invalid_state_transition";

    /// <summary>409 — alias semántico de EstadoInvalido para reintento no permitido.</summary>
    public const string ReintentoNoPermitido = EstadoInvalido;

    /// <summary>409 — IdempotencyInProgressException (middleware).</summary>
    public const string IdempotencyConflict = Prefix + "idempotency_in_progress";

    /// <summary>403 — ForbiddenException de operador no autorizado.</summary>
    public const string PermisoInsuficiente = Prefix + "forbidden";

    /// <summary>403 — CrossTenantViolationException (acceso cross-empresa).</summary>
    public const string EmpresaInvalida = Prefix + "cross_tenant_violation";

    /// <summary>422 — BusinessRuleException por nota requerida en MarcarResueltoManual.</summary>
    public const string NotaRequerida = Prefix + "aw_nota_requerida";
}
