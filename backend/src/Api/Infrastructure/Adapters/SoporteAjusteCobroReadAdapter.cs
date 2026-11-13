using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Domain.Ports.Adjuntos;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// M-240: ¿la propuesta de aplicación tiene soporte vigente de su ajuste? Lee el servicio genérico de adjuntos (G1.2) sin exponer el
/// blob (ADR-0058). Vigente = sin baja.
/// </summary>
public sealed class SoporteAjusteCobroReadAdapter(CompartidoDbContext db) : ISoporteAjusteCobroReadPort
{
    public Task<bool> TieneSoporteAsync(Guid propuestaId, CancellationToken ct) =>
        db.Adjuntos.AsNoTracking().AnyAsync(a => a.TipoEntidad == SoporteAjusteCobro.TipoEntidad && a.EntidadId == propuestaId && a.BajaEn == null, ct);
}
