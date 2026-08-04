using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Integraciones.Fiscal.Domain.Exceptions;

/// <summary>
/// Lanzada cuando un caller del módulo Integraciones.Fiscal necesita
/// credenciales del PAC pero no hay <see cref="ConfiguracionPac"/>
/// activa para la empresa actual. El resolver no puede sintetizar
/// credenciales — el admin debe crearlas vía
/// <c>/admin/integraciones/fiscal</c> (PR-4).
///
/// <para>Mapea a HTTP 422 vía middleware Problem Details (ADR-0010).</para>
/// </summary>
public sealed class ConfiguracionPacNoDisponibleException : DomainException
{
    public override string Code => "CONFIG_PAC_NO_DISPONIBLE";

    public Guid EmpresaId { get; }

    public ConfiguracionPacNoDisponibleException(Guid empresaId)
        : base($"No hay configuración de PAC activa para la empresa {empresaId}.")
    {
        EmpresaId = empresaId;
    }
}
