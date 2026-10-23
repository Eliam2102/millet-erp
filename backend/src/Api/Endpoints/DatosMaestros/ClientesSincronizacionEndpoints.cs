using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.DatosMaestros;

public sealed record IniciarSincronizacionClientesRequest(string? Tipo);
public sealed record ReintentarClienteAwRequest(string? Referencia);

public sealed record EjecucionSincronizacionResumen(
    Guid Id, string Tipo, string Estado, int Leidos, int Creados, int Actualizados, int SinCambios,
    int Pendientes, int Conflictos, int Errores, DateTimeOffset IniciadaEnUtc, DateTimeOffset? TerminadaEnUtc,
    string Actor, string? ErrorGeneral, string? ResumenProductos = null);

public sealed record EjecucionSincronizacionError(
    string Referencia, string Codigo, string Mensaje, DateTimeOffset OcurridoEnUtc);

public sealed record EjecucionSincronizacionDetalle(
    EjecucionSincronizacionResumen Ejecucion, IReadOnlyList<EjecucionSincronizacionError> Errores, bool ErroresTruncados);

public sealed record ClienteAwOrigenCampo(string Campo, string Etiqueta, string? Valor);

/// <summary>Cómo llegó una referencia de A+W y qué le falta para importarse.</summary>
public sealed record ClienteAwOrigenDetalle(
    string Referencia, IReadOnlyList<ClienteAwOrigenCampo> Campos, IReadOnlyList<AwCampoFaltante> Faltantes);

/// <summary>Área que valida y dónde se corrige cada código de error por fila (propuesta, se confirma con Millet).</summary>
public static class AreasIncidenciaAw
{
    public static (string Area, string CorrigeEn, string Paso) De(string codigo) => codigo switch
    {
        "fila_invalida" => ("Fiscal", "A+W", "Fiscal valida el dato correcto contra la constancia; después se corrige en A+W."),
        "no_encontrada_en_origen" => ("TI", "A+W", "TI confirma si la referencia se dio de baja o cambió de número en A+W."),
        "moneda_sin_equivalencia" => ("TI", "A+W", "TI confirma la moneda del cliente en A+W."),
        "aplicacion_fallida" => ("Soporte VILO", "ERP", "Soporte revisa la falla del ERP y reintenta."),
        _ => ("Soporte VILO", "ERP", "Soporte revisa el caso."),
    };
}

public sealed record ListarEjecucionesSincronizacionResponse(
    IReadOnlyList<EjecucionSincronizacionResumen> Items, int Total, int Offset, int Limit);

/// <summary>
/// ADM-06 Entrega D2: sincronización de clientes desde A+W. El POST de barrido solo ENCOLA
/// (202); lo ejecuta <c>AwClientesEjecucionDispatcher</c>. El reintento de una referencia es
/// inline y devuelve el estado real. Permiso: <c>datos_maestros.clientes.sincronizar</c>.
/// </summary>
public static class ClientesSincronizacionEndpoints
{
    private const int MaxErroresDetalle = 200;
    private const string Base = "/api/v1/datos-maestros/clientes/sincronizacion";

    public static IEndpointRouteBuilder MapClientesSincronizacionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Base).WithTags("DatosMaestros");
        var policy = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesSincronizar;

        group.MapPost("/ejecuciones", async (
            [FromBody] IniciarSincronizacionClientesRequest body,
            AwClientesSincronizador sync,
            ICurrentUserContext user,
            CancellationToken ct) =>
        {
            if (!string.Equals(body.Tipo, "Barrido", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("AW_CLIENTES_TIPO_INVALIDO", "Solo se admite tipo 'Barrido'.");
            var id = await sync.IniciarBarridoAsync(Actor(user), ct);
            return Results.Accepted($"{Base}/ejecuciones/{id}", new { id, estado = nameof(AwClientesEjecucionEstado.Pendiente) });
        })
        .RequireAuthorization(policy)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("IniciarSincronizacionClientesAw")
        .WithSummary("Encolar un barrido de clientes A+W (no ejecuta inline)")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/ejecuciones", async (
            [FromQuery] AwClientesEjecucionEstado? estado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IntegracionesAwDbContext db,
            CancellationToken ct) =>
        {
            var off = Math.Max(0, offset ?? 0);
            var lim = Math.Clamp(limit ?? 20, 1, 100);
            var q = db.ClientesEjecuciones.AsNoTracking();
            if (estado is not null) q = q.Where(e => e.Estado == estado);
            var total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(e => e.IniciadaEnUtc).Skip(off).Take(lim).ToListAsync(ct);
            return Results.Ok(new ListarEjecucionesSincronizacionResponse(items.Select(Resumen).ToList(), total, off, lim));
        })
        .RequireAuthorization(policy)
        .WithName("ListarEjecucionesSincronizacionClientesAw")
        .WithSummary("Listar ejecuciones de sincronización de clientes A+W")
        .Produces<ListarEjecucionesSincronizacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/ejecuciones/{id:guid}", async (Guid id, IntegracionesAwDbContext db, CancellationToken ct) =>
            Results.Ok(await DetalleAsync(db, id, ct)))
        .RequireAuthorization(policy)
        .WithName("ObtenerEjecucionSincronizacionClientesAw")
        .WithSummary("Detalle de una ejecución (contadores y hasta 200 errores por referencia)")
        .Produces<EjecucionSincronizacionDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/reintentos", async (
            [FromBody] ReintentarClienteAwRequest body,
            AwClientesSincronizador sync,
            IntegracionesAwDbContext db,
            ICurrentUserContext user,
            CancellationToken ct) =>
        {
            var id = await sync.ReintentarReferenciaAsync(body.Referencia ?? string.Empty, Actor(user), ct);
            return Results.Ok(await DetalleAsync(db, id, ct));
        })
        .RequireAuthorization(policy)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReintentarClienteAw")
        .WithSummary("Relee una referencia del origen y la aplica (inline, estado real)")
        .Produces<EjecucionSincronizacionDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/ejecuciones/{id:guid}/solicitudes", async (
            Guid id, IntegracionesAwDbContext db, IAwClientesOrigen origen, CancellationToken ct) =>
        {
            var d = await DetalleAsync(db, id, ct);
            var filas = new List<IReadOnlyDictionary<string, string?>>();
            foreach (var er in d.Errores.Where(x => x.Codigo != "conflicto_correlacion"))
            {
                var (area, corrigeEn, paso) = AreasIncidenciaAw.De(er.Codigo);
                string? llego = null, falta = null, debe = null;
                if (er.Codigo == "fila_invalida" && await origen.LeerPorReferenciaAsync(er.Referencia, ct) is { } f)
                {
                    var faltantes = AwClienteSnapshotMapper.Faltantes(f);
                    llego = $"NAME1: {f.Name1 ?? "(vacío)"} · NAME2: {f.Name2 ?? "(vacío)"} · RFC: {f.UstId ?? "(vacío)"} · Bloqueado: {(f.KzGesperrt is null or 0 ? "No" : "Sí")}";
                    falta = string.Join("; ", faltantes.Select(x => $"{x.Campo} ({x.Etiqueta}): {x.Regla}"));
                    debe = string.Join("; ", faltantes.Select(x => $"{x.Campo}: {x.ComoDebeQuedar}"));
                    if (faltantes.Count > 0) (area, corrigeEn) = (faltantes[0].Area, faltantes[0].CorrigeEn);
                }
                filas.Add(new Dictionary<string, string?>
                {
                    ["area"] = area, ["corrigeEn"] = corrigeEn, ["paso"] = paso, ["referencia"] = er.Referencia,
                    ["codigo"] = er.Codigo, ["error"] = er.Mensaje, ["comoLlego"] = llego, ["queFalta"] = falta ?? er.Mensaje,
                    ["comoDebeQuedar"] = debe,
                });
            }
            filas = filas.OrderBy(x => x["area"]).ThenBy(x => x["referencia"]).ToList();
            return Results.Ok(new ReporteDiferenciasAwDto(
                $"Solicitudes de corrección por área · corrida {d.Ejecucion.IniciadaEnUtc:dd/MM/yyyy HH:mm} UTC", DateTimeOffset.UtcNow,
                new Dictionary<string, string?> { ["Corrida"] = id.ToString() },
                [
                    new("area", "Área que valida", "texto"), new("corrigeEn", "Dónde se corrige", "texto"),
                    new("paso", "Siguiente paso", "texto"), new("referencia", "Referencia A+W", "texto"),
                    new("codigo", "Código", "texto"), new("comoLlego", "Cómo llegó de A+W", "texto"),
                    new("queFalta", "Qué falta", "texto"), new("comoDebeQuedar", "Cómo debe quedar", "texto"),
                ],
                filas, new Dictionary<string, decimal> { ["solicitudes"] = filas.Count }, d.ErroresTruncados));
        })
        .RequireAuthorization(policy, PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesOrigenVer)
        .WithName("SolicitudesCorreccionClientesAw")
        .WithSummary("Errores de la corrida agrupados por área que valida y dónde se corrigen (JSON de reporte ADR-0036)")
        .Produces<ReporteDiferenciasAwDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/origen/{referencia}", async (string referencia, IAwClientesOrigen origen, CancellationToken ct) =>
        {
            var f = await origen.LeerPorReferenciaAsync(referencia, ct)
                ?? throw new EntityNotFoundException("AW_CLIENTE_NO_ENCONTRADO", "La referencia no existe en el origen A+W.");
            ClienteAwOrigenCampo C(string campo, string etiqueta, object? v) =>
                new(campo, etiqueta, v is null || (v is string t && string.IsNullOrWhiteSpace(t)) ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim());
            return Results.Ok(new ClienteAwOrigenDetalle(f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [
                C("NAME1", "Nombre o razón social", f.Name1), C("NAME2", "Nombre 2", f.Name2), C("NAME3", "Nombre 3", f.Name3),
                C("UST_ID", "RFC", f.UstId), C("STRASSE", "Calle", f.Strasse), C("ORT", "Ciudad", f.Ort),
                C("PLZ", "Código postal", f.Plz), C("PROVINZ", "Estado", f.Provinz), C("LAND", "País", f.Land),
                C("WAEHRUNG", "Moneda", f.Waehrung), C("ZAHLBED", "Condición de pago", f.Zahlbed),
                C("KZ_GESPERRT", "Bloqueado en A+W", f.KzGesperrt is null ? null : f.KzGesperrt == 0 ? "No" : "Sí"),
                C("DATUM", "Fecha de alta", f.Datum?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            ],
            AwClienteSnapshotMapper.Faltantes(f)));
        })
        .RequireAuthorization(policy, PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesOrigenVer)
        .WithName("ObtenerOrigenClienteAw")
        .WithSummary("Cómo llegó una referencia de A+W (lectura en vivo) y qué datos obligatorios le faltan")
        .Produces<ClienteAwOrigenDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static string Actor(ICurrentUserContext user) =>
        user.UserName ?? user.Email ?? user.UserId?.ToString() ?? "desconocido";

    private static async Task<EjecucionSincronizacionDetalle> DetalleAsync(
        IntegracionesAwDbContext db, Guid id, CancellationToken ct)
    {
        var e = await db.ClientesEjecuciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new AwClientesSyncException("ejecucion_no_encontrada", "Ejecución de clientes inexistente.");
        var errores = await db.Set<AwClientesEjecucionError>().AsNoTracking()
            .Where(x => x.EjecucionId == id)
            .OrderBy(x => x.OcurridoEnUtc).ThenBy(x => x.Referencia)
            .Take(MaxErroresDetalle + 1)
            .Select(x => new EjecucionSincronizacionError(x.Referencia, x.Codigo, x.Mensaje, x.OcurridoEnUtc))
            .ToListAsync(ct);
        return new(Resumen(e), errores.Take(MaxErroresDetalle).ToList(), errores.Count > MaxErroresDetalle);
    }

    private static EjecucionSincronizacionResumen Resumen(AwClientesEjecucion e) => new(
        e.Id, e.Tipo.ToString(), e.Estado.ToString(), e.Leidos, e.Creados, e.Actualizados, e.SinCambios,
        e.Pendientes, e.Conflictos, e.Errores, e.IniciadaEnUtc, e.TerminadaEnUtc, e.Actor, e.ErrorGeneral, e.ResumenProductos);
}
