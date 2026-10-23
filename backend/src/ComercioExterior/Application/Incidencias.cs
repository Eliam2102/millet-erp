using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;
public sealed record CambiarIncidenciaDatos(EstadoIncidencia Estado, string? AccionTomada = null, string? EfectoPosterior = null,
    Guid[]? EvidenciasIds = null, string? Motivo = null, string? NuevoPedimento = null, Guid? AdjuntoPedimentoId = null);
public sealed record CrearIncidenciaCommand(Guid ExpedienteId, int Version, DatosIncidencia Datos) : IRequest<ExpedienteDetalle>;
public sealed record CambiarIncidenciaCommand(Guid ExpedienteId, Guid IncidenciaId, int Version, CambiarIncidenciaDatos Datos) : IRequest<ExpedienteDetalle>;
public sealed record HistorialIncidenciaDto(EstadoIncidencia Estado, Guid ActorId, DateTimeOffset Fecha, string? Motivo);
public sealed record IncidenciaDto(Guid Id, TipoIncidencia Tipo, EstadoIncidencia Estado, DateOnly Fecha, Guid ResponsableUsuarioId,
    string ResponsableNombre, string Motivo, string? Descripcion, Guid? ParticipanteId, Guid[] DocumentosIds, Guid[] EvidenciasIds,
    string? AccionCorrectiva, string? EfectoPosterior, string? HitoClave, Guid? ContenedorId, Guid? PartidaId,
    string? PedimentoAnterior, string? PedimentoNuevo, string? MotivoCancelacion, int AntiguedadDias, IReadOnlyList<HistorialIncidenciaDto> Historial);
public sealed record AvisoIncidenciaDto(Guid Id, Guid IncidenciaId, Guid DestinatarioUsuarioId, DateTimeOffset Fecha);
public static class IncidenciaMap
{
    public static IncidenciaDto Dto(IncidenciaExpediente i, IReadOnlyDictionary<Guid, string> nombres, DateOnly hoy) => new(i.Id, i.Tipo, i.Estado, i.Fecha, i.ResponsableUsuarioId,
        nombres.GetValueOrDefault(i.ResponsableUsuarioId, "Sin responsable"), i.Motivo, i.Descripcion, i.ParticipanteId, i.DocumentosIds, i.EvidenciasIds,
        i.AccionCorrectiva, i.EfectoPosterior, i.HitoClave, i.ContenedorId, i.PartidaId, i.PedimentoAnterior, i.PedimentoNuevo, i.MotivoCancelacion,
        Antiguedad(i, hoy), i.Historial.OrderBy(h => h.Fecha).ThenBy(h => h.Estado).Select(h => new HistorialIncidenciaDto(h.Estado, h.ActorId, h.Fecha, h.Motivo)).ToArray());
    public static int Antiguedad(IncidenciaExpediente i, DateOnly hoy) => Math.Max(0,
        (i.Pendiente ? hoy : FechaContable.HoyLocal(i.Historial.Max(h => h.Fecha))).DayNumber - i.Fecha.DayNumber);
}
public sealed class CrearIncidenciaHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IClock clock) : IRequestHandler<CrearIncidenciaCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(CrearIncidenciaCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorIncidenciasGestionar, cancellationToken);
        var e = await servicio.CargarAsync(request.ExpedienteId, true, cancellationToken);
        if (e.Version != request.Version) throw new ConcurrencyException("Expediente", e.Id);
        await catalogos.ValidarResponsableAsync(e.SucursalId, request.Datos.ResponsableUsuarioId, cancellationToken);
        e.AbrirIncidencia(Guid.CreateVersion7(), request.Datos, await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken), servicio.Actor, clock.UtcNow, clock.HoyLocal());
        await db.SaveChangesAsync(cancellationToken);
        return await servicio.DetalleAsync(e, cancellationToken);
    }
}
public sealed class CambiarIncidenciaHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IOcImportacionReadPort compras, IClock clock) : IRequestHandler<CambiarIncidenciaCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(CambiarIncidenciaCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorIncidenciasGestionar, cancellationToken);
        var e = await servicio.CargarAsync(request.ExpedienteId, true, cancellationToken);
        if (e.Version != request.Version) throw new ConcurrencyException("Expediente", e.Id);
        var i = e.Incidencias.SingleOrDefault(i => i.Id == request.IncidenciaId) ?? throw new EntityNotFoundException("CE_INCIDENCIA_NO_ENCONTRADA", "No existe la incidencia en este expediente.");
        RequisitoDocumento? pedimento = null; string? original = null;
        if (i.Tipo == TipoIncidencia.RectificacionPedimento && request.Datos.Estado == EstadoIncidencia.Resuelta)
        {
            pedimento = (await catalogos.RequisitosAsync(e.Tipo, cancellationToken)).Single(x => x.Codigo == (e.Tipo == TipoExpediente.Importacion ? "IMP_PEDIMENTO" : "EXP_PEDIMENTO"));
            await catalogos.ValidarAdjuntoAsync(e.Id, pedimento.Id, request.Datos.AdjuntoPedimentoId ?? Guid.Empty, cancellationToken);
            if (e.OcId is Guid oc) original = (await compras.ObtenerAsync(oc, cancellationToken))?.InformacionImportacion?.NumeroPedimento;
        }
        e.CambiarIncidencia(i.Id, request.Datos.Estado, request.Datos.AccionTomada, request.Datos.EfectoPosterior, request.Datos.EvidenciasIds ?? [], request.Datos.Motivo,
            servicio.Actor, clock.UtcNow, await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken), pedimento, request.Datos.AdjuntoPedimentoId, request.Datos.NuevoPedimento, original);
        await db.SaveChangesAsync(cancellationToken);
        return await servicio.DetalleAsync(e, cancellationToken);
    }
}
public sealed class CrearIncidenciaValidator : AbstractValidator<CrearIncidenciaCommand>
{
    public CrearIncidenciaValidator()
    {
        RuleFor(x => x.ExpedienteId).NotEmpty(); RuleFor(x => x.Version).GreaterThan(0); RuleFor(x => x.Datos).NotNull();
        When(x => x.Datos != null, () => {
            RuleFor(x => x.Datos.Tipo).IsInEnum(); RuleFor(x => x.Datos.Fecha).NotEmpty(); RuleFor(x => x.Datos.ResponsableUsuarioId).NotEmpty();
            RuleFor(x => x.Datos.Motivo).NotEmpty().MinimumLength(5).MaximumLength(500);
            RuleFor(x => x.Datos.Descripcion).MaximumLength(2000); RuleFor(x => x.Datos.AccionCorrectiva).MaximumLength(2000); RuleFor(x => x.Datos.EfectoPosterior).MaximumLength(2000);
            RuleFor(x => x.Datos.DocumentosIds).Must(x => x == null || x.Length <= 100); RuleFor(x => x.Datos.EvidenciasIds).Must(x => x == null || x.Length <= 100);
        });
    }
}
public sealed class CambiarIncidenciaValidator : AbstractValidator<CambiarIncidenciaCommand>
{
    public CambiarIncidenciaValidator()
    {
        RuleFor(x => x.ExpedienteId).NotEmpty(); RuleFor(x => x.IncidenciaId).NotEmpty(); RuleFor(x => x.Version).GreaterThan(0); RuleFor(x => x.Datos).NotNull();
        When(x => x.Datos != null, () => {
            RuleFor(x => x.Datos.Estado).IsInEnum(); RuleFor(x => x.Datos.AccionTomada).MaximumLength(2000); RuleFor(x => x.Datos.EfectoPosterior).MaximumLength(2000);
            RuleFor(x => x.Datos.Motivo).MaximumLength(500); RuleFor(x => x.Datos.NuevoPedimento).MaximumLength(100);
            RuleFor(x => x.Datos.EvidenciasIds).Must(x => x == null || x.Length <= 100);
        });
    }
}
public sealed record IncidenciasQuery(Guid? ExpedienteId = null, TipoIncidencia? Tipo = null, EstadoIncidencia? Estado = null,
    DateOnly? Desde = null, DateOnly? Hasta = null, Guid? SucursalId = null, bool SoloAbiertas = false) : IRequest<IncidenciasReporte>;
public sealed record IncidenciaReporteFila(Guid ExpedienteId, string Expediente, string Sucursal, string Tipo, string Estado, DateOnly Fecha, string Motivo, string Responsable, int AntiguedadDias);
public sealed record IncidenciasReporte(string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaRelacionIva> Columnas, IReadOnlyList<IncidenciaReporteFila> Filas);
public sealed class IncidenciasHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, ICatalogosCeReadPort catalogos, IClock clock) : IRequestHandler<IncidenciasQuery, IncidenciasReporte>
{
    public async Task<IncidenciasReporte> Handle(IncidenciasQuery request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesLeer, cancellationToken);
        var sucursales = await servicio.SucursalesAsync(request.SucursalId, cancellationToken);
        if (request.ExpedienteId is Guid id) await servicio.CargarAsync(id, false, cancellationToken);
        if (request.Desde > request.Hasta) throw new BusinessRuleException("CE_PERIODO_INVALIDO", "La fecha inicial debe ser anterior o igual a la final.");
        var expedientes = db.Expedientes.AsNoTracking();
        if (sucursales is not null) expedientes = expedientes.Where(e => sucursales.Contains(e.SucursalId));
        var q = from i in db.Set<IncidenciaExpediente>().AsNoTracking() join e in expedientes on i.ExpedienteId equals e.Id
                where (request.ExpedienteId == null || i.ExpedienteId == request.ExpedienteId) && (request.Tipo == null || i.Tipo == request.Tipo) && (request.Estado == null || i.Estado == request.Estado)
                    && (request.Desde == null || i.Fecha >= request.Desde) && (request.Hasta == null || i.Fecha <= request.Hasta)
                    && (!request.SoloAbiertas || i.Estado == EstadoIncidencia.Abierta || i.Estado == EstadoIncidencia.EnAtencion)
                select new { Incidencia = i, e.Folio, e.SucursalId };
        var datos = await q.OrderBy(x => x.Incidencia.Fecha).ThenBy(x => x.Incidencia.Id).ToListAsync(cancellationToken);
        var ids = datos.Select(x => x.Incidencia.Id).ToArray();
        var finales = await db.Set<EstadoIncidenciaHistorial>().Where(h => ids.Contains(h.IncidenciaId) && (h.Estado == EstadoIncidencia.Resuelta || h.Estado == EstadoIncidencia.Cancelada)).ToDictionaryAsync(h => h.IncidenciaId, h => h.Fecha, cancellationToken);
        var nombres = await catalogos.NombresAsync(datos.Select(x => x.Incidencia.ResponsableUsuarioId).Distinct(), cancellationToken);
        var sedes = await catalogos.SucursalesAsync(datos.Select(x => x.SucursalId).Distinct(), cancellationToken);
        var hoy = clock.HoyLocal();
        return new("Incidencias de Comercio Exterior", clock.UtcNow, new Dictionary<string, string?> { ["Desde"] = request.Desde?.ToString("yyyy-MM-dd"), ["Hasta"] = request.Hasta?.ToString("yyyy-MM-dd"), ["Sucursal"] = request.SucursalId is Guid s ? sedes.GetValueOrDefault(s, "Sucursal seleccionada") : "Sucursales autorizadas", ["Tipo"] = request.Tipo is { } t ? TipoNombre(t) : null, ["Estado"] = request.SoloAbiertas ? "Abiertas y en atención" : request.Estado is { } estado ? EstadoNombre(estado) : null, ["Expediente"] = request.ExpedienteId?.ToString() },
            [new("expediente", "Expediente"), new("sucursal", "Sucursal"), new("tipo", "Tipo"), new("estado", "Estado"), new("fecha", "Fecha", "fecha"), new("motivo", "Motivo"), new("responsable", "Responsable"), new("antiguedadDias", "Antigüedad (días)", "numero")],
            datos.Select(x => new IncidenciaReporteFila(x.Incidencia.ExpedienteId, x.Folio, sedes.GetValueOrDefault(x.SucursalId, "Sin sucursal"), TipoNombre(x.Incidencia.Tipo), EstadoNombre(x.Incidencia.Estado), x.Incidencia.Fecha, x.Incidencia.Motivo, nombres.GetValueOrDefault(x.Incidencia.ResponsableUsuarioId, "Sin responsable"), Math.Max(0, (finales.TryGetValue(x.Incidencia.Id, out var fin) ? FechaContable.HoyLocal(fin) : hoy).DayNumber - x.Incidencia.Fecha.DayNumber))).ToArray());
    }
    public static string EstadoNombre(EstadoIncidencia estado) => estado == EstadoIncidencia.EnAtencion ? "En atención" : estado.ToString();
    public static string TipoNombre(TipoIncidencia tipo) => tipo switch { TipoIncidencia.CancelacionOperacion => "Cancelación de la operación", TipoIncidencia.RectificacionPedimento => "Rectificación de pedimento", TipoIncidencia.MercanciaRetenida => "Mercancía retenida", TipoIncidencia.MercanciaRechazada => "Mercancía rechazada", TipoIncidencia.MercanciaDanada => "Mercancía dañada", TipoIncidencia.MercanciaFaltante => "Mercancía faltante", TipoIncidencia.RoturaTransporte => "Rotura en transporte", TipoIncidencia.DocumentoIncorrecto => "Documento incorrecto", _ => "Otra" };
}

public sealed class IncidenciasQueryValidator : AbstractValidator<IncidenciasQuery>
{
    public IncidenciasQueryValidator()
    {
        RuleFor(x => x.Tipo).IsInEnum().When(x => x.Tipo.HasValue);
        RuleFor(x => x.Estado).IsInEnum().When(x => x.Estado.HasValue);
        RuleFor(x => x.ExpedienteId).NotEqual(Guid.Empty).When(x => x.ExpedienteId.HasValue);
        RuleFor(x => x.SucursalId).NotEqual(Guid.Empty).When(x => x.SucursalId.HasValue);
    }
}
