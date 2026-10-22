using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Incidencias;

public sealed record CompromisoIncidenciaOcResponse(Guid Id, int Numero, DateOnly FechaCompromiso, string? Motivo,
    Guid RegistradoPorId, DateTimeOffset RegistradoEn);

public sealed record IncidenciaProveedorOcResponse(Guid Id, int Numero, Guid LineaOrdenCompraId,
    TipoIncidenciaProveedorOc Tipo, decimal Cantidad, string? Descripcion, Guid ResponsableId, string? ResponsableNombre,
    DateOnly FechaCompromisoVigente, bool Abierta, bool Vencida, DateTimeOffset? AvisoVencidaEnviadoEn,
    Guid RegistradaPorId, DateTimeOffset RegistradaEn, DateTimeOffset? CerradaEn, Guid? CerradaPorId,
    bool CerradaPorRecepcion, string? MotivoCierre, IReadOnlyList<CompromisoIncidenciaOcResponse> Compromisos);

public static class IncidenciaProveedorMapper
{
    /// <summary>
    /// Mapea las incidencias de una OC resolviendo el nombre del responsable y el aviso de vencimiento
    /// del compromiso vigente (un solo viaje por cada dato).
    /// </summary>
    public static async Task<IReadOnlyList<IncidenciaProveedorOcResponse>> ToResponsesAsync(
        IReadOnlyCollection<IncidenciaProveedorOc> incidencias, ComprasDbContext db, IUsuarioReadPort usuarios,
        DateOnly hoy, CancellationToken cancellationToken)
    {
        if (incidencias.Count == 0) return [];
        var ids = incidencias.Select(i => i.Id).ToList();
        var avisos = await db.AvisosIncidenciaProveedorOc.AsNoTracking()
            .Where(a => ids.Contains(a.IncidenciaProveedorOcId))
            .Select(a => new { a.IncidenciaProveedorOcId, a.FechaCompromiso, a.EnviadoEn })
            .ToListAsync(cancellationToken);
        var nombres = await usuarios.ObtenerNombresAsync(
            incidencias.Select(i => i.ResponsableId).Distinct().ToList(), cancellationToken);
        return incidencias.OrderByDescending(i => i.Numero).Select(i =>
        {
            var vigente = i.FechaCompromisoVigente;
            var aviso = avisos.FirstOrDefault(a => a.IncidenciaProveedorOcId == i.Id && a.FechaCompromiso == vigente);
            return new IncidenciaProveedorOcResponse(i.Id, i.Numero, i.LineaOrdenCompraId, i.Tipo, i.Cantidad,
                i.Descripcion, i.ResponsableId, nombres.GetValueOrDefault(i.ResponsableId), vigente, i.Abierta,
                i.Abierta && vigente < hoy, aviso?.EnviadoEn, i.RegistradaPorId, i.RegistradaEn, i.CerradaEn,
                i.CerradaPorId, !i.Abierta && i.CerradaPorId is null, i.MotivoCierre,
                i.Compromisos.OrderBy(c => c.Numero).Select(c => new CompromisoIncidenciaOcResponse(
                    c.Id, c.Numero, c.FechaCompromiso, c.Motivo, c.RegistradoPorId, c.RegistradoEn)).ToList());
        }).ToList();
    }
}

public sealed record RegistrarIncidenciaProveedorCommand(Guid OrdenCompraId, Guid LineaOrdenCompraId,
    TipoIncidenciaProveedorOc Tipo, decimal Cantidad, DateOnly FechaCompromiso, Guid ResponsableId,
    string? Descripcion) : IRequest<IncidenciaProveedorOcResponse>;

public sealed record ReprogramarIncidenciaProveedorCommand(Guid OrdenCompraId, Guid IncidenciaId,
    DateOnly FechaCompromiso, string Motivo) : IRequest<IncidenciaProveedorOcResponse>;

public sealed record CerrarIncidenciaProveedorCommand(Guid OrdenCompraId, Guid IncidenciaId, string Motivo)
    : IRequest<IncidenciaProveedorOcResponse>;

public sealed class RegistrarIncidenciaProveedorValidator : AbstractValidator<RegistrarIncidenciaProveedorCommand>
{
    public RegistrarIncidenciaProveedorValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.LineaOrdenCompraId).NotEmpty().WithMessage("Indica la línea de la orden de compra.");
        RuleFor(c => c.Tipo).IsInEnum();
        RuleFor(c => c.Cantidad).GreaterThan(0).WithMessage("La cantidad afectada debe ser mayor a cero.");
        RuleFor(c => c.FechaCompromiso).NotEmpty().WithMessage("Indica la fecha compromiso.");
        RuleFor(c => c.ResponsableId).NotEmpty().WithMessage("Indica quién da seguimiento a la incidencia.");
        RuleFor(c => c.Descripcion).MaximumLength(500);
    }
}

public sealed class ReprogramarIncidenciaProveedorValidator : AbstractValidator<ReprogramarIncidenciaProveedorCommand>
{
    public ReprogramarIncidenciaProveedorValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.IncidenciaId).NotEmpty();
        RuleFor(c => c.FechaCompromiso).NotEmpty().WithMessage("Indica la nueva fecha compromiso.");
        RuleFor(c => c.Motivo).NotEmpty().WithMessage("Indica el motivo del cambio de fecha.").MaximumLength(300);
    }
}

public sealed class CerrarIncidenciaProveedorValidator : AbstractValidator<CerrarIncidenciaProveedorCommand>
{
    public CerrarIncidenciaProveedorValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.IncidenciaId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().WithMessage("Indica el motivo del cierre.").MaximumLength(300);
    }
}

public sealed class IncidenciasProveedorHandler(ComprasDbContext db, ICurrentUserContext currentUser, IClock clock,
    IUsuarioReadPort usuarios) :
    IRequestHandler<RegistrarIncidenciaProveedorCommand, IncidenciaProveedorOcResponse>,
    IRequestHandler<ReprogramarIncidenciaProveedorCommand, IncidenciaProveedorOcResponse>,
    IRequestHandler<CerrarIncidenciaProveedorCommand, IncidenciaProveedorOcResponse>
{
    public Task<IncidenciaProveedorOcResponse> Handle(RegistrarIncidenciaProveedorCommand request,
        CancellationToken cancellationToken) =>
        Ejecutar(request.OrdenCompraId, (oc, hoy, userId, ahora) => oc.RegistrarIncidenciaProveedor(
            request.LineaOrdenCompraId, request.Tipo, request.Cantidad, request.FechaCompromiso, request.ResponsableId,
            request.Descripcion, hoy, userId, ahora), cancellationToken);

    public Task<IncidenciaProveedorOcResponse> Handle(ReprogramarIncidenciaProveedorCommand request,
        CancellationToken cancellationToken) =>
        Ejecutar(request.OrdenCompraId, (oc, hoy, userId, ahora) => oc.ReprogramarIncidenciaProveedor(
            request.IncidenciaId, request.FechaCompromiso, request.Motivo, hoy, userId, ahora), cancellationToken);

    public Task<IncidenciaProveedorOcResponse> Handle(CerrarIncidenciaProveedorCommand request,
        CancellationToken cancellationToken) =>
        Ejecutar(request.OrdenCompraId, (oc, _, userId, ahora) =>
            oc.CerrarIncidenciaProveedor(request.IncidenciaId, request.Motivo, userId, ahora), cancellationToken);

    private async Task<IncidenciaProveedorOcResponse> Ejecutar(Guid ocId,
        Func<OrdenCompra, DateOnly, Guid, DateTimeOffset, IncidenciaProveedorOc> accion, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        // Las incidencias se incluyen: el dominio numera con su conteo.
        var oc = await db.OrdenesCompra.Include(o => o.Lineas)
            .Include(o => o.Incidencias).ThenInclude(i => i.Compromisos)
            .FirstOrDefaultAsync(o => o.Id == ocId, ct)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        var ahora = clock.UtcNow;
        var hoy = FechaContable.HoyLocal(ahora);
        var incidencia = accion(oc, hoy, userId, ahora);
        await db.SaveChangesAsync(ct);
        return (await IncidenciaProveedorMapper.ToResponsesAsync([incidencia], db, usuarios, hoy, ct))[0];
    }
}
