using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// Detalle de una serie con preview del próximo folio (F-Admin-PR6.1).
/// El preview se calcula contra la fila de
/// <see cref="SecuenciaFolio"/> del período actual (basado en
/// <see cref="IClock.UtcNow"/>); si no existe la fila aún, el preview
/// muestra <c>...-000001</c>.
/// </summary>
public sealed record ObtenerSerieQuery(Guid Id) : IRequest<SerieDetalleResponse>;

public sealed class ObtenerSerieHandler
    : IRequestHandler<ObtenerSerieQuery, SerieDetalleResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly IClock _clock;

    public ObtenerSerieHandler(CompartidoDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SerieDetalleResponse> Handle(
        ObtenerSerieQuery query, CancellationToken cancellationToken)
    {
        var serie = await _db.Series.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SERIE_NO_ENCONTRADA",
                $"No existe serie con id '{query.Id}'.");

        var fechaHoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var periodo = Serie.CalcularPeriodoClave(serie.ReinicioPeriodo, fechaHoy);

        var ultimo = await _db.SecuenciasFolio.AsNoTracking()
            .Where(x => x.SerieId == serie.Id && x.PeriodoClave == periodo)
            .Select(x => (long?)x.UltimoNumero)
            .FirstOrDefaultAsync(cancellationToken);

        var siguiente = (ultimo ?? 0) + 1;
        var preview = FormatearFolio(serie, periodo, siguiente);

        var dto = new SerieResponse(
            serie.Id, serie.EmpresaId, serie.SucursalId, serie.TipoDocumento,
            serie.Prefijo, serie.Sufijo, serie.ReinicioPeriodo,
            serie.Activa, serie.Version);

        return new SerieDetalleResponse(dto, preview);
    }

    internal static string FormatearFolio(Serie serie, string periodoClave, long numero)
    {
        // Formato:
        //   None    → "{Prefijo}{Sufijo?}-{numero:D6}"
        //   Anual   → "{Prefijo}-{YYYY}-{numero:D6}"
        //   Mensual → "{Prefijo}-{YYYY-MM}-{numero:D6}"
        var numStr = numero.ToString("D6", CultureInfo.InvariantCulture);
        if (serie.ReinicioPeriodo == ReinicioPeriodo.None)
        {
            var sufijo = serie.Sufijo ?? string.Empty;
            return $"{serie.Prefijo}{sufijo}-{numStr}";
        }
        return $"{serie.Prefijo}-{periodoClave}-{numStr}";
    }
}
