using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Depositos;

// ============================================================================
// 86e3vr1tj (Millet 23-oct): bandeja «Diferencias de corte». Tesorería autoriza o rechaza (con
// motivo → «Por aclarar») el faltante/sobrante de un corte; el cajero responsable no puede resolver
// la suya (dominio). Autorizar publica el evento para la póliza contra deudores funcionarios y empleados.
// ============================================================================

public sealed record DiferenciaCorteResponse(
    Guid Id, Guid CajaSesionId, Guid CajaId, Guid SucursalId, Guid CajeroId, DateOnly DiaCorte,
    decimal Importe, string Tipo, EstadoDiferenciaCorte Estado, string? MotivoRechazo,
    decimal ImporteDevuelto, decimal PendientePorDevolver, Guid? ResueltaPor, DateTimeOffset? ResueltaEn,
    DateTimeOffset RecibidoEn, int Version)
{
    public static DiferenciaCorteResponse De(DiferenciaCorte d) =>
        new(d.Id, d.CajaSesionId, d.CajaId, d.SucursalId, d.CajeroId, d.DiaCorte, d.Importe,
            d.EsFaltante ? "Faltante" : "Sobrante", d.Estado, d.MotivoRechazo, d.ImporteDevuelto,
            d.PendientePorDevolver, d.ResueltaPor, d.ResueltaEn, d.CreatedAt, d.Version);
}

// --------------------------------------------------- Listar

public sealed record DiferenciasCorteQuery(
    EstadoDiferenciaCorte? Estado = null, int Offset = 0, int Limit = 50)
    : IRequest<PagedResponse<DiferenciaCorteResponse>>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "diferencia_corte";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class DiferenciasCorteHandler(TesoreriaDbContext db)
    : IRequestHandler<DiferenciasCorteQuery, PagedResponse<DiferenciaCorteResponse>>
{
    public async Task<PagedResponse<DiferenciaCorteResponse>> Handle(DiferenciasCorteQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = db.DiferenciasCorte.AsNoTracking()
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id));
        if (query.Estado is EstadoDiferenciaCorte estado) q = q.Where(d => d.Estado == estado);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(d => d.Estado == EstadoDiferenciaCorte.PendienteAutorizar ? 0 : 1)
            .ThenByDescending(d => d.CreatedAt)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);
        return new PagedResponse<DiferenciaCorteResponse>(items.Select(DiferenciaCorteResponse.De).ToList(), offset, limit, total);
    }
}

// --------------------------------------------------- Autorizar

public sealed record AutorizarDiferenciaCorteCommand(Guid DiferenciaId, int VersionEsperada)
    : IRequest<DiferenciaCorteResponse>;

public sealed class AutorizarDiferenciaCorteHandler(
    TesoreriaDbContext db, ICurrentUserContext currentUser, IIntegrationEventPublisher publisher, IClock clock)
    : IRequestHandler<AutorizarDiferenciaCorteCommand, DiferenciaCorteResponse>
{
    public async Task<DiferenciaCorteResponse> Handle(AutorizarDiferenciaCorteCommand command, CancellationToken cancellationToken)
    {
        var usuarioId = currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que autoriza.");
        var dif = await DiferenciasCorteLectura.CargarAsync(db, command.DiferenciaId, command.VersionEsperada, cancellationToken);
        var ahora = clock.UtcNow;

        dif.Autorizar(usuarioId, ahora);
        await publisher.PublishAsync(DiferenciaCorteEventos.Autorizada(dif, usuarioId, ahora), cancellationToken);
        // Devoluciones que llegaron antes de la autorización: el abono sale ahora.
        if (dif.ImporteDevuelto > 0)
            await publisher.PublishAsync(DiferenciaCorteEventos.Devolucion(dif, dif.ImporteDevuelto, null, ahora), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return DiferenciaCorteResponse.De(dif);
    }
}

// --------------------------------------------------- Rechazar

public sealed record RechazarDiferenciaCorteCommand(Guid DiferenciaId, string Motivo, int VersionEsperada)
    : IRequest<DiferenciaCorteResponse>;

public sealed class RechazarDiferenciaCorteValidator : AbstractValidator<RechazarDiferenciaCorteCommand>
{
    public RechazarDiferenciaCorteValidator()
    {
        RuleFor(c => c.DiferenciaId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RechazarDiferenciaCorteHandler(TesoreriaDbContext db, ICurrentUserContext currentUser, IClock clock)
    : IRequestHandler<RechazarDiferenciaCorteCommand, DiferenciaCorteResponse>
{
    public async Task<DiferenciaCorteResponse> Handle(RechazarDiferenciaCorteCommand command, CancellationToken cancellationToken)
    {
        var usuarioId = currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que rechaza.");
        var dif = await DiferenciasCorteLectura.CargarAsync(db, command.DiferenciaId, command.VersionEsperada, cancellationToken);

        dif.Rechazar(command.Motivo, usuarioId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return DiferenciaCorteResponse.De(dif);
    }
}

internal static class DiferenciasCorteLectura
{
    public static async Task<DiferenciaCorte> CargarAsync(TesoreriaDbContext db, Guid id, int versionEsperada, CancellationToken cancellationToken)
    {
        var dif = await db.DiferenciasCorte.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("DIF_NO_ENCONTRADA", $"No se encontró la diferencia '{id}'.");
        if (dif.Version != versionEsperada) throw new ConcurrencyException(nameof(DiferenciaCorte), dif.Id);
        return dif;
    }
}
