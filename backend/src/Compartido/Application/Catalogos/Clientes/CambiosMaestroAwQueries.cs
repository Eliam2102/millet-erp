using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.DatosMaestros.Application.Clientes;

public sealed record CambioMaestroAwDto(
    Guid Id, string TipoEntidad, Guid? EntidadId, string ReferenciaExterna, string Campo, string? ValorAntes,
    string? ValorDespues, string Dueno, string Tipo, string Estado, string Causa, string VersionEvento,
    string VersionMatriz, Guid? EjecucionId, DateTime OcurridoEnUtc, string? Resolucion, DateTimeOffset? ResueltoEnUtc, int Version);

/// <summary><c>SoloConflictos</c>: limita a RfcCompartido y EditadoEnErp (bandeja de conflictos).</summary>
public sealed record ListarCambiosMaestroAwQuery(
    string TipoEntidad, Guid? EntidadId = null, string? Estado = null, Guid? EjecucionId = null, int Limit = 50,
    bool SoloConflictos = false) : IRequest<IReadOnlyList<CambioMaestroAwDto>>;

public sealed class ListarCambiosMaestroAwValidator : AbstractValidator<ListarCambiosMaestroAwQuery>
{
    public ListarCambiosMaestroAwValidator()
    {
        RuleFor(q => q.TipoEntidad).Must(t => t is "Cliente" or "ProductoAw");
        RuleFor(q => q.Estado).Must(e => e is null or "Informativo" or "Pendiente" or "Resuelto" or "Todos");
    }
}

public sealed class ListarCambiosMaestroAwHandler(CompartidoDbContext db)
    : IRequestHandler<ListarCambiosMaestroAwQuery, IReadOnlyList<CambioMaestroAwDto>>
{
    public async Task<IReadOnlyList<CambioMaestroAwDto>> Handle(ListarCambiosMaestroAwQuery request, CancellationToken cancellationToken)
    {
        var query = db.CambiosMaestrosAw.AsNoTracking().Where(c => c.TipoEntidad == request.TipoEntidad);
        if (request.EntidadId is { } entidad) query = query.Where(c => c.EntidadId == entidad);
        if (request.EjecucionId is { } ejecucion) query = query.Where(c => c.EjecucionId == ejecucion);
        if (request.Estado is not (null or "Todos") && Enum.Parse<EstadoCambioMaestroAw>(request.Estado) is var estado)
            query = query.Where(c => c.Estado == estado);
        if (request.SoloConflictos)
            query = query.Where(c => c.Tipo == TipoCambioMaestroAw.RfcCompartido || c.Tipo == TipoCambioMaestroAw.EditadoEnErp);
        var filas = await query.OrderByDescending(c => c.OcurridoEnUtc).Take(Math.Clamp(request.Limit, 1, 200)).ToListAsync(cancellationToken);
        return filas.Select(c => new CambioMaestroAwDto(c.Id, c.TipoEntidad, c.EntidadId, c.ReferenciaExterna, c.Campo,
            c.ValorAntes, c.ValorDespues, c.Dueno, c.Tipo.ToString(), c.Estado.ToString(), c.Causa, c.VersionEvento,
            c.VersionMatriz, c.EjecucionId, c.OcurridoEnUtc, c.Resolucion, c.ResueltoEnUtc, c.Version)).ToList();
    }
}

public sealed record ResolverConflictoMaestroAwCommand(Guid Id, string TipoEntidad, string Resolucion, int Version) : IRequest;

public sealed class ResolverConflictoMaestroAwValidator : AbstractValidator<ResolverConflictoMaestroAwCommand>
{
    public ResolverConflictoMaestroAwValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.TipoEntidad).Must(t => t is "Cliente" or "ProductoAw");
        RuleFor(c => c.Resolucion).Must(r => r is "MantenerSeparados" or "AplicarAw");
        RuleFor(c => c.Version).GreaterThan(0);
    }
}

public sealed class ResolverConflictoMaestroAwHandler(CompartidoDbContext db, ICurrentUserContext user)
    : IRequestHandler<ResolverConflictoMaestroAwCommand>
{
    public async Task Handle(ResolverConflictoMaestroAwCommand request, CancellationToken cancellationToken)
    {
        var usuario = user.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "Inicia sesión para resolver el conflicto.");
        var c = await db.CambiosMaestrosAw.SingleOrDefaultAsync(x => x.Id == request.Id && x.TipoEntidad == request.TipoEntidad, cancellationToken)
            ?? throw new EntityNotFoundException("CONFLICTO_AW_NO_ENCONTRADO", "No existe ese conflicto.");
        if (c.Version != request.Version)
            throw new ConflictException("CONFLICTO_AW_CAMBIO", "El conflicto cambió. Recarga la bandeja.");
        // Combinaciones permitidas en esta rebanada: RFC repetido + «Mantener separados».
        if (c.Tipo != TipoCambioMaestroAw.RfcCompartido || request.Resolucion != "MantenerSeparados")
            throw new BusinessRuleException("CONFLICTO_AW_RESOLUCION_INVALIDA", "Esa resolución no aplica a este conflicto.");
        var ahora = DateTimeOffset.UtcNow;
        c.Resolver(request.Resolucion, usuario, ahora);
        var hermanos = await db.CambiosMaestrosAw.Where(x => x.Id != c.Id && x.TipoEntidad == c.TipoEntidad
            && x.ReferenciaExterna == c.ReferenciaExterna && x.Campo == c.Campo && x.Tipo == c.Tipo
            && x.Estado == EstadoCambioMaestroAw.Pendiente).ToListAsync(cancellationToken);
        foreach (var h in hermanos) h.Resolver(request.Resolucion, usuario, ahora);
        await db.SaveChangesAsync(cancellationToken);
    }
}
