using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Polizas;

// M-212 (86e3zr7pq): anulación de póliza con segunda firma. Una persona con el permiso de anular la solicita (la póliza sigue
// autorizada); otra con el permiso de aprobar la aprueba —entonces se ejecuta la anulación K10.3 con el motivo de la solicitud— o la
// rechaza con motivo. La bitácora central registra la solicitud y la decisión (entidad auditable).

public sealed record SolicitudAnulacionResponse(
    Guid Id, Guid PolizaId, string? PolizaFolio, string PolizaConcepto, DateOnly PolizaFecha, decimal PolizaTotal, string Motivo,
    int? PeriodoAnio, int? PeriodoNumero, EstadoSolicitudAnulacion Estado, Guid? SolicitadaPorId, string SolicitadaPor,
    DateTimeOffset SolicitadaEn, Guid? DecididaPorId, string? DecididaPor, DateTimeOffset? DecididaEn, string? MotivoRechazo,
    Guid? PolizaAnulacionId, int Version);

/// <summary>Lo que la póliza muestra mientras su anulación espera aprobación.</summary>
public sealed record AnulacionPendienteResponse(Guid SolicitudId, string Motivo, Guid? SolicitadaPorId, string SolicitadaPor,
    DateTimeOffset SolicitadaEn);

/// <summary>Pide anular una póliza autorizada; verifica todo lo que exigirá la anulación (periodo destino incluido).</summary>
public sealed record SolicitarAnulacionPolizaCommand(Guid PolizaId, int VersionEsperada, string Motivo, int? PeriodoAnio = null,
    int? PeriodoNumero = null) : IRequest<SolicitudAnulacionResponse>;

public sealed class SolicitarAnulacionPolizaValidator : AbstractValidator<SolicitarAnulacionPolizaCommand>
{
    public SolicitarAnulacionPolizaValidator()
    {
        RuleFor(c => c.Motivo).Must(m => m is not null && m.Trim().Length is >= Poliza.MotivoMinimo and <= Poliza.MotivoMaximo)
            .WithMessage($"Indique el motivo (entre {Poliza.MotivoMinimo} y {Poliza.MotivoMaximo} caracteres).");
        RuleFor(c => c).Must(c => (c.PeriodoAnio is null) == (c.PeriodoNumero is null)).WithName("periodoNumero")
            .WithMessage("Indique el año y el número del periodo destino, o ninguno.");
        RuleFor(c => c.PeriodoNumero).InclusiveBetween(1, PeriodoContable.NumeroAjuste).When(c => c.PeriodoNumero is not null);
    }
}

public sealed class SolicitarAnulacionPolizaHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<SolicitarAnulacionPolizaCommand, SolicitudAnulacionResponse>
{
    public async Task<SolicitudAnulacionResponse> Handle(SolicitarAnulacionPolizaCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var poliza = await PoliticaPolizas.CargarAsync(db, request.PolizaId, request.VersionEsperada, ct);
        foreach (var s in poliza.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, ct);
        if (await db.SolicitudesAnulacionPoliza.AnyAsync(x => x.PolizaId == poliza.Id && x.Estado == EstadoSolicitudAnulacion.Pendiente, ct))
            throw Duplicada(poliza);
        var destino = request.PeriodoAnio is { } a && request.PeriodoNumero is { } n ? (a, n) : ((int, int)?)null;
        await registro.ValidarAnulacionAsync(poliza, request.Motivo, destino, FechaContable.HoyLocal(clock.UtcNow), ct);

        var solicitud = new SolicitudAnulacionPoliza(Guid.CreateVersion7(), poliza.Id, request.Motivo, request.PeriodoAnio,
            request.PeriodoNumero, usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow);
        db.SolicitudesAnulacionPoliza.Add(solicitud);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: SolicitudAnulacionPolizaConfiguration.UxPendiente,
        })
        {
            throw Duplicada(poliza);
        }
        return LecturaSolicitudesAnulacion.Response(solicitud, poliza);
    }

    private static ConflictException Duplicada(Poliza p) => new("CONTAB_ANULACION_YA_SOLICITADA",
        $"La póliza {p.Folio} ya tiene una solicitud de anulación pendiente de aprobación.");
}

/// <summary>Aprueba la solicitud (otra persona): ejecuta la anulación K10.3 con su motivo y devuelve la póliza de anulación.</summary>
public sealed record AprobarAnulacionPolizaCommand(Guid SolicitudId) : IRequest<PolizaResponse>;

public sealed class AprobarAnulacionPolizaHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, ICurrentUserContext usuario, IClock clock,
    LecturaPolizas lectura)
    : IRequestHandler<AprobarAnulacionPolizaCommand, PolizaResponse>
{
    public async Task<PolizaResponse> Handle(AprobarAnulacionPolizaCommand request, CancellationToken cancellationToken)
    {
        Poliza? anulacion = null;
        await registro.EnTransaccionAsync(async ct =>
        {
            var solicitud = await LecturaSolicitudesAnulacion.CargarAsync(db, request.SolicitudId, ct);
            solicitud.ExigirDecidible(usuario.UserId);
            var original = await db.Polizas.Include(x => x.Partidas).FirstOrDefaultAsync(x => x.Id == solicitud.PolizaId, ct)
                ?? throw new EntityNotFoundException("CONTAB_POLIZA_NO_ENCONTRADA", $"No existe la póliza '{solicitud.PolizaId}'.");
            foreach (var s in original.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, ct);
            var destino = solicitud.PeriodoAnio is { } a && solicitud.PeriodoNumero is { } n ? (a, n) : ((int, int)?)null;
            anulacion = await registro.AnularAsync(original, solicitud.Motivo, destino, FechaContable.HoyLocal(clock.UtcNow),
                usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow, ct);
            solicitud.Aprobar(usuario.UserId, PoliticaPolizas.Usuario(usuario), anulacion.Id, clock.UtcNow);
            await PoliticaPolizas.GuardarAnulacionAsync(db, ct);
        }, cancellationToken);
        return await lectura.ObtenerAsync(anulacion!.Id, cancellationToken);
    }
}

/// <summary>Rechaza la solicitud con motivo (otra persona): la póliza no cambia y se puede pedir otra anulación.</summary>
public sealed record RechazarAnulacionPolizaCommand(Guid SolicitudId, string Motivo) : IRequest<SolicitudAnulacionResponse>;

public sealed class RechazarAnulacionPolizaValidator : AbstractValidator<RechazarAnulacionPolizaCommand>
{
    public RechazarAnulacionPolizaValidator() =>
        RuleFor(c => c.Motivo).Must(m => m is not null && m.Trim().Length is >= Poliza.MotivoMinimo and <= Poliza.MotivoMaximo)
            .WithMessage($"Indique el motivo del rechazo (entre {Poliza.MotivoMinimo} y {Poliza.MotivoMaximo} caracteres).");
}

public sealed class RechazarAnulacionPolizaHandler(
    ContabilidadDbContext db, AlcanceSucursalContable alcance, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<RechazarAnulacionPolizaCommand, SolicitudAnulacionResponse>
{
    public async Task<SolicitudAnulacionResponse> Handle(RechazarAnulacionPolizaCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var solicitud = await LecturaSolicitudesAnulacion.CargarAsync(db, request.SolicitudId, ct);
        var poliza = await db.Polizas.AsNoTracking().Include(x => x.Partidas).FirstAsync(x => x.Id == solicitud.PolizaId, ct);
        foreach (var s in poliza.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, ct);
        solicitud.Rechazar(usuario.UserId, PoliticaPolizas.Usuario(usuario), request.Motivo, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return LecturaSolicitudesAnulacion.Response(solicitud, poliza);
    }
}

/// <summary>Bandeja de solicitudes (por omisión, las pendientes), la más antigua primero.</summary>
public sealed record ListarSolicitudesAnulacionQuery(EstadoSolicitudAnulacion? Estado = EstadoSolicitudAnulacion.Pendiente, int Limit = 200)
    : IRequest<IReadOnlyList<SolicitudAnulacionResponse>>;

public sealed class ListarSolicitudesAnulacionHandler(ContabilidadDbContext db)
    : IRequestHandler<ListarSolicitudesAnulacionQuery, IReadOnlyList<SolicitudAnulacionResponse>>
{
    public async Task<IReadOnlyList<SolicitudAnulacionResponse>> Handle(ListarSolicitudesAnulacionQuery request, CancellationToken cancellationToken)
    {
        var q = db.SolicitudesAnulacionPoliza.AsNoTracking();
        if (request.Estado is { } estado) q = q.Where(x => x.Estado == estado);
        var solicitudes = await q.OrderBy(x => x.SolicitadaEn).Take(Math.Clamp(request.Limit, 1, 500)).ToListAsync(cancellationToken);
        var ids = solicitudes.Select(x => x.PolizaId).Distinct().ToList();
        var polizas = await db.Polizas.AsNoTracking().Include(x => x.Partidas).Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return [.. solicitudes.Where(x => polizas.ContainsKey(x.PolizaId)).Select(x => LecturaSolicitudesAnulacion.Response(x, polizas[x.PolizaId]))];
    }
}

internal static class LecturaSolicitudesAnulacion
{
    public static async Task<SolicitudAnulacionPoliza> CargarAsync(ContabilidadDbContext db, Guid id, CancellationToken ct) =>
        await db.SolicitudesAnulacionPoliza.FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new EntityNotFoundException("CONTAB_ANULACION_NO_ENCONTRADA", $"No existe la solicitud de anulación '{id}'.");

    public static SolicitudAnulacionResponse Response(SolicitudAnulacionPoliza s, Poliza p) => new(
        s.Id, s.PolizaId, p.Folio, p.Concepto, p.Fecha, p.TotalCargos, s.Motivo, s.PeriodoAnio, s.PeriodoNumero, s.Estado, s.SolicitadaPorId,
        s.SolicitadaPor, s.SolicitadaEn, s.DecididaPorId, s.DecididaPor, s.DecididaEn, s.MotivoRechazo, s.PolizaAnulacionId, s.Version);
}
