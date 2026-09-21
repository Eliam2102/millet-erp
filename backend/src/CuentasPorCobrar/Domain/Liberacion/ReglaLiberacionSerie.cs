using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>
/// Catálogo de reglas de liberación por serie de folio A+W (levantamiento
/// §2 + §3.1, CXC-PR4). Seed provisional del documento internacional §8.3
/// — ⚠️ gate suave: confirmar con Prida si la cartera nacional comparte
/// exactamente estas series (3000/4000/5000/7000/8000) o maneja otras;
/// el catálogo es tabla compartida y las carteras difieren solo en filas.
/// </summary>
public sealed class ReglaLiberacionSerie : BaseEntity, IAuditable
{
    /// <summary>Prefijo del folio del pedido A+W (match por StartsWith).</summary>
    public string Prefijo { get; private set; } = default!;

    public ComportamientoSerie Comportamiento { get; private set; }
    public bool Activo { get; private set; }

    private ReglaLiberacionSerie() { }

    public ReglaLiberacionSerie(Guid id, string prefijo, ComportamientoSerie comportamiento, bool activo = true)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(prefijo))
            throw new BusinessRuleException("RLS_PREFIJO_VACIO", "El prefijo de serie es obligatorio.");

        Prefijo = prefijo.Trim();
        Comportamiento = comportamiento;
        Activo = activo;
    }
}
