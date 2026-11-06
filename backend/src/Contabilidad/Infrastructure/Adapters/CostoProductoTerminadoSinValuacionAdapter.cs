using Millet.Contabilidad.Application.Ports;

namespace Millet.Contabilidad.Infrastructure.Adapters;

/// <summary>
/// PCV (86e3ke10b): implementación provisional del costo de producto terminado. Aún no hay valuación de PT, así que siempre responde
/// <c>null</c> y la entrega queda en la bandeja de rechazos con «Dato faltante: costo de PT» (nunca se contabiliza en cero).
/// PLATFORM-TODO(&lt;ValuacionPT&gt;): PT5.12 (F1-PT-09) implementa <see cref="ICostoProductoTerminadoReadPort"/> en
/// <c>ProductoTerminado.Infrastructure.PublicAdapters</c> y se cablea en <c>Program.cs</c> después de <c>AddContabilidadModule</c>
/// (gana el último registro); entonces se borra esta clase y se reprocesan los rechazos de la bandeja.
/// </summary>
public sealed class CostoProductoTerminadoSinValuacionAdapter : ICostoProductoTerminadoReadPort
{
    public Task<CostoEntregaPt?> CostoEntregaAsync(Guid empresaId, Guid pedidoPtId, IReadOnlyList<PiezasPosicionPt> entregadas, CancellationToken ct) =>
        Task.FromResult<CostoEntregaPt?>(null);
}
