using FluentValidation;
using MediatR;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Compartido.Application.Incidencias;

/// <summary>Alta manual (p. ej. desde el aviso de un rechazo que revirtió la operación). Requiere «registrar».</summary>
public sealed record AbrirIncidenciaCommand(
    string Modulo,
    string TipoDocumento,
    Guid DocumentoId,
    string DocumentoEtiqueta,
    Guid? SucursalId,
    string Paso,
    string? CodigoMotivo,
    string Motivo,
    string? Detalle,
    string? DetalleClave,
    Guid ResponsableUsuarioId,
    DateOnly? FechaCompromiso,
    string? PasoReanudacion,
    string? RutaReanudacion) : IRequest<IncidenciaAbiertaDetalle>;

public sealed class AbrirIncidenciaValidator : AbstractValidator<AbrirIncidenciaCommand>
{
    private const string PatronClave = "^[a-z0-9][a-z0-9._-]*$";

    public AbrirIncidenciaValidator()
    {
        RuleFor(x => x.Modulo).NotEmpty().MaximumLength(40).Matches(PatronClave);
        RuleFor(x => x.TipoDocumento).NotEmpty().MaximumLength(60).Matches(PatronClave);
        RuleFor(x => x.Paso).NotEmpty().MaximumLength(100).Matches(PatronClave);
        RuleFor(x => x.PasoReanudacion).MaximumLength(100).Matches(PatronClave).When(x => !string.IsNullOrWhiteSpace(x.PasoReanudacion));
        RuleFor(x => x.DocumentoId).NotEmpty();
        RuleFor(x => x.DocumentoEtiqueta).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Motivo).NotEmpty().Must(m => m.Trim().Length is >= 5 and <= 1000)
            .WithMessage("El motivo debe tener de 5 a 1000 caracteres.");
        RuleFor(x => x.Detalle).MaximumLength(2000);
        RuleFor(x => x.DetalleClave).MaximumLength(100);
        RuleFor(x => x.CodigoMotivo).MaximumLength(100);
        RuleFor(x => x.ResponsableUsuarioId).NotEmpty();
        RuleFor(x => x.RutaReanudacion).MaximumLength(300).Must(r => r!.StartsWith('/'))
            .When(x => !string.IsNullOrWhiteSpace(x.RutaReanudacion)).WithMessage("La ruta para retomar debe empezar con «/».");
    }
}

public sealed class AbrirIncidenciaHandler(IncidenciasServicio servicio, Millet.SharedKernel.Application.ICurrentUserPermissions permisos)
    : IRequestHandler<AbrirIncidenciaCommand, IncidenciaAbiertaDetalle>
{
    public async Task<IncidenciaAbiertaDetalle> Handle(AbrirIncidenciaCommand r, CancellationToken cancellationToken)
    {
        var actor = servicio.UsuarioRequerido;
        if (!await permisos.TieneAsync(IncidenciasPermisos.Registrar, cancellationToken))
            throw new ForbiddenException("INCIDENCIA_SIN_PERMISO", "No tienes permiso para registrar incidencias.");
        await servicio.VerificarSucursalAsync(r.SucursalId, IncidenciasPermisos.GestionarTodasSucursales, cancellationToken);
        var (i, ya) = await servicio.AbrirAsync(new AbrirIncidenciaSolicitud(
            r.Modulo, r.TipoDocumento, r.DocumentoId, r.DocumentoEtiqueta, r.Paso, r.Motivo, r.ResponsableUsuarioId, r.SucursalId,
            r.CodigoMotivo, r.Detalle, r.DetalleClave, r.FechaCompromiso, r.PasoReanudacion, r.RutaReanudacion), actor, cancellationToken);
        return new IncidenciaAbiertaDetalle(await servicio.DetalleAsync(i, cancellationToken), ya);
    }
}

public sealed record AtenderIncidenciaCommand(Guid Id, int Version, string? Nota) : IRequest<IncidenciaDetalle>;

public sealed class AtenderIncidenciaHandler(IncidenciasServicio servicio, CompartidoDbContext db) : IRequestHandler<AtenderIncidenciaCommand, IncidenciaDetalle>
{
    public async Task<IncidenciaDetalle> Handle(AtenderIncidenciaCommand r, CancellationToken cancellationToken)
    {
        var actor = servicio.UsuarioRequerido;
        var i = await servicio.CargarAsync(r.Id, true, cancellationToken);
        IncidenciasServicio.ExigirVersion(i, r.Version);
        var a = await servicio.AccionesAsync(i, cancellationToken);
        if (i.Pendiente) IncidenciasServicio.Exigir(a.PuedeAtender || a.PuedeResolver, "atender");
        i.Atender(actor, await servicio.NombreUsuarioAsync(actor, cancellationToken), r.Nota, servicio.Ahora);
        await db.SaveChangesAsync(cancellationToken);
        return await servicio.DetalleAsync(i, cancellationToken);
    }
}

public sealed record ResolverIncidenciaCommand(Guid Id, int Version, string Resolucion) : IRequest<IncidenciaDetalle>;

public sealed class ResolverIncidenciaHandler(IncidenciasServicio servicio, CompartidoDbContext db) : IRequestHandler<ResolverIncidenciaCommand, IncidenciaDetalle>
{
    public async Task<IncidenciaDetalle> Handle(ResolverIncidenciaCommand r, CancellationToken cancellationToken)
    {
        var actor = servicio.UsuarioRequerido;
        var i = await servicio.CargarAsync(r.Id, true, cancellationToken);
        IncidenciasServicio.ExigirVersion(i, r.Version);
        if (i.Pendiente) IncidenciasServicio.Exigir((await servicio.AccionesAsync(i, cancellationToken)).PuedeResolver, "resolver");
        i.Resolver(actor, await servicio.NombreUsuarioAsync(actor, cancellationToken), r.Resolucion, servicio.Ahora);
        await db.SaveChangesAsync(cancellationToken);
        return await servicio.DetalleAsync(i, cancellationToken);
    }
}

public sealed record CancelarIncidenciaCommand(Guid Id, int Version, string Motivo) : IRequest<IncidenciaDetalle>;

public sealed class CancelarIncidenciaHandler(IncidenciasServicio servicio, CompartidoDbContext db) : IRequestHandler<CancelarIncidenciaCommand, IncidenciaDetalle>
{
    public async Task<IncidenciaDetalle> Handle(CancelarIncidenciaCommand r, CancellationToken cancellationToken)
    {
        var actor = servicio.UsuarioRequerido;
        var i = await servicio.CargarAsync(r.Id, true, cancellationToken);
        IncidenciasServicio.ExigirVersion(i, r.Version);
        if (i.Pendiente) IncidenciasServicio.Exigir((await servicio.AccionesAsync(i, cancellationToken)).PuedeCancelar, "cancelar");
        i.Cancelar(actor, await servicio.NombreUsuarioAsync(actor, cancellationToken), r.Motivo, servicio.Ahora);
        await db.SaveChangesAsync(cancellationToken);
        return await servicio.DetalleAsync(i, cancellationToken);
    }
}

public sealed record ReasignarIncidenciaCommand(Guid Id, int Version, Guid ResponsableUsuarioId, string? Nota) : IRequest<IncidenciaDetalle>;

public sealed class ReasignarIncidenciaHandler(IncidenciasServicio servicio, CompartidoDbContext db) : IRequestHandler<ReasignarIncidenciaCommand, IncidenciaDetalle>
{
    public async Task<IncidenciaDetalle> Handle(ReasignarIncidenciaCommand r, CancellationToken cancellationToken)
    {
        var actor = servicio.UsuarioRequerido;
        var i = await servicio.CargarAsync(r.Id, true, cancellationToken);
        IncidenciasServicio.ExigirVersion(i, r.Version);
        if (i.Pendiente) IncidenciasServicio.Exigir((await servicio.AccionesAsync(i, cancellationToken)).PuedeReasignar, "reasignar");
        var nombre = await servicio.ResponsableValidoAsync(r.ResponsableUsuarioId, cancellationToken);
        i.Reasignar(r.ResponsableUsuarioId, nombre, actor, await servicio.NombreUsuarioAsync(actor, cancellationToken), r.Nota, servicio.Ahora);
        await db.SaveChangesAsync(cancellationToken);
        return await servicio.DetalleAsync(i, cancellationToken);
    }
}
