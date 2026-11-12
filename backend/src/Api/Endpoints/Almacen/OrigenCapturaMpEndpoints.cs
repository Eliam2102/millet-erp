using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.AppAlmacenes;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// M-335: origen único de captura de materia prima por evento (recepción, consumo, merma, requisición): ERP o app de Almacenes.
/// Antes de cambiar se muestran los pendientes (mensajes en revisión y sin acuse); con pendientes no procede. El cambio queda en la
/// bitácora y se revierte con otro cambio. Lo autoriza quien ejecuta el cierre mensual de Almacén (autoridad corporativa del
/// inventario, <c>almacen.cierre-mes.gestionar-todas-sucursales</c>) mientras Millet no nombre a otro responsable.
/// </summary>
public static class OrigenCapturaMpEndpoints
{
    public sealed record PendientesResponse(int MensajesEnRevision, int? MensajesSinAcuse);
    public sealed record EventoResponse(string Evento, string Nombre, string Origen, DateTimeOffset? Desde, PendientesResponse Pendientes);
    public sealed record CambioResponse(Guid Id, string Evento, string? OrigenAnterior, string Origen, string Motivo, string CambiadoPor,
        DateTimeOffset CambiadoAt);
    public sealed record OrigenCapturaResponse(IReadOnlyList<EventoResponse> Eventos, IReadOnlyList<CambioResponse> Bitacora);
    public sealed record CambiarOrigenRequest(string Origen, string Motivo);

    private const string Permiso = PermisosCanonicos.AlmacenCierreMesGestionarTodasSucursales;

    public static IEndpointRouteBuilder MapOrigenCapturaMpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/origen-captura-mp").WithTags("Almacen");

        group.MapGet("/", async (AlmacenDbContext db, IUsuarioReadPort usuarios, IServiceProvider sp, CancellationToken ct) =>
            Results.Ok(await LeerAsync(db, usuarios, await SinAcuseAsync(sp, ct), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + Permiso)
            .WithName("ConsultarOrigenCapturaMp")
            .Produces<OrigenCapturaResponse>(StatusCodes.Status200OK);

        group.MapPost("/{evento}", async (string evento, CambiarOrigenRequest req, AlmacenDbContext db, IUsuarioReadPort usuarios,
            ICurrentUserContext user, IServiceProvider sp, CancellationToken ct) =>
        {
            var usuarioId = user.UserId ?? throw new BusinessRuleException("ORIGEN_CAPTURA_SIN_USUARIO", "El usuario es requerido.");
            await OrigenCapturaMpReglas.CambiarAsync(db, evento, (req.Origen ?? "").Trim().ToUpperInvariant(), req.Motivo ?? "",
                usuarioId, await SinAcuseAsync(sp, ct), ct);
            return Results.Ok(await LeerAsync(db, usuarios, await SinAcuseAsync(sp, ct), ct));
        })
            .RequireAuthorization(PermissionPolicyProvider.Prefix + Permiso)
            .WithName("CambiarOrigenCapturaMp")
            .Produces<OrigenCapturaResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Sin acuse en <c>alm_mensaje</c> (todos los tipos: la tabla no se filtra por evento). Null si la lectura está apagada.</summary>
    private static async Task<int?> SinAcuseAsync(IServiceProvider sp, CancellationToken ct)
    {
        if (!sp.GetRequiredService<IOptions<AlmMensajeOptions>>().Value.Habilitado) return null;
        if (sp.GetService<IAlmMensajeOrigen>() is not { } origen) return null;
        return (await origen.ContarSinAcuseAntiguosAsync(0, ct)).Cantidad;
    }

    private static async Task<OrigenCapturaResponse> LeerAsync(AlmacenDbContext db, IUsuarioReadPort usuarios, int? sinAcuse,
        CancellationToken ct)
    {
        var eventos = new List<EventoResponse>();
        foreach (var e in OrigenCapturaMp.Eventos)
        {
            var v = await OrigenCapturaMpReglas.VigenteAsync(db, e, ct);
            var p = await OrigenCapturaMpReglas.PendientesAsync(db, e, sinAcuse, ct);
            eventos.Add(new(e, OrigenCapturaMp.NombreEvento(e), v.Origen, v.Desde, new(p.MensajesEnRevision, p.MensajesSinAcuse)));
        }
        var cambios = await db.OrigenesCapturaMp.AsNoTracking().OrderByDescending(o => o.CambiadoAt).Take(100).ToListAsync(ct);
        var nombres = await usuarios.ObtenerNombresAsync(cambios.Select(c => c.CambiadoPor).Distinct().ToList(), ct);
        return new(eventos, cambios.Select(c => new CambioResponse(c.Id, c.Evento, c.OrigenAnterior, c.Origen, c.Motivo,
            nombres.GetValueOrDefault(c.CambiadoPor, "—"), c.CambiadoAt)).ToList());
    }
}
