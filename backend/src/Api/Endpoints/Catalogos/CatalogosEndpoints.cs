using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.DatosMaestros.Application.Articulos;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>
/// Endpoints HTTP read-only para catálogos cross-empresa que viven en
/// el schema <c>compartido</c> (F7-PR1, MVP):
/// <list type="bullet">
///   <item><c>GET /api/v1/catalogos/proveedores</c> — list paginado con filtros.</item>
///   <item><c>GET /api/v1/catalogos/proveedores/{id}</c> — detalle.</item>
///   <item><c>GET /api/v1/catalogos/articulos</c> — list paginado con filtros.</item>
///   <item><c>GET /api/v1/catalogos/articulos/{id}</c> — detalle.</item>
/// </list>
///
/// <para>
/// Permiso: <c>compartido.catalogos.leer</c>. Read-only en MVP; CRUD
/// llegará cuando el cliente decida cómo administra estos catálogos
/// desde el ERP (post-import SAP).
/// </para>
/// </summary>
public static class CatalogosEndpoints
{
    // Tope duro de página para estos catálogos de typeahead. OJO: `limit` se
    // clampa a este máximo (Math.Min), así que pedir `limit:1000` devuelve
    // a lo más LimitMax — NO trae el catálogo completo. La resolución
    // id→etiqueta para detalle de req/OC NO debe depender de esta lista
    // capada; se hace server-side vía read-ports (ADR-0042 addendum).
    private const int LimitMax = 200;

    // Mapa de folding de acentos centralizado en
    // `PostgresFunctions.AcentosOrigen/Destino` (SharedKernel): fuente única
    // compartida con las queries admin de Datos Maestros. Built-in translate,
    // sin extensión unaccent (ADR-0045).

    public static IEndpointRouteBuilder MapCatalogosEndpoints(this IEndpointRouteBuilder app)
    {
        var proveedores = app.MapGroup("/api/v1/catalogos/proveedores").WithTags("Catalogos");
        var articulos = app.MapGroup("/api/v1/catalogos/articulos").WithTags("Catalogos");

        proveedores.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? clave,
            [FromQuery] string? nombre,
            [FromQuery] string? rfc,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            var off = offset is < 0 ? 0 : offset ?? 0;
            var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);

            IQueryable<Proveedor> q = db.Proveedores.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(p => p.Estatus == e);
            // `clave` y `nombre` son excluyentes, `clave` con precedencia (mismo
            // patrón que el endpoint de artículo). clave = código → lower() en
            // ambos lados. nombre = texto libre → folding case + acentos (ADR-0045)
            // sobre RazonSocial OR NombreComercial (este último nullable → null-guard),
            // reusando el mapa centralizado en PostgresFunctions.
#pragma warning disable CA1304, CA1311, CA1862
            if (!string.IsNullOrWhiteSpace(clave))
            {
                q = q.Where(p => p.Clave.ToLower().Contains(clave.ToLower()));
            }
            else if (!string.IsNullOrWhiteSpace(nombre))
            {
                string aguja = nombre;
                q = q.Where(p =>
                    PostgresFunctions.Translate(p.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                        .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino))
                    || (p.NombreComercial != null &&
                        PostgresFunctions.Translate(p.NombreComercial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                            .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino))));
            }

            // `rfc` es un filtro AND independiente (no participa en la
            // exclusividad clave/nombre): lo usa el auto-resolve de proveedor
            // desde el RFC emisor del CFDI en CxP. Match exacto
            // case-insensitive — el RFC es un identificador fiscal, no texto
            // libre.
            if (!string.IsNullOrWhiteSpace(rfc))
            {
                var rfcNormalizado = rfc.Trim();
                q = q.Where(p => p.Rfc.ToLower() == rfcNormalizado.ToLower());
            }
#pragma warning restore CA1304, CA1311, CA1862

            var total = await q.CountAsync(ct);
            var items = await q
                .OrderBy(p => p.Clave)
                .Skip(off).Take(lim)
                .Select(p => new ProveedorListItem(
                    p.Id, p.Clave, p.RazonSocial, p.NombreComercial, p.Rfc,
                    p.TipoPersona, p.CondicionesPagoDias, p.MonedaPreferidaId, p.Estatus))
                .ToListAsync(ct);

            return Results.Ok(new PagedCatalogoResponse<ProveedorListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarProveedores")
        .WithSummary("Listar proveedores cross-empresa")
        .WithDescription(
            "Lista paginada de proveedores en `compartido.proveedores`. " +
            "Filtros opcionales: `estatus` (Activo/Inactivo/EnRevisión), " +
            "`clave` (substring case-insensitive) y `nombre` (substring sobre " +
            "razón social o nombre comercial, insensible a acentos y mayúsculas " +
            "vía `translate`, ADR-0045). `clave` y `nombre` son excluyentes; si " +
            "llegan ambos, `clave` tiene precedencia. `rfc` es un filtro AND " +
            "independiente con match exacto case-insensitive (auto-resolve de " +
            "proveedor desde CFDI). Paginación offset-based con " +
            $"tope `limit={LimitMax}`. Permiso: `compartido.catalogos.leer`.")
        .Produces<PagedCatalogoResponse<ProveedorListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        proveedores.MapGet("/{id:guid}", async (
            Guid id, CompartidoDbContext db, CancellationToken ct) =>
        {
            var p = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new EntityNotFoundException(
                    "PROVEEDOR_NO_ENCONTRADO",
                    $"No se encontró proveedor con id '{id}'.");
            return Results.Ok(new ProveedorDetalle(
                p.Id, p.Clave, p.ClaveLegacy, p.RazonSocial, p.NombreComercial, p.Rfc,
                p.TipoPersona, p.CondicionesPagoDias, p.MonedaPreferidaId,
                p.Email, p.Telefono, p.Estatus));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ObtenerProveedorPorId")
        .WithSummary("Detalle de proveedor por id")
        .WithDescription(
            "Devuelve cabecera completa del proveedor (incluye `email`, " +
            "`telefono`, `claveLegacy` que el list no expone). 404 si el id " +
            "no existe.")
        .Produces<ProveedorDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        articulos.MapGet("/", async (
            [FromQuery] Naturaleza? naturaleza,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? clave,
            [FromQuery] string? nombre,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            var off = offset is < 0 ? 0 : offset ?? 0;
            var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);

            IQueryable<Articulo> q = db.Articulos.AsNoTracking();
            if (naturaleza is Naturaleza n) q = q.Where(a => a.Naturaleza == n);
            if (estatus is EstatusCatalogo e) q = q.Where(a => a.Estatus == e);
            // `clave` y `nombre` son excluyentes: se aplica el que venga con
            // valor, nunca ambos. `clave` tiene precedencia (comportamiento
            // histórico intacto). La búsqueda por nombre es insensible a
            // acentos y mayúsculas y matchea en cualquier posición: folding
            // con pg_catalog.translate sobre AMBOS lados (columna y aguja)
            // usando el mismo mapa, previo lower(). Ver ADR-0045.
            if (!string.IsNullOrWhiteSpace(clave))
            {
                // clave = código → case-insensitive con lower() en ambos lados.
#pragma warning disable CA1304, CA1311, CA1862
                q = q.Where(a => a.Clave.ToLower().Contains(clave.ToLower()));
#pragma warning restore CA1304, CA1311, CA1862
            }
            else if (!string.IsNullOrWhiteSpace(nombre))
            {
                string aguja = nombre;
                // CA1304/CA1311: `ToLower()` aquí NO se evalúa en .NET — vive en
                // un árbol de expresión que EF Core traduce al `lower()` de
                // PostgreSQL. La cultura es la del servidor (collation de la BD),
                // no la del proceso; el análisis de cultura no aplica.
#pragma warning disable CA1304, CA1311
                q = q.Where(a =>
                    PostgresFunctions.Translate(a.Nombre.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                        .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
#pragma warning restore CA1304, CA1311
            }

            var total = await q.CountAsync(ct);
            var items = await q
                .OrderBy(a => a.Clave)
                .Skip(off).Take(lim)
                .Select(a => new ArticuloListItem(
                    a.Id, a.Clave, a.Nombre, a.UnidadMedidaDefault,
                    a.UnidadMedidaId, a.Naturaleza, a.Categoria, a.CategoriaId, a.Estatus))
                .ToListAsync(ct);

            return Results.Ok(new PagedCatalogoResponse<ArticuloListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarArticulos")
        .WithSummary("Listar artículos cross-empresa")
        .WithDescription(
            "Lista paginada de artículos en `compartido.articulos`. Filtros " +
            "opcionales: `naturaleza` (Estandar/Servicio/Critico/Riesgo — " +
            "alimenta la matriz de aprobación A1 §3.bis), `estatus`, `clave` " +
            "(substring) y `nombre` (substring insensible a acentos y " +
            "mayúsculas vía `translate` nativo, ADR-0045). `clave` y `nombre` " +
            "son excluyentes; si llegan ambos, `clave` tiene precedencia. " +
            $"Paginación offset-based, tope `limit={LimitMax}`.")
        .Produces<PagedCatalogoResponse<ArticuloListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        articulos.MapGet("/{id:guid}", async (
            Guid id, CompartidoDbContext db, CancellationToken ct) =>
        {
            var a = await db.Articulos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new EntityNotFoundException(
                    "ARTICULO_NO_ENCONTRADO",
                    $"No se encontró artículo con id '{id}'.");
            return Results.Ok(new ArticuloDetalle(
                a.Id, a.Clave, a.ClaveLegacy, a.Nombre, a.DescripcionLarga,
                a.UnidadMedidaDefault, a.UnidadMedidaId, a.Naturaleza, a.Categoria,
                a.CategoriaId,
                a.PrecioReferenciaMonto, a.PrecioReferenciaMoneda, a.Estatus));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ObtenerArticuloPorId")
        .WithSummary("Detalle de artículo por id")
        .WithDescription(
            "Devuelve cabecera completa del artículo (incluye " +
            "`descripcionLarga`, `precioReferencia` y `claveLegacy` que el " +
            "list no expone). 404 si el id no existe.")
        .Produces<ArticuloDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // F9-PR1: reclasificar naturaleza en bulk (post-go-live cuando el
        // cliente decide ajustar la matriz A1 §3.bis.1).
        articulos.MapPost("/reclasificar-naturaleza", async (
            [FromBody] ReclasificarNaturalezaArticulosRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ReclasificarNaturalezaArticulosCommand(
                    request.ArticuloIds, request.Naturaleza, request.Motivo),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("ReclasificarNaturalezaArticulos")
        .WithSummary("Reclasificar naturaleza de un batch de artículos")
        .WithDescription(
            "Cambia la `Naturaleza` de hasta 500 artículos en un solo " +
            "request. Single SQL via `ExecuteUpdate`. IDs no encontrados " +
            "se ignoran silenciosamente — el contador devuelto refleja " +
            $"sólo los reclasificados. Tope de batch: " +
            $"{ReclasificarNaturalezaArticulosHandler.MaxBatchSize}. Permiso: " +
            "`compartido.catalogos.administrar`. Header `Idempotency-Key` " +
            "obligatorio (cambia la matriz de aprobación A1).")
        .Produces<ReclasificarNaturalezaArticulosResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // === B.5: CRUD de proveedores ===

        proveedores.MapPost("/", async (
            [FromBody] CrearProveedorRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new CrearProveedorCommand(
                    Clave: request.Clave,
                    RazonSocial: request.RazonSocial,
                    Rfc: request.Rfc,
                    TipoPersona: request.TipoPersona,
                    NombreComercial: request.NombreComercial,
                    CondicionesPagoDias: request.CondicionesPagoDias,
                    MonedaPreferidaId: request.MonedaPreferidaId,
                    Email: request.Email,
                    Telefono: request.Telefono),
                cancellationToken);
            return Results.Created($"/api/v1/catalogos/proveedores/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("CrearProveedor")
        .WithSummary("Crear proveedor (B.5)")
        .WithDescription(
            "Alta de proveedor. UNIQUE(clave) → 422 " +
            "`PROVEEDOR_CLAVE_DUPLICADA` si choca. Si llega " +
            "`monedaPreferidaId`, validación cross-table contra " +
            "`compartido.monedas` (404 si no existe). Header " +
            "`Idempotency-Key` obligatorio.")
        .Produces<CrearProveedorResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        proveedores.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarProveedorRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new ActualizarProveedorCommand(
                    ProveedorId: id,
                    RazonSocial: body.RazonSocial,
                    NombreComercial: body.NombreComercial,
                    Rfc: body.Rfc,
                    TipoPersona: body.TipoPersona,
                    CondicionesPagoDias: body.CondicionesPagoDias,
                    MonedaPreferidaId: body.MonedaPreferidaId,
                    Email: body.Email,
                    Telefono: body.Telefono,
                    LimpiarNombreComercial: body.LimpiarNombreComercial ?? false,
                    LimpiarCondicionesPago: body.LimpiarCondicionesPago ?? false,
                    LimpiarMonedaPreferida: body.LimpiarMonedaPreferida ?? false,
                    LimpiarEmail: body.LimpiarEmail ?? false,
                    LimpiarTelefono: body.LimpiarTelefono ?? false,
                    Banco: body.Banco,
                    Clabe: body.Clabe,
                    Beneficiario: body.Beneficiario,
                    LimpiarBanco: body.LimpiarBanco ?? false,
                    LimpiarClabe: body.LimpiarClabe ?? false,
                    LimpiarBeneficiario: body.LimpiarBeneficiario ?? false),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("ActualizarProveedor")
        .WithSummary("Editar proveedor (PATCH parcial, B.5)")
        .WithDescription(
            "PATCH parcial sobre los campos editables. Convención: " +
            "nullable `null` = no tocar; flag `limpiarX = true` = " +
            "setear nullable a null. Inmutables: `id`, `clave`, " +
            "`claveLegacy`. Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        proveedores.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(new DesactivarProveedorCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("DesactivarProveedor")
        .WithSummary("Desactivar proveedor (soft delete, B.5)")
        .WithDescription(
            "Soft delete: setea `Estatus = Inactivo`. Idempotente: si " +
            "ya está Inactivo, retorna 204 sin cambios. Las RQs " +
            "históricas siguen funcionando con el id; las nuevas RQs " +
            "se bloquean por la validación cross-table de F7-PR1. " +
            "Para reactivar, usar PATCH (cuando se necesite).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // === B.5: CRUD de artículos ===

        articulos.MapPost("/", async (
            [FromBody] CrearArticuloRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new CrearArticuloCommand(
                    Clave: request.Clave,
                    Nombre: request.Nombre,
                    UnidadMedidaId: request.UnidadMedidaId,
                    Naturaleza: request.Naturaleza,
                    DescripcionLarga: request.DescripcionLarga,
                    CategoriaId: request.CategoriaId,
                    PrecioReferenciaMonto: request.PrecioReferenciaMonto,
                    PrecioReferenciaMoneda: request.PrecioReferenciaMoneda),
                cancellationToken);
            return Results.Created($"/api/v1/catalogos/articulos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("CrearArticulo")
        .WithSummary("Crear artículo (B.5)")
        .WithDescription(
            "Alta de artículo. UNIQUE(clave) → 422 " +
            "`ARTICULO_CLAVE_DUPLICADA` si choca. Si llega " +
            "`precioReferenciaMoneda`, validación cross-table contra " +
            "`compartido.monedas` (422 `ARTICULO_MONEDA_NO_REGISTRADA` " +
            "si no existe). Header `Idempotency-Key` obligatorio.")
        .Produces<CrearArticuloResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        articulos.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarArticuloRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new ActualizarArticuloCommand(
                    ArticuloId: id,
                    Nombre: body.Nombre,
                    DescripcionLarga: body.DescripcionLarga,
                    UnidadMedidaId: body.UnidadMedidaId,
                    Naturaleza: body.Naturaleza,
                    CategoriaId: body.CategoriaId,
                    PrecioReferenciaMonto: body.PrecioReferenciaMonto,
                    PrecioReferenciaMoneda: body.PrecioReferenciaMoneda,
                    LimpiarDescripcionLarga: body.LimpiarDescripcionLarga ?? false,
                    LimpiarCategoria: body.LimpiarCategoria ?? false,
                    LimpiarPrecioReferencia: body.LimpiarPrecioReferencia ?? false),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("ActualizarArticulo")
        .WithSummary("Editar artículo (PATCH parcial, B.5)")
        .WithDescription(
            "PATCH parcial; mismas reglas que ActualizarProveedor. El " +
            "cambio individual de `naturaleza` aquí es complementario " +
            "al endpoint bulk `/reclasificar-naturaleza` de F9-PR1: " +
            "uno-a-uno cuando el admin solo necesita ajustar 1 " +
            "artículo, vs bulk cuando reclasifica un set grande.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        articulos.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(new DesactivarArticuloCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("DesactivarArticulo")
        .WithSummary("Desactivar artículo (soft delete, B.5)")
        .WithDescription("Mismas reglas que DesactivarProveedor.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Body del POST /articulos/reclasificar-naturaleza.</summary>
    public sealed record ReclasificarNaturalezaArticulosRequest(
        IReadOnlyList<Guid> ArticuloIds,
        Naturaleza Naturaleza,
        string? Motivo);

    /// <summary>Body del POST /proveedores (B.5).</summary>
    public sealed record CrearProveedorRequest(
        string Clave,
        string RazonSocial,
        string Rfc,
        TipoPersonaProveedor TipoPersona,
        string? NombreComercial,
        short? CondicionesPagoDias,
        Guid? MonedaPreferidaId,
        string? Email,
        string? Telefono);

    /// <summary>Body del PATCH /proveedores/{id} (B.5; datos bancarios TES-PR3). Todos opcionales.</summary>
    public sealed record ActualizarProveedorRequest(
        string? RazonSocial,
        string? NombreComercial,
        string? Rfc,
        TipoPersonaProveedor? TipoPersona,
        short? CondicionesPagoDias,
        Guid? MonedaPreferidaId,
        string? Email,
        string? Telefono,
        bool? LimpiarNombreComercial,
        bool? LimpiarCondicionesPago,
        bool? LimpiarMonedaPreferida,
        bool? LimpiarEmail,
        bool? LimpiarTelefono,
        string? Banco = null,
        string? Clabe = null,
        string? Beneficiario = null,
        bool? LimpiarBanco = null,
        bool? LimpiarClabe = null,
        bool? LimpiarBeneficiario = null);

    /// <summary>Body del POST /articulos (B.5).</summary>
    public sealed record CrearArticuloRequest(
        string Clave,
        string Nombre,
        Guid UnidadMedidaId,
        Naturaleza Naturaleza,
        string? DescripcionLarga,
        Guid? CategoriaId,
        decimal? PrecioReferenciaMonto,
        string? PrecioReferenciaMoneda);

    /// <summary>Body del PATCH /articulos/{id} (B.5). Todos opcionales.</summary>
    public sealed record ActualizarArticuloRequest(
        string? Nombre,
        string? DescripcionLarga,
        Guid? UnidadMedidaId,
        Naturaleza? Naturaleza,
        Guid? CategoriaId,
        decimal? PrecioReferenciaMonto,
        string? PrecioReferenciaMoneda,
        bool? LimpiarDescripcionLarga,
        bool? LimpiarCategoria,
        bool? LimpiarPrecioReferencia);
}

/// <summary>Wrapper de paginación offset-based para listados de catálogos.</summary>
public sealed record PagedCatalogoResponse<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record ProveedorListItem(
    Guid Id,
    string Clave,
    string RazonSocial,
    string? NombreComercial,
    string Rfc,
    TipoPersonaProveedor TipoPersona,
    short? CondicionesPagoDias,
    Guid? MonedaPreferidaId,
    EstatusCatalogo Estatus);

public sealed record ProveedorDetalle(
    Guid Id,
    string Clave,
    string? ClaveLegacy,
    string RazonSocial,
    string? NombreComercial,
    string Rfc,
    TipoPersonaProveedor TipoPersona,
    short? CondicionesPagoDias,
    Guid? MonedaPreferidaId,
    string? Email,
    string? Telefono,
    EstatusCatalogo Estatus);

public sealed record ArticuloListItem(
    Guid Id,
    string Clave,
    string Nombre,
    string UnidadMedidaDefault,
    Guid? UnidadMedidaId,
    Naturaleza Naturaleza,
    string? Categoria,
    Guid? CategoriaId,
    EstatusCatalogo Estatus);
