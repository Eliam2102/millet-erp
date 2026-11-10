namespace Millet.Contabilidad.Application.Ports;

/// <summary>
/// M-207 (86e3zr7nf) · puerto de CONSUMIDOR: tolerancia (%) entre el TC del CFDI y el del catálogo, del parámetro global
/// <c>contabilidad.tipo-cambio.tolerancia-pct</c>. Lo implementa Compartido; 0 apaga la comparación.
/// </summary>
public interface IToleranciaTipoCambioReadPort
{
    Task<decimal> ToleranciaPctAsync(CancellationToken ct);
}
