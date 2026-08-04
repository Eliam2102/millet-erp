namespace Millet.Api.Web;

/// <summary>
/// Marca un endpoint como obligatoriamente idempotente: el cliente debe
/// incluir el header <c>Idempotency-Key</c>. Sin lógica propia — el
/// <see cref="IdempotencyMiddleware"/> lee este metadata desde el endpoint
/// resuelto por routing y aplica el flujo del ADR-0020.
///
/// <para>
/// Convención: decorar todos los <c>POST</c>/<c>PATCH</c>/<c>DELETE</c> de
/// mutación con impacto fiscal o financiero (timbrar CFDI, aplicar pago,
/// crear OC, etc). Ver §"Endpoints que exigen idempotencia" del ADR-0020.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RequireIdempotencyKeyAttribute : Attribute
{
}
