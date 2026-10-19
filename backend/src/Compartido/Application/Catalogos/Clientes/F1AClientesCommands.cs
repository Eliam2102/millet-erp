using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
// EF traduce ToUpper a SQL; StringComparison y ToUpperInvariant no se traducen.
#pragma warning disable CA1304, CA1311, CA1862
namespace Millet.DatosMaestros.Application.Clientes;

public sealed record ClasificacionClienteDto(Guid Id, string Nombre, string Descripcion, bool Activa, int Version);
public sealed record ListarClasificacionesClienteQuery : IRequest<IReadOnlyList<ClasificacionClienteDto>>;
public sealed class ListarClasificacionesClienteHandler(CompartidoDbContext db)
    : IRequestHandler<ListarClasificacionesClienteQuery, IReadOnlyList<ClasificacionClienteDto>>
{
    public async Task<IReadOnlyList<ClasificacionClienteDto>> Handle(ListarClasificacionesClienteQuery request, CancellationToken cancellationToken) =>
        await db.Set<ClasificacionCliente>().AsNoTracking().OrderBy(c => c.Nombre)
            .Select(c => new ClasificacionClienteDto(c.Id,c.Nombre,c.Descripcion,c.Activa,c.Version)).ToListAsync(cancellationToken);
}
public sealed record GuardarClasificacionClienteCommand(Guid? Id, string Nombre, string Descripcion, bool Activa, int? Version) : IRequest<Guid>;
public sealed class GuardarClasificacionClienteValidator : AbstractValidator<GuardarClasificacionClienteCommand>
{
    public GuardarClasificacionClienteValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Descripcion).NotNull().MaximumLength(500);
    }
}
public sealed class GuardarClasificacionClienteHandler(CompartidoDbContext db) : IRequestHandler<GuardarClasificacionClienteCommand, Guid>
{
    public async Task<Guid> Handle(GuardarClasificacionClienteCommand request, CancellationToken cancellationToken)
    {
        var nombre = request.Nombre.Trim();
        if (await db.Set<ClasificacionCliente>().AnyAsync(c => c.Id != request.Id && c.Nombre.ToUpper() == nombre.ToUpper(), cancellationToken))
            throw new BusinessRuleException("CLASIFICACION_DUPLICADA", "Ya existe una clasificación con ese nombre.");
        var c = request.Id is { } id ? await db.Set<ClasificacionCliente>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("CLASIFICACION_NO_ENCONTRADA", "No existe esa clasificación.")
            : new ClasificacionCliente(Guid.CreateVersion7(), nombre, request.Descripcion);
        if (request.Id is not null && request.Version != c.Version)
            throw new ConflictException("CLASIFICACION_CAMBIO", "La clasificación cambió. Recarga antes de guardar.");
        c.Editar(nombre, request.Descripcion, request.Activa);
        if (request.Id is null) db.Add(c);
        // La comprobación de arriba no es atómica: dos altas simultáneas pueden pasarla.
        // El índice único ix_clasificaciones_cliente_nombre es la autoridad y su colisión es 422, no 500.
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_clasificaciones_cliente_nombre" })
        {
            throw new BusinessRuleException("CLASIFICACION_DUPLICADA", "Ya existe una clasificación con ese nombre.");
        }
        return c.Id;
    }
}

public sealed record ClienteLigadoDto(Guid Id, string ReferenciaExterna, string Rfc, string RazonSocialAw, int Version, IReadOnlyList<ClienteExistente> Candidatos);
public sealed record ListarLigadosClienteQuery : IRequest<IReadOnlyList<ClienteLigadoDto>>;
public sealed class ListarLigadosClienteHandler(CompartidoDbContext db) : IRequestHandler<ListarLigadosClienteQuery,IReadOnlyList<ClienteLigadoDto>>
{
    public async Task<IReadOnlyList<ClienteLigadoDto>> Handle(ListarLigadosClienteQuery request, CancellationToken cancellationToken)
    {
        var conflictos = await db.Set<ClienteLigadoAw>().AsNoTracking().Where(c => c.RevisadoEn == null).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var rfcs = conflictos.Select(c => c.Rfc).ToArray();
        var candidatos = await db.Clientes.AsNoTracking().Where(c => c.Origen == OrigenMaster.Manual && c.Rfc != null && rfcs.Contains(c.Rfc.Trim().ToUpper())).ToListAsync(cancellationToken);
        return conflictos.Select(c => new ClienteLigadoDto(c.Id,c.ReferenciaExterna,c.Rfc,
            JsonSerializer.Deserialize<AplicarClienteAwSnapshot>(c.SnapshotJson)!.RazonSocial,c.Version,
            candidatos.Where(m => RfcCliente.Normalizar(m.Rfc) == c.Rfc).Select(m => new ClienteExistente(m.Id,m.Clave,m.RazonSocial)).ToArray()))
            // Sin candidatos vigentes (p. ej. el manual cambió su RFC) no hay nada que revisar.
            .Where(d => d.Candidatos.Count > 0).ToArray();
    }
}
public sealed record RevisarLigadoClienteCommand(Guid Id, Guid ClienteId, int Version) : IRequest;
public sealed class RevisarLigadoClienteValidator : AbstractValidator<RevisarLigadoClienteCommand>
{
    public RevisarLigadoClienteValidator() { RuleFor(c => c.Id).NotEmpty(); RuleFor(c => c.ClienteId).NotEmpty(); RuleFor(c => c.Version).GreaterThan(0); }
}
public sealed class RevisarLigadoClienteHandler(CompartidoDbContext db, ICurrentUserContext user, AplicarClienteAwService aplicar)
    : IRequestHandler<RevisarLigadoClienteCommand>
{
    public async Task Handle(RevisarLigadoClienteCommand request, CancellationToken cancellationToken)
    {
        var usuario = user.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "Inicia sesión para revisar el ligado.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var c = await db.Set<ClienteLigadoAw>().SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("LIGADO_NO_ENCONTRADO", "No existe ese conflicto de ligado.");
        await using var rfcScope = await ClienteRfcScope.AbrirAsync(db, c.Rfc, cancellationToken);
        if (c.Version != request.Version || c.RevisadoEn is not null)
            throw new ConflictException("LIGADO_CAMBIO", "El conflicto cambió o ya fue revisado. Recarga la bandeja.");
        var cliente = await db.Clientes.SingleOrDefaultAsync(m => m.Id == request.ClienteId && m.Origen == OrigenMaster.Manual, cancellationToken)
            ?? throw new BusinessRuleException("CLIENTE_NO_LIGABLE", "Elige un cliente manual de la lista de coincidencias.");
        if (await db.Clientes.AnyAsync(m => m.Id != cliente.Id && m.ReferenciaExterna == c.ReferenciaExterna, cancellationToken))
            throw new BusinessRuleException("CLIENTE_REFERENCIA_DUPLICADA", "La clave de A+W ya está ligada a otro cliente.");
        cliente.LigarAw(c.ReferenciaExterna, c.Rfc); c.Revisar(cliente.Id, usuario, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        var snap = JsonSerializer.Deserialize<AplicarClienteAwSnapshot>(c.SnapshotJson)!;
        await aplicar.AplicarAsync(snap with { SoloCrear = false }, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
