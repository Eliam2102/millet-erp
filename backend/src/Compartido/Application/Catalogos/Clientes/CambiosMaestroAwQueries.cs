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
        RuleFor(c => c.Resolucion).Must(r => r is "MantenerSeparados" or "AplicarAw" or "MantenerErp");
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
        // Combinaciones permitidas: RFC repetido + «Mantener separados»; descripción/unidad de producto editada en el ERP + «Aplicar A+W» o «Mantener ERP».
        var rfc = c.TipoEntidad == "Cliente" && c.Tipo == TipoCambioMaestroAw.RfcCompartido && request.Resolucion == "MantenerSeparados";
        var producto = c.TipoEntidad == "ProductoAw" && c.Tipo == TipoCambioMaestroAw.EditadoEnErp && request.Resolucion is "AplicarAw" or "MantenerErp";
        if (!rfc && !producto)
            throw new BusinessRuleException("CONFLICTO_AW_RESOLUCION_INVALIDA", "Esa resolución no aplica a este conflicto.");
        var ahora = DateTimeOffset.UtcNow;
        if (producto && request.Resolucion == "AplicarAw")
        {
            var origen = await db.ProductosSincronizacionAw.AsNoTracking().Where(r => r.ReferenciaExterna == c.ReferenciaExterna)
                .Select(r => new { r.DescripcionOrigen, r.UnidadOrigenCruda }).SingleOrDefaultAsync(cancellationToken);
            var p = await db.ProductosAw.SingleOrDefaultAsync(x => x.ReferenciaExterna == c.ReferenciaExterna, cancellationToken)
                ?? throw new EntityNotFoundException("PRODUCTO_AW_NO_ENCONTRADO", "El producto del conflicto ya no existe.");
            string? antes;
            string despues;
            if (c.Campo == "Unidad")
            {
                var codigo = origen?.UnidadOrigenCruda?.Trim().ToUpperInvariant();
                var unidad = string.IsNullOrEmpty(codigo) ? null
                    : await db.UnidadesMedida.AsNoTracking().FirstOrDefaultAsync(u => u.Codigo == codigo && u.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Activo, cancellationToken);
                if (unidad is null)
                    throw new BusinessRuleException("CONFLICTO_AW_SIN_VALOR_ORIGEN", "No hay una unidad de A+W con equivalencia para aplicar; vuelve a leer el producto.");
                antes = p.UnidadMedida; despues = unidad.Codigo;
                p.AsignarUnidadMedida(unidad.Id, unidad.Codigo);
            }
            else
            {
                if (origen?.DescripcionOrigen is null)
                    throw new BusinessRuleException("CONFLICTO_AW_SIN_VALOR_ORIGEN", "No hay valor de A+W para aplicar; vuelve a leer el producto.");
                antes = p.Descripcion; despues = origen.DescripcionOrigen;
                p.ActualizarDatos(descripcion: despues);
            }
            // Deja constancia del valor aplicado; la huella previa (id del conflicto) hace única esta fila por resolución.
            db.CambiosMaestrosAw.Add(new CambioMaestroAw(Guid.CreateVersion7(), "ProductoAw", c.EntidadId, c.ReferenciaExterna, c.Campo,
                antes, despues, MatrizCampoDuenoAw.DuenoAw, TipoCambioMaestroAw.Aplicado, EstadoCambioMaestroAw.Informativo,
                "Se aplicó el valor de A+W al resolver el conflicto.", c.VersionEvento, MatrizCampoDuenoAw.Version, null, ahora.UtcDateTime, c.Id.ToString()));
        }
        c.Resolver(request.Resolucion, usuario, ahora);
        var hermanos = await db.CambiosMaestrosAw.Where(x => x.Id != c.Id && x.TipoEntidad == c.TipoEntidad
            && x.ReferenciaExterna == c.ReferenciaExterna && x.Campo == c.Campo && x.Tipo == c.Tipo
            && x.Estado == EstadoCambioMaestroAw.Pendiente).ToListAsync(cancellationToken);
        foreach (var h in hermanos) h.Resolver(request.Resolucion, usuario, ahora);
        await db.SaveChangesAsync(cancellationToken);
    }
}
