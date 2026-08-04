using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Integraciones.Fiscal.Domain.Exceptions;

/// <summary>
/// Lanzada cuando la búsqueda de catálogos SAT vía FiscalAPI no puede
/// atenderse: SDK deshabilitado (<c>IntegracionesFiscal:Sdk</c> sin
/// TenantKey), empresa sin <see cref="ConfiguracionPac"/> activa, o
/// error del PAC. El frontend degrada a captura manual del código.
///
/// <para>Mapea a HTTP 503 vía <c>GlobalExceptionHandler</c> (ADR-0010):
/// una lista vacía mentiría al operador ("la clave no existe") cuando
/// el problema es de disponibilidad, no de datos.</para>
/// </summary>
public sealed class CatalogoSatNoDisponibleException : DomainException
{
    public override string Code => "CATALOGO_SAT_NO_DISPONIBLE";

    public CatalogoSatNoDisponibleException(string motivo)
        : base($"El catálogo SAT no está disponible: {motivo}")
    {
    }

    public CatalogoSatNoDisponibleException(string motivo, Exception inner)
        : base($"El catálogo SAT no está disponible: {motivo}", inner)
    {
    }
}
