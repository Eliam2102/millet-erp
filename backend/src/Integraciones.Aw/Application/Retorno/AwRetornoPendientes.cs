using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Retorno;

/// <summary>Estado de los avisos a A+W que esperan (M-160) para el tablero de integraciones.</summary>
public sealed record AwRetornoPendienteResumen(
    int Pendientes, int Detenidos, DateTimeOffset? MasAntiguoEnUtc, string? MasAntiguoTipoEvento, int? MasAntiguoIntentos,
    DateTimeOffset? MasAntiguoProximoIntentoUtc, string? MasAntiguoUltimoError, bool Alerta);

/// <summary>M-160: guarda y consulta los avisos a A+W que no se pudieron escribir por falta de conexión.</summary>
public sealed class AwRetornoPendientes(IntegracionesAwDbContext db, IClock clock)
{
    /// <summary>Un pendiente más viejo que esto enciende la alerta (log y tablero).</summary>
    public static readonly TimeSpan UmbralAlerta = TimeSpan.FromHours(4);
    private const int LotePorPasada = 200;

    public async Task GuardarAsync(string tipoEvento, string payload, string error, CancellationToken ct)
    {
        db.RetornoPendientes.Add(AwRetornoPendiente.Crear(tipoEvento, payload, error, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Pendientes cuyo siguiente intento ya venció, en el orden en que llegaron (la factura antes que su pago).</summary>
    public async Task<List<AwRetornoPendiente>> VencidosAsync(CancellationToken ct)
    {
        var ahora = clock.UtcNow;
        return await db.RetornoPendientes
            .Where(p => p.Estado == AwRetornoPendiente.EstadoPendiente && p.ProximoIntentoUtc <= ahora)
            .OrderBy(p => p.CreadoEnUtc).ThenBy(p => p.Id)
            .Take(LotePorPasada)
            .ToListAsync(ct);
    }

    public Task GuardarCambiosAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public DateTimeOffset Ahora => clock.UtcNow;

    public async Task<AwRetornoPendienteResumen> ResumenAsync(CancellationToken ct)
    {
        var pendientes = db.RetornoPendientes.AsNoTracking().Where(p => p.Estado == AwRetornoPendiente.EstadoPendiente);
        var total = await pendientes.CountAsync(ct);
        var detenidos = await db.RetornoPendientes.CountAsync(p => p.Estado == AwRetornoPendiente.EstadoDetenido, ct);
        var viejo = await pendientes.OrderBy(p => p.CreadoEnUtc).ThenBy(p => p.Id).FirstOrDefaultAsync(ct);
        return new AwRetornoPendienteResumen(total, detenidos, viejo?.CreadoEnUtc, viejo?.TipoEvento, viejo?.Intentos,
            viejo?.ProximoIntentoUtc, viejo?.UltimoError, viejo is not null && clock.UtcNow - viejo.CreadoEnUtc > UmbralAlerta);
    }
}

public sealed record ObtenerResumenRetornoPendienteQuery : IRequest<AwRetornoPendienteResumen>;

public sealed class ObtenerResumenRetornoPendienteHandler(AwRetornoPendientes pendientes)
    : IRequestHandler<ObtenerResumenRetornoPendienteQuery, AwRetornoPendienteResumen>
{
    public Task<AwRetornoPendienteResumen> Handle(ObtenerResumenRetornoPendienteQuery request, CancellationToken cancellationToken) =>
        pendientes.ResumenAsync(cancellationToken);
}
