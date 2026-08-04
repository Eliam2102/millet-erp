namespace Millet.CuentasPorPagar.Domain.Ports.Contabilidad;

/// <summary>
/// Puerto de lectura del catálogo de conceptos contables (módulo
/// Contabilidad, hoy stub). CxP lo consume al capturar líneas de factura
/// y comprobaciones para asignar cuenta contable (§6.1 del 01-diseno).
///
/// <para>
/// F0-PR1 introduce el contrato + stub <c>NoOpConceptoContableReadPort</c>.
/// PLATFORM-TODO(&lt;ContabilidadConceptos&gt;): adapter real cuando el
/// módulo Contabilidad entre en runtime.
/// </para>
/// </summary>
public interface IConceptoContableReadPort
{
    Task<ConceptoContableDto?> ObtenerAsync(Guid conceptoId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConceptoContableDto>> ListarAsync(CancellationToken cancellationToken);
}

public sealed record ConceptoContableDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string CuentaContable,
    bool Activo);
