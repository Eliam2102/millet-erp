using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Utilidades comunes de los handlers de adjuntos.</summary>
internal static class AdjuntoSoporte
{
    private static readonly TimeZoneInfo ZonaMexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    /// <summary>"Hoy" para vigencia: fecha en America/Mexico_City derivada de <see cref="IClock"/>.</summary>
    public static DateOnly Hoy(IClock clock)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, ZonaMexico).DateTime);

    /// <summary>
    /// Busca el adjunto dentro de SU padre. Uno de otro padre responde igual que uno inexistente
    /// (404 <c>ADJUNTO_NO_ENCONTRADO</c>): sin IDOR ni pistas de existencia.
    /// </summary>
    public static async Task<Adjunto> CargarAsync(
        CompartidoDbContext db, string tipoEntidad, Guid entidadId, Guid adjuntoId, bool tracking, CancellationToken ct)
    {
        var q = db.Adjuntos.Where(a => a.Id == adjuntoId && a.TipoEntidad == tipoEntidad && a.EntidadId == entidadId);
        if (!tracking) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException(
                "ADJUNTO_NO_ENCONTRADO", $"No se encontró el adjunto '{adjuntoId}'.");
    }

    public static Task<AdjuntoTipoDocumento> CargarTipoAsync(CompartidoDbContext db, Guid tipoId, CancellationToken ct)
        => db.AdjuntoTiposDocumento.AsNoTracking().FirstAsync(t => t.Id == tipoId, ct);
}
