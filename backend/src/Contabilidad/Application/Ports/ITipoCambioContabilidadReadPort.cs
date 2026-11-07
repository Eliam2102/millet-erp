namespace Millet.Contabilidad.Application.Ports;

/// <summary>
/// CON-11 (86e3kdypd) · puerto de CONSUMIDOR: tipo de cambio del catálogo (K10.6, <c>compartido.tipos_cambio</c>, escalera FIX → DOF)
/// para la diferencia cambiaria realizada y la revaluación de cierre. Lo implementa Compartido; Contabilidad no lee su esquema.
/// </summary>
public interface ITipoCambioContabilidadReadPort
{
    /// <summary>Pesos por una unidad de <paramref name="moneda"/> en <paramref name="fecha"/>; MXN = 1; null si el catálogo no lo tiene.</summary>
    Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken ct);
}
