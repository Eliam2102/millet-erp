using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Integraciones.Aw.Application.Exceptions;

/// <summary>
/// Se lanza cuando un handler del módulo necesita resolver la empresa
/// actual desde <c>ICurrentEmpresaContext.Current</c> y obtiene
/// <c>null</c>. Indica que el caller no tiene contexto de empresa
/// (JWT sin <c>current_empresa_id</c> claim, o ejecución fuera de
/// request HTTP sin haber abierto el bypass del filtro de empresa).
///
/// <para>
/// Type Problem Details (PR D): <c>aw/missing_empresa_context</c>.
/// HTTP 400 Bad Request (el JWT debió traer la empresa).
/// </para>
///
/// <para>
/// Ubicada en el módulo y NO en SharedKernel (regla del prompt PR B
/// "no tocar SharedKernel salvo D-OUTBOX"). Si en PRs siguientes otro
/// módulo necesita la misma excepción, se promueve a SharedKernel ahí.
/// </para>
/// </summary>
public sealed class MissingEmpresaContextException : DomainException
{
    public override string Code => "AW_MISSING_EMPRESA_CONTEXT";

    public MissingEmpresaContextException()
        : base(
            "El handler requiere ICurrentEmpresaContext.Current poblado " +
            "pero la sesión actual no tiene empresa seleccionada (claim 'current_empresa_id' ausente o nulo).")
    {
    }
}
