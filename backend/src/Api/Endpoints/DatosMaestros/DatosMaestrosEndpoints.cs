using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Articulos;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Application.Proveedores;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.DatosMaestros;

/// <summary>
/// Endpoints HTTP read-only enriquecidos del módulo Datos Maestros
/// (F-Admin-PR4.5):
/// <list type="bullet">
///   <item><c>GET /api/v1/datos-maestros/proveedores</c> — list paginado
///         con filtros enriquecidos (rfc, razonSocial, tipoPersona, estatus).</item>
///   <item><c>GET /api/v1/datos-maestros/proveedores/{id}</c> — detalle.</item>
///   <item><c>GET /api/v1/datos-maestros/articulos</c> — list paginado con
///         filtros (codigo, descripcion, naturaleza, unidadMedida, estatus).</item>
///   <item><c>GET /api/v1/datos-maestros/articulos/{id}</c> — detalle.</item>
/// </list>
///
/// <para>
/// Los endpoints de mutación (POST/PATCH/DELETE) siguen viviendo en
/// <see cref="Millet.Api.Endpoints.Catalogos.CatalogosEndpoints"/> bajo
/// <c>/api/v1/catalogos/proveedores</c> y <c>/api/v1/catalogos/articulos</c>
/// (legacy F7-PR1, B.5). Esta clase solo agrega rutas de read enriquecidas
/// bajo el nuevo namespace <c>/datos-maestros/*</c>. Permiso:
/// <c>datos_maestros.proveedores.gestionar</c> /
/// <c>datos_maestros.articulos.gestionar</c>.
/// </para>
/// </summary>
public static class DatosMaestrosEndpoints
{
    public static IEndpointRouteBuilder MapDatosMaestrosEndpoints(this IEndpointRouteBuilder app)
    {
        var proveedores = app.MapGroup("/api/v1/datos-maestros/proveedores")
            .WithTags("DatosMaestros");
        var articulos = app.MapGroup("/api/v1/datos-maestros/articulos")
            .WithTags("DatosMaestros");

        // ===== PROVEEDORES =====
        proveedores.MapGet("/", async (
            [FromQuery] string? rfc,
            [FromQuery] string? razonSocial,
            [FromQuery] TipoPersonaProveedor? tipoPersona,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarProveedoresQuery(
                    Rfc: rfc,
                    RazonSocial: razonSocial,
                    TipoPersona: tipoPersona,
                    Estatus: estatus,
                    Offset: offset ?? 0,
                    Limit: limit ?? 50),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProveedoresGestionar)
        .WithName("ListarProveedoresDatosMaestros")
        .WithSummary("Listar proveedores con filtros enriquecidos (F-Admin-PR4.5)")
        .Produces<ListarProveedoresResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        proveedores.MapGet("/{id:guid}", async (
            Guid id, CompartidoDbContext db, CancellationToken ct) =>
        {
            var p = await db.Proveedores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new EntityNotFoundException(
                    "PROVEEDOR_NO_ENCONTRADO",
                    $"No existe proveedor con id '{id}'.");
            return Results.Ok(new ProveedorDetalle(
                p.Id, p.Clave, p.ClaveLegacy, p.RazonSocial, p.NombreComercial,
                p.Rfc, p.TipoPersona, p.CondicionesPagoDias, p.MonedaPreferidaId,
                p.Email, p.Telefono, p.Estatus));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProveedoresGestionar)
        .WithName("ObtenerProveedorDatosMaestros")
        .WithSummary("Detalle de proveedor (F-Admin-PR4.5)")
        .Produces<ProveedorDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ===== ARTÍCULOS =====
        articulos.MapGet("/", async (
            [FromQuery] string? codigo,
            [FromQuery] string? descripcion,
            [FromQuery] Naturaleza? naturaleza,
            [FromQuery] string? unidadMedidaDefault,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarArticulosQuery(
                    Codigo: codigo,
                    Descripcion: descripcion,
                    Naturaleza: naturaleza,
                    UnidadMedidaDefault: unidadMedidaDefault,
                    Estatus: estatus,
                    Offset: offset ?? 0,
                    Limit: limit ?? 50),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosArticulosGestionar)
        .WithName("ListarArticulosDatosMaestros")
        .WithSummary("Listar artículos con filtros enriquecidos (F-Admin-PR4.5)")
        .Produces<ListarArticulosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        articulos.MapGet("/{id:guid}", async (
            Guid id, CompartidoDbContext db, CancellationToken ct) =>
        {
            var a = await db.Articulos.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new EntityNotFoundException(
                    "ARTICULO_NO_ENCONTRADO",
                    $"No existe artículo con id '{id}'.");
            return Results.Ok(new ArticuloDetalle(
                a.Id, a.Clave, a.ClaveLegacy, a.Nombre, a.DescripcionLarga,
                a.UnidadMedidaDefault, a.UnidadMedidaId, a.Naturaleza, a.Categoria,
                a.CategoriaId,
                a.PrecioReferenciaMonto, a.PrecioReferenciaMoneda, a.Estatus));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosArticulosGestionar)
        .WithName("ObtenerArticuloDatosMaestros")
        .WithSummary("Detalle de artículo (F-Admin-PR4.5)")
        .Produces<ArticuloDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ===== CLIENTES (ADR-0048 D6) =====
        var clientes = app.MapGroup("/api/v1/datos-maestros/clientes")
            .WithTags("DatosMaestros");

        clientes.MapGet("/", async (
            [FromQuery] string? rfc,
            [FromQuery] string? razonSocial,
            [FromQuery] OrigenMaster? origen,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] bool? fiscalesIncompletos,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarClientesQuery(
                    Rfc: rfc,
                    RazonSocial: razonSocial,
                    Origen: origen,
                    Estatus: estatus,
                    FiscalesIncompletos: fiscalesIncompletos,
                    Offset: offset ?? 0,
                    Limit: limit ?? 50),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesGestionar)
        .WithName("ListarClientesDatosMaestros")
        .WithSummary("Listar clientes del master (ADR-0048)")
        .Produces<ListarClientesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        clientes.MapGet("/{id:guid}", async (
            Guid id, CompartidoDbContext db, CancellationToken ct) =>
        {
            var c = await db.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new EntityNotFoundException(
                    "CLIENTE_NO_ENCONTRADO",
                    $"No existe cliente con id '{id}'.");
            return Results.Ok(new ClienteDetalle(
                c.Id, c.Clave, c.ReferenciaExterna, c.RazonSocial, c.Rfc,
                c.RegimenFiscal, c.CodigoPostalFiscal, c.UsoCfdiDefault,
                c.FormaPagoDefault, c.MetodoPagoDefault, c.MonedaDefault,
                c.EsGenerico, c.Origen, c.Email, c.Telefono,
                c.NumRegIdTrib, c.PaisResidencia, c.DomicilioExtranjeroCalle,
                c.DomicilioExtranjeroEstado, c.DomicilioExtranjeroCodigoPostal,
                c.DatosFiscalesCompletos, c.Estatus));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesGestionar)
        .WithName("ObtenerClienteDatosMaestros")
        .WithSummary("Detalle de cliente (ADR-0048)")
        .Produces<ClienteDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        clientes.MapPost("/", async (
            [FromBody] CrearClienteRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new CrearClienteCommand(
                    Clave: request.Clave,
                    RazonSocial: request.RazonSocial,
                    ReferenciaExterna: request.ReferenciaExterna,
                    Rfc: request.Rfc,
                    RegimenFiscal: request.RegimenFiscal,
                    CodigoPostalFiscal: request.CodigoPostalFiscal,
                    UsoCfdiDefault: request.UsoCfdiDefault,
                    FormaPagoDefault: request.FormaPagoDefault,
                    MetodoPagoDefault: request.MetodoPagoDefault,
                    MonedaDefault: request.MonedaDefault ?? "MXN",
                    EsGenerico: request.EsGenerico ?? false,
                    Email: request.Email,
                    Telefono: request.Telefono,
                    NumRegIdTrib: request.NumRegIdTrib,
                    PaisResidencia: request.PaisResidencia,
                    DomicilioExtranjeroCalle: request.DomicilioExtranjeroCalle,
                    DomicilioExtranjeroEstado: request.DomicilioExtranjeroEstado,
                    DomicilioExtranjeroCodigoPostal: request.DomicilioExtranjeroCodigoPostal),
                ct);
            return Results.Created($"/api/v1/datos-maestros/clientes/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesGestionar)
        .WithName("CrearCliente")
        .WithSummary("Crear cliente manual (ADR-0048)")
        .WithDescription(
            "Alta manual (`origen = Manual`). UNIQUE(clave) → 422 " +
            "`CLIENTE_CLAVE_DUPLICADA`; UNIQUE(referenciaExterna) → 422 " +
            "`CLIENTE_REFERENCIA_DUPLICADA`. Datos fiscales incompletos NO " +
            "bloquean el alta (bloquean timbrado). Header `Idempotency-Key` " +
            "obligatorio.")
        .Produces<CrearClienteResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        clientes.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarClienteRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(
                new ActualizarClienteCommand(
                    ClienteId: id,
                    RazonSocial: body.RazonSocial,
                    Rfc: body.Rfc,
                    RegimenFiscal: body.RegimenFiscal,
                    CodigoPostalFiscal: body.CodigoPostalFiscal,
                    UsoCfdiDefault: body.UsoCfdiDefault,
                    FormaPagoDefault: body.FormaPagoDefault,
                    MetodoPagoDefault: body.MetodoPagoDefault,
                    MonedaDefault: body.MonedaDefault,
                    EsGenerico: body.EsGenerico,
                    Email: body.Email,
                    Telefono: body.Telefono,
                    LimpiarRfc: body.LimpiarRfc ?? false,
                    LimpiarRegimenFiscal: body.LimpiarRegimenFiscal ?? false,
                    LimpiarCodigoPostalFiscal: body.LimpiarCodigoPostalFiscal ?? false,
                    LimpiarUsoCfdiDefault: body.LimpiarUsoCfdiDefault ?? false,
                    LimpiarFormaPagoDefault: body.LimpiarFormaPagoDefault ?? false,
                    LimpiarMetodoPagoDefault: body.LimpiarMetodoPagoDefault ?? false,
                    LimpiarEmail: body.LimpiarEmail ?? false,
                    LimpiarTelefono: body.LimpiarTelefono ?? false,
                    NumRegIdTrib: body.NumRegIdTrib,
                    PaisResidencia: body.PaisResidencia,
                    DomicilioExtranjeroCalle: body.DomicilioExtranjeroCalle,
                    DomicilioExtranjeroEstado: body.DomicilioExtranjeroEstado,
                    DomicilioExtranjeroCodigoPostal: body.DomicilioExtranjeroCodigoPostal,
                    LimpiarNumRegIdTrib: body.LimpiarNumRegIdTrib ?? false,
                    LimpiarPaisResidencia: body.LimpiarPaisResidencia ?? false,
                    LimpiarDomicilioExtranjeroCalle: body.LimpiarDomicilioExtranjeroCalle ?? false,
                    LimpiarDomicilioExtranjeroEstado: body.LimpiarDomicilioExtranjeroEstado ?? false,
                    LimpiarDomicilioExtranjeroCodigoPostal: body.LimpiarDomicilioExtranjeroCodigoPostal ?? false),
                ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesGestionar)
        .WithName("ActualizarCliente")
        .WithSummary("Editar cliente (PATCH parcial, ADR-0048)")
        .WithDescription(
            "PATCH parcial: `null` = no tocar; `limpiarX = true` = setear " +
            "nullable a null. Inmutables: `id`, `clave`, `referenciaExterna` " +
            "(correlación A+W) y `origen`. Es la vía para completar " +
            "RFC/régimen/CP de clientes auto-provisionados antes de timbrar. " +
            "Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        clientes.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new DesactivarClienteCommand(id), ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesGestionar)
        .WithName("DesactivarCliente")
        .WithSummary("Desactivar cliente (soft delete, ADR-0048)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // ===== PRODUCTOS A+W (ADR-0048 D5) =====
        var productosAw = app.MapGroup("/api/v1/datos-maestros/productos-aw")
            .WithTags("DatosMaestros");

        productosAw.MapGet("/", async (
            [FromQuery] string? referencia,
            [FromQuery] string? descripcion,
            [FromQuery] OrigenMaster? origen,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] bool? fiscalesIncompletos,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarProductosAwQuery(
                    Referencia: referencia,
                    Descripcion: descripcion,
                    Origen: origen,
                    Estatus: estatus,
                    FiscalesIncompletos: fiscalesIncompletos,
                    Offset: offset ?? 0,
                    Limit: limit ?? 50),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar)
        .WithName("ListarProductosAw")
        .WithSummary("Listar productos de venta A+W (ADR-0048)")
        .Produces<ListarProductosAwResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        productosAw.MapGet("/{id:guid}", async (
            Guid id, CompartidoDbContext db, CancellationToken ct) =>
        {
            var p = await db.ProductosAw.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new EntityNotFoundException(
                    "PRODUCTO_AW_NO_ENCONTRADO",
                    $"No existe producto A+W con id '{id}'.");
            return Results.Ok(new ProductoAwDetalle(
                p.Id, p.ReferenciaExterna, p.Descripcion, p.UnidadMedida,
                p.UnidadMedidaId, p.CategoriaId, p.ClaveProdServSat,
                p.ClaveUnidadSat, p.ObjetoImp, p.TasaIvaTraslado,
                p.TasaRetencionIva, p.TasaRetencionIsr,
                p.FraccionArancelaria, p.UnidadAduana, p.PesoUnitarioKg, p.Origen,
                p.DatosFiscalesCompletos, p.Estatus));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar)
        .WithName("ObtenerProductoAw")
        .WithSummary("Detalle de producto A+W (ADR-0048)")
        .Produces<ProductoAwDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        productosAw.MapPost("/", async (
            [FromBody] CrearProductoAwRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new CrearProductoAwCommand(
                    ReferenciaExterna: request.ReferenciaExterna,
                    Descripcion: request.Descripcion,
                    UnidadMedida: request.UnidadMedida,
                    UnidadMedidaId: request.UnidadMedidaId,
                    CategoriaId: request.CategoriaId,
                    ClaveProdServSat: request.ClaveProdServSat,
                    ClaveUnidadSat: request.ClaveUnidadSat,
                    ObjetoImp: request.ObjetoImp,
                    TasaIvaTraslado: request.TasaIvaTraslado,
                    TasaRetencionIva: request.TasaRetencionIva,
                    TasaRetencionIsr: request.TasaRetencionIsr,
                    FraccionArancelaria: request.FraccionArancelaria,
                    UnidadAduana: request.UnidadAduana,
                    PesoUnitarioKg: request.PesoUnitarioKg),
                ct);
            return Results.Created($"/api/v1/datos-maestros/productos-aw/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar)
        .WithName("CrearProductoAw")
        .WithSummary("Crear producto A+W manual (ADR-0048)")
        .WithDescription(
            "Alta manual (`origen = Manual`). UNIQUE(referenciaExterna) → 422 " +
            "`PRODUCTO_AW_REFERENCIA_DUPLICADA`. Claves SAT incompletas NO " +
            "bloquean el alta (bloquean timbrado). Header `Idempotency-Key` " +
            "obligatorio.")
        .Produces<CrearProductoAwResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        productosAw.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarProductoAwRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(
                new ActualizarProductoAwCommand(
                    ProductoAwId: id,
                    Descripcion: body.Descripcion,
                    UnidadMedida: body.UnidadMedida,
                    UnidadMedidaId: body.UnidadMedidaId,
                    CategoriaId: body.CategoriaId,
                    ClaveProdServSat: body.ClaveProdServSat,
                    ClaveUnidadSat: body.ClaveUnidadSat,
                    ObjetoImp: body.ObjetoImp,
                    TasaIvaTraslado: body.TasaIvaTraslado,
                    TasaRetencionIva: body.TasaRetencionIva,
                    TasaRetencionIsr: body.TasaRetencionIsr,
                    FraccionArancelaria: body.FraccionArancelaria,
                    UnidadAduana: body.UnidadAduana,
                    PesoUnitarioKg: body.PesoUnitarioKg,
                    LimpiarCategoria: body.LimpiarCategoria ?? false,
                    LimpiarTasaIvaTraslado: body.LimpiarTasaIvaTraslado ?? false,
                    LimpiarTasaRetencionIva: body.LimpiarTasaRetencionIva ?? false,
                    LimpiarTasaRetencionIsr: body.LimpiarTasaRetencionIsr ?? false,
                    LimpiarFraccionArancelaria: body.LimpiarFraccionArancelaria ?? false,
                    LimpiarUnidadAduana: body.LimpiarUnidadAduana ?? false,
                    LimpiarPesoUnitarioKg: body.LimpiarPesoUnitarioKg ?? false),
                ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar)
        .WithName("ActualizarProductoAw")
        .WithSummary("Editar producto A+W (PATCH parcial, ADR-0048)")
        .WithDescription(
            "PATCH parcial: `null` = no tocar; `limpiarX = true` = borrar. " +
            "Inmutables: `id`, `referenciaExterna` (correlación A+W) y " +
            "`origen`. Es la vía para completar claves SAT de productos " +
            "auto-provisionados antes de timbrar. Header `Idempotency-Key` " +
            "obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        productosAw.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new DesactivarProductoAwCommand(id), ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar)
        .WithName("DesactivarProductoAw")
        .WithSummary("Desactivar producto A+W (soft delete, ADR-0048)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record ClienteDetalle(
        Guid Id,
        string Clave,
        string? ReferenciaExterna,
        string RazonSocial,
        string? Rfc,
        string? RegimenFiscal,
        string? CodigoPostalFiscal,
        string? UsoCfdiDefault,
        string? FormaPagoDefault,
        string? MetodoPagoDefault,
        string MonedaDefault,
        bool EsGenerico,
        OrigenMaster Origen,
        string? Email,
        string? Telefono,
        string? NumRegIdTrib,
        string? PaisResidencia,
        string? DomicilioExtranjeroCalle,
        string? DomicilioExtranjeroEstado,
        string? DomicilioExtranjeroCodigoPostal,
        bool DatosFiscalesCompletos,
        EstatusCatalogo Estatus);

    public sealed record CrearClienteRequest(
        string Clave,
        string RazonSocial,
        string? ReferenciaExterna,
        string? Rfc,
        string? RegimenFiscal,
        string? CodigoPostalFiscal,
        string? UsoCfdiDefault,
        string? FormaPagoDefault,
        string? MetodoPagoDefault,
        string? MonedaDefault,
        bool? EsGenerico,
        string? Email,
        string? Telefono,
        string? NumRegIdTrib = null,
        string? PaisResidencia = null,
        string? DomicilioExtranjeroCalle = null,
        string? DomicilioExtranjeroEstado = null,
        string? DomicilioExtranjeroCodigoPostal = null);

    public sealed record ActualizarClienteRequest(
        string? RazonSocial,
        string? Rfc,
        string? RegimenFiscal,
        string? CodigoPostalFiscal,
        string? UsoCfdiDefault,
        string? FormaPagoDefault,
        string? MetodoPagoDefault,
        string? MonedaDefault,
        bool? EsGenerico,
        string? Email,
        string? Telefono,
        bool? LimpiarRfc,
        bool? LimpiarRegimenFiscal,
        bool? LimpiarCodigoPostalFiscal,
        bool? LimpiarUsoCfdiDefault,
        bool? LimpiarFormaPagoDefault,
        bool? LimpiarMetodoPagoDefault,
        bool? LimpiarEmail,
        bool? LimpiarTelefono,
        string? NumRegIdTrib = null,
        string? PaisResidencia = null,
        string? DomicilioExtranjeroCalle = null,
        string? DomicilioExtranjeroEstado = null,
        string? DomicilioExtranjeroCodigoPostal = null,
        bool? LimpiarNumRegIdTrib = null,
        bool? LimpiarPaisResidencia = null,
        bool? LimpiarDomicilioExtranjeroCalle = null,
        bool? LimpiarDomicilioExtranjeroEstado = null,
        bool? LimpiarDomicilioExtranjeroCodigoPostal = null);

    public sealed record ProductoAwDetalle(
        Guid Id,
        string ReferenciaExterna,
        string Descripcion,
        string UnidadMedida,
        Guid? UnidadMedidaId,
        Guid? CategoriaId,
        string? ClaveProdServSat,
        string? ClaveUnidadSat,
        string? ObjetoImp,
        decimal? TasaIvaTraslado,
        decimal? TasaRetencionIva,
        decimal? TasaRetencionIsr,
        string? FraccionArancelaria,
        string? UnidadAduana,
        decimal? PesoUnitarioKg,
        OrigenMaster Origen,
        bool DatosFiscalesCompletos,
        EstatusCatalogo Estatus);

    public sealed record CrearProductoAwRequest(
        string ReferenciaExterna,
        string Descripcion,
        string UnidadMedida,
        Guid? UnidadMedidaId,
        Guid? CategoriaId,
        string? ClaveProdServSat,
        string? ClaveUnidadSat,
        string? ObjetoImp,
        decimal? TasaIvaTraslado,
        decimal? TasaRetencionIva,
        decimal? TasaRetencionIsr,
        string? FraccionArancelaria = null,
        string? UnidadAduana = null,
        decimal? PesoUnitarioKg = null);

    public sealed record ActualizarProductoAwRequest(
        string? Descripcion,
        string? UnidadMedida,
        Guid? UnidadMedidaId,
        Guid? CategoriaId,
        string? ClaveProdServSat,
        string? ClaveUnidadSat,
        string? ObjetoImp,
        decimal? TasaIvaTraslado,
        decimal? TasaRetencionIva,
        decimal? TasaRetencionIsr,
        bool? LimpiarCategoria,
        bool? LimpiarTasaIvaTraslado,
        bool? LimpiarTasaRetencionIva,
        bool? LimpiarTasaRetencionIsr,
        string? FraccionArancelaria = null,
        string? UnidadAduana = null,
        decimal? PesoUnitarioKg = null,
        bool? LimpiarFraccionArancelaria = null,
        bool? LimpiarUnidadAduana = null,
        bool? LimpiarPesoUnitarioKg = null);

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
}
