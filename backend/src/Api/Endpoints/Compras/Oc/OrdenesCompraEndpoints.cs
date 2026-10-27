using Millet.Compras.Application.Oc.Envios;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Auth;
using Millet.Api.Endpoints.Adjuntos;
using Millet.Api.Web;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.Compras.Application.Oc.ActualizarCabecera;
using Millet.Compras.Application.Oc.ActualizarContactoProveedor;
using Millet.Compras.Application.Oc.ActualizarInformacionImportacion;
using Millet.Compras.Application.Oc.ActualizarInformacionLogistica;
using Millet.Compras.Application.Oc.ActualizarNumeroPedimento;
using Millet.Compras.Application.Oc.ActualizarReferenciaProveedor;
using Millet.Compras.Application.Oc.Adjuntos.AdjuntarDocumento;
using Millet.Compras.Application.Oc.Adjuntos.RemoverAdjunto;
using Millet.Compras.Application.Autorizar;
using Millet.Compras.Application.Oc.Autorizar;
using Millet.Compras.Application.Oc.Cancelar;
using Millet.Compras.Application.Oc.CancelarConRecepciones;
using Millet.Compras.Application.Oc.CerrarManual;
using Millet.Compras.Application.Oc.CrearOrdenCompraDesdeRequisicion;
using Millet.Compras.Application.Oc.CrearOrdenCompraVacia;
using Millet.Compras.Application.Oc.DuplicarOrdenCompra;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraOrigen;
using Millet.Compras.Application.Oc.EnviarAAutorizacion;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;
using Millet.Compras.Application.Oc.ListarPartidasAbiertas;
using Millet.Compras.Application.Oc.ListarPendientesAutorizacion;
using Millet.Compras.Application.Oc.ListarRequisicionesDisponibles;
using Millet.Compras.Application.Oc.Rechazar;
using Millet.Compras.Application.Oc.Incidencias;
using Millet.Compras.Application.Oc.RespuestaProveedor;
using Millet.Compras.Application.Oc.Lineas.ActualizarLinea;
using Millet.Compras.Application.Oc.Lineas.ActualizarTextoAdicional;
using Millet.Compras.Application.Oc.Lineas.AgregarLineaDesdeRequisicion;
using Millet.Compras.Application.Oc.Lineas.AgregarLineaManual;
using Millet.Compras.Application.Oc.Lineas.EliminarLinea;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Api.Endpoints.Compras.Oc;

/// <summary>
/// Endpoints HTTP del submódulo Órdenes de Compra (F1-PR2).
/// <list type="bullet">
///   <item><c>POST /api/v1/compras/ordenes</c>: crea OC vacía en Borrador.</item>
///   <item><c>GET /api/v1/compras/ordenes/{id}</c>: detalle por id con ETag.</item>
/// </list>
///
/// Nota arquitectónica: el chequeo del permiso adicional
/// <c>compras.ordenes.crear-sin-rq</c> (FOC11) se hace aquí en el endpoint
/// y no en el handler porque <c>Compras.Application</c> no debe depender
/// de <c>Identidad</c>; Api es el host integrador y sí tiene acceso a
/// ambos (mismo patrón que <c>seleccionar-requisitante</c> en RQ).
/// </summary>
public static class OrdenesCompraEndpoints
{
    public static IEndpointRouteBuilder MapOrdenesCompraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras/ordenes").WithTags("Compras");

        group.MapPost("/", async (
            [FromBody] CrearOrdenCompraVaciaCommand command,
            IMediator mediator,
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            Millet.Administracion.Application.Abstractions.IUsuarioSucursalReadPort scopeSucursales,
            Millet.SharedKernel.Application.ICurrentUserPermissions scopePermisos,
            CancellationToken cancellationToken) =>
        {
            await Millet.Administracion.Application.Abstractions.SucursalScopeGuard.VerificarAsync(currentUser.UserId,
                "compras.ordenes.gestionar-todas-sucursales", scopePermisos,
                (uid, c) => scopeSucursales.EstaAsociadoAsync(uid, command.SucursalDestinoId, c), cancellationToken);
            // FOC11: una OC con SinRequisicionPrevia=true requiere el
            // permiso adicional `compras.ordenes.crear-sin-rq`. La policy
            // base `compras.ordenes.crear` se aplica via .RequireAuthorization.
            if (command.SinRequisicionPrevia)
            {
                if (currentUser.UserId is not Guid userId)
                {
                    return Results.Unauthorized();
                }
                if (currentEmpresa.Current is not Guid empresaId)
                {
                    throw new ForbiddenException(
                        "EMPRESA_NO_SELECCIONADA",
                        "El usuario no tiene una empresa seleccionada en el JWT actual.");
                }

                var permisos = await permissionCache.GetAsync(userId, empresaId, cancellationToken);
                if (permisos is null)
                {
                    permisos = await permissionLoader.LoadForUserInEmpresaAsync(userId, empresaId, cancellationToken);
                    await permissionCache.SetAsync(userId, empresaId, permisos, cancellationToken);
                }

                if (!permisos.Contains(PermisosCanonicos.ComprasOrdenesCrearSinRq))
                {
                    throw new ForbiddenException(
                        "CREAR_SIN_RQ_DENEGADO",
                        "El usuario no tiene permiso para crear órdenes de compra sin requisición previa.");
                }
            }

            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/compras/ordenes/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("CrearOrdenCompraVacia")
        .WithSummary("Crear orden de compra vacía en Borrador")
        .WithDescription(
            "Crea una nueva OC en estado Borrador sin líneas ni adjuntos. " +
            "Requiere permiso `compras.ordenes.crear`. Si `sinRequisicionPrevia` " +
            "es `true` (FOC11), exige adicionalmente " +
            "`compras.ordenes.crear-sin-rq` y un `motivoSinRequisicion` no " +
            "vacío. Folio se genera atómicamente con formato " +
            "`OC-{sucursalCodigo}{folioAnio}-{secuencial:6}`. Header " +
            "`Idempotency-Key` (UUID v4) obligatorio (ADR-0020). Devuelve " +
            "201 con la URI del recurso en `Location`.")
        .Produces<CrearOrdenCompraVaciaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F6-PR2: duplicar OC Cancelada o Rechazada (C4) ---
        group.MapPost("/{id:guid}/duplicar", async (
            Guid id,
            [FromBody] DuplicarOcRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            var response = await mediator.Send(
                new DuplicarOrdenCompraCommand(
                    OrdenCompraOrigenId: id,
                    SucursalCodigo: body.SucursalCodigo,
                    FolioAnio: body.FolioAnio,
                    FechaDocumento: body.FechaDocumento),
                cancellationToken);
            return Results.Created($"/api/v1/compras/ordenes/{response.OrdenCompraNuevaId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("DuplicarOrdenCompra")
        .WithSummary("Duplicar OC Cancelada o Rechazada como Borrador (F6-PR2, C4)")
        .WithDescription(
            "Crea una OC nueva en Borrador heredando cabecera y líneas del " +
            "origen como manuales (sin FK a RQ — el comprador re-selecciona). " +
            "Solo permitido si la OC origen está Cancelada o Rechazada. " +
            "NO copia: adjuntos, autorizaciones, sub-estados, motivos de " +
            "rechazo/cancelación. Setea `OcOrigenId` para trazabilidad. " +
            "Header `Idempotency-Key` obligatorio.")
        .Produces<DuplicarOrdenCompraResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F6-PR2: navegar a OC origen ---
        group.MapGet("/{id:guid}/origen", async (
            Guid id,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            var origen = await mediator.Send(new ObtenerOrdenCompraOrigenQuery(id), cancellationToken);
            return origen is null ? Results.NoContent() : Results.Ok(origen);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerOrdenCompraOrigen")
        .WithSummary("Obtener OC origen (si esta OC fue duplicada)")
        .WithDescription(
            "Si esta OC nació de un `POST /duplicar`, devuelve su origen " +
            "(id + folio). Si no, devuelve 204 No Content.")
        .Produces<ObtenerOrdenCompraOrigenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ComprasDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            IMediator mediator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(
                id, db, currentUser, permisos, usuarioSucursales, cancellationToken);

            var response = await mediator.Send(new ObtenerOrdenCompraPorIdQuery(id), cancellationToken);
            // ETag con Version (cuidado §2.4 [P1]). El cliente devuelve
            // este valor en If-Match al hacer mutaciones futuras.
            httpContext.Response.Headers.ETag = $"\"{response.Version}\"";
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerOrdenCompraPorId")
        .WithSummary("Obtener orden de compra por id")
        .WithDescription(
            "Devuelve cabecera completa de la OC. Header `ETag` se setea " +
            "con `Version` para concurrencia optimista — el cliente lo " +
            "manda en `If-Match` al mutar (ADR-0012). 404 si el id no " +
            "existe o no es accesible para la empresa actual (evita " +
            "enumeración cross-tenant).")
        .Produces<OrdenCompraResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/envios", async (
            Guid id, IMediator mediator, ComprasDbContext db, ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, db, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            return Results.Ok(await mediator.Send(new ObtenerEnviosOcQuery(id), cancellationToken));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerEnviosOrdenCompra")
        .WithSummary("Consultar historial y disponibilidad del envío de una orden al proveedor")
        .Produces<EnviosOcResponse>();

        group.MapPost("/{id:guid}/envios", async (
            Guid id, [FromBody] RegistrarEnvioOcRequest body, IMediator mediator,
            ComprasDbContext db, ICurrentUserContext scopeUser, ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales, CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, db, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            var envio = await mediator.Send(new RegistrarEnvioOcCommand(id, body.Destinatario, body.Modo), cancellationToken);
            return Results.Ok(EnvioOcResponse.From(envio));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("RegistrarEnvioOrdenCompra")
        .WithSummary("Enviar el PDF existente al proveedor o registrar un envío manual")
        .Produces<EnvioOcResponse>()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F6-PR1: descargar PDF de la OC ---
        group.MapGet("/{id:guid}/pdf", async (
            Guid id,
            bool? vistaPrevia,
            HttpResponse response,
            IMediator mediator,
            ComprasDbContext db,
            Millet.Compras.Domain.Ports.Blob.IAlmacenarBlobPort blobPort,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, db, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            var pdf = await db.OrdenCompraPdfs
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.OrdenCompraId == id, cancellationToken)
                ?? throw new EntityNotFoundException(
                    "OC_PDF_NO_ENCONTRADO",
                    $"La OC '{id}' aún no tiene PDF generado. Se genera al autorizar N2.");

            var stream = await blobPort.ObtenerStreamAsync(pdf.BlobUrl, cancellationToken);
            try
            {
                if (vistaPrevia != true)
                {
                    var numero = await mediator.Send(
                        new Millet.Compras.Application.Oc.RegistrarImpresion.RegistrarImpresionOcCommand(id, pdf.Id), cancellationToken);
                    response.Headers["X-Oc-Impresion"] = numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                response.Headers.CacheControl = "no-store";
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
            return Results.File(
                fileStream: stream,
                contentType: pdf.ContentType,
                fileDownloadName: $"OC-{id:N}.pdf");
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerOrdenCompraPdf")
        .WithSummary("Consultar o descargar PDF de orden de compra")
        .WithDescription(
            "Devuelve el PDF guardado. Cada descarga registra una impresión y devuelve " +
            "X-Oc-Impresion: 1 es original; 2 y posteriores son copias. " +
            "Con vistaPrevia=true no registra impresión. 404 si aún no existe PDF.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- F2-PR2: editar cabecera (PATCH parcial) ---
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarCabeceraOcRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarCabeceraOcCommand(
                    OrdenCompraId: id,
                    ProveedorId: body.ProveedorId,
                    CondicionesPagoId: body.CondicionesPagoId,
                    UsoPrincipalId: body.UsoPrincipalId,
                    EncargadoComprasId: body.EncargadoComprasId,
                    Moneda: body.Moneda,
                    TipoCambio: body.TipoCambio,
                    EsImportacion: body.EsImportacion,
                    CotizacionExcepcionada: body.CotizacionExcepcionada,
                    Observaciones: body.Observaciones,
                    FechaEntregaEsperada: body.FechaEntregaEsperada,
                    DescuentoGlobalTipo: body.DescuentoGlobalTipo,
                    DescuentoGlobalValor: body.DescuentoGlobalValor,
                    GastosAdicionales: body.GastosAdicionales,
                    Redondeo: body.Redondeo,
                    LimpiarObservaciones: body.LimpiarObservaciones ?? false,
                    LimpiarFechaEntregaEsperada: body.LimpiarFechaEntregaEsperada ?? false,
                    LimpiarDescuentoGlobal: body.LimpiarDescuentoGlobal ?? false,
                    LimpiarTipoCambio: body.LimpiarTipoCambio ?? false,
                    UsoCfdiClave: body.UsoCfdiClave,
                    MetodoPago: body.MetodoPago,
                    FormaPagoClave: body.FormaPagoClave),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("ActualizarCabeceraOc")
        .WithSummary("Editar cabecera de OC en Borrador/Rechazada (PATCH parcial)")
        .WithDescription(
            "PATCH parcial sobre los campos editables de cabecera. " +
            "Inmutables (sucursalId, compradorTitularId, folio, " +
            "sinRequisicionPrevia, ocOrigenId): requieren Cancelar + " +
            "Duplicar (C4). Para limpiar un nullable a null, mandar el " +
            "flag `limpiarX = true`. Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR2: agregar línea manual ---
        group.MapPost("/{id:guid}/lineas", async (
            Guid id,
            [FromBody] AgregarLineaManualOcRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            var response = await mediator.Send(
                new AgregarLineaManualOcCommand(
                    OrdenCompraId: id,
                    ArticuloId: body.ArticuloId,
                    Cantidad: body.Cantidad,
                    UnidadMedida: body.UnidadMedida,
                    PrecioUnitario: body.PrecioUnitario,
                    DepartamentoSolicitanteId: body.DepartamentoSolicitanteId,
                    DescuentoTipo: body.DescuentoTipo,
                    DescuentoValor: body.DescuentoValor,
                    IndicadorImpuestos: body.IndicadorImpuestos,
                    DescripcionExtendida: body.DescripcionExtendida,
                    FechaEntregaLinea: body.FechaEntregaLinea,
                    CentroCostoId: body.CentroCostoId,
                    TextoAdicional: body.TextoAdicional, EsActivoFijo: body.EsActivoFijo, ClaseActivoId: body.ClaseActivoId),
                cancellationToken);
            return Results.Created($"/api/v1/compras/ordenes/{id}/lineas/{response.LineaId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("AgregarLineaManualOc")
        .WithSummary("Agregar línea manual a OC (sin FK a RQ)")
        .WithDescription(
            "Agrega una línea manual a una OC en Borrador o Rechazada. " +
            "Requiere `sinRequisicionPrevia = true` en la cabecera (§4.3 " +
            "del mapa funcional). El motor v0 calcula IVA 16% automáticamente; " +
            "el cliente recibe el subtotal e IVA recalculados en la respuesta. " +
            "Header `Idempotency-Key` obligatorio.")
        .Produces<AgregarLineaManualOcResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR2: actualizar línea (PATCH parcial) ---
        group.MapPatch("/{id:guid}/lineas/{lineaId:guid}", async (
            Guid id,
            Guid lineaId,
            [FromBody] ActualizarLineaOcRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarLineaOcCommand(
                    OrdenCompraId: id,
                    LineaId: lineaId,
                    ArticuloId: body.ArticuloId,
                    Cantidad: body.Cantidad,
                    UnidadMedida: body.UnidadMedida,
                    PrecioUnitario: body.PrecioUnitario,
                    DescuentoTipo: body.DescuentoTipo,
                    DescuentoValor: body.DescuentoValor,
                    IndicadorImpuestos: body.IndicadorImpuestos,
                    DepartamentoSolicitanteId: body.DepartamentoSolicitanteId,
                    DescripcionExtendida: body.DescripcionExtendida,
                    FechaEntregaLinea: body.FechaEntregaLinea,
                    CentroCostoId: body.CentroCostoId,
                    LimpiarDescripcionExtendida: body.LimpiarDescripcionExtendida ?? false,
                    LimpiarFechaEntregaLinea: body.LimpiarFechaEntregaLinea ?? false,
                    FraccionArancelaria: body.FraccionArancelaria,
                    LimpiarFraccionArancelaria: body.LimpiarFraccionArancelaria ?? false,
                    EsActivoFijo: body.EsActivoFijo,
                    ClaseActivoId: body.ClaseActivoId),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("ActualizarLineaOc")
        .WithSummary("Editar línea de OC (PATCH parcial)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR2: eliminar línea ---
        group.MapDelete("/{id:guid}/lineas/{lineaId:guid}", async (
            Guid id,
            Guid lineaId,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(new EliminarLineaOcCommand(id, lineaId), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("EliminarLineaOc")
        .WithSummary("Eliminar línea de OC en Borrador/Rechazada")
        .WithDescription(
            "Elimina una línea. Solo en Borrador/Rechazada y si la línea " +
            "no tiene recepción ni facturación. No requiere " +
            "`Idempotency-Key` — la operación no tiene impacto fiscal.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR2: actualizar texto_adicional de una línea (excepción §4.2) ---
        group.MapPatch("/{id:guid}/lineas/{lineaId:guid}/texto-adicional", async (
            Guid id,
            Guid lineaId,
            [FromBody] ActualizarTextoAdicionalRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarTextoAdicionalOcCommand(id, lineaId, body.TextoAdicional),
                cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("ActualizarTextoAdicionalLineaOc")
        .WithSummary("Actualizar texto adicional de una línea (cualquier estado no terminal)")
        .WithDescription(
            "Permite editar el `texto_adicional` aunque la línea ya tenga " +
            "recepción o facturación (§4.2 — los campos estructurales se " +
            "bloquean post-recepción pero el texto sigue editable). No " +
            "transiciona estado. No requiere `Idempotency-Key`.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR3: referencia proveedor ---
        group.MapPatch("/{id:guid}/referencia-proveedor", async (
            Guid id,
            [FromBody] ReferenciaProveedorRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarReferenciaProveedorCommand(id, body.ReferenciaProveedor),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("ActualizarReferenciaProveedorOc")
        .WithSummary("Actualizar folio externo del proveedor (§4.4)")
        .WithDescription("Editable en Borrador/Rechazada. Normaliza a uppercase + trim. " +
            "Enviar `null` o cadena vacía limpia el campo.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR3: contacto proveedor ---
        group.MapPatch("/{id:guid}/contacto-proveedor", async (
            Guid id,
            [FromBody] ContactoProveedorRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarContactoProveedorCommand(id, body.Nombre, body.Email, body.Telefono),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("ActualizarContactoProveedorOc")
        .WithSummary("Actualizar snapshot del contacto del proveedor (§4.5)")
        .WithDescription("Editable en Borrador/Rechazada. Enviar los 3 campos en `null` limpia el snapshot.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR3: información logística ---
        group.MapPatch("/{id:guid}/informacion-logistica", async (
            Guid id,
            [FromBody] InformacionLogisticaRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarInformacionLogisticaCommand(
                    id,
                    body.DireccionEntrega,
                    body.TransportistaId,
                    body.TransportistaTexto,
                    body.NumeroGuia,
                    body.InstruccionesEnvio),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLogistica)
        .WithName("ActualizarInformacionLogisticaOc")
        .WithSummary("Actualizar información logística (§4.6)")
        .WithDescription("Editable en Borrador, Rechazada y Autorizada (transportista, " +
            "guía y contenedor cambian durante el ciclo de recepción sin re-autorización). " +
            "Permiso `compras.ordenes.logistica` permite editar post-autorización.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR3: información importación ---
        group.MapPatch("/{id:guid}/informacion-importacion", async (
            Guid id,
            [FromBody] InformacionImportacionRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarInformacionImportacionCommand(
                    id,
                    body.IncotermId,
                    body.PaisOrigen,
                    body.NumeroContenedor,
                    body.CodigoRuta,
                    body.SemanaEmbarque,
                    body.NumeroPedimento,
                    TipoOperacion: body.TipoOperacion,
                    NumeroBl: body.NumeroBl,
                    TipoTransporte: body.TipoTransporte,
                    AgenteAduanalNaviera: body.AgenteAduanalNaviera,
                    PuertoAduanaEntrada: body.PuertoAduanaEntrada,
                    FechaEstimadaArribo: body.FechaEstimadaArribo,
                    CertificadoOrigen: body.CertificadoOrigen ?? false),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("ActualizarInformacionImportacionOc")
        .WithSummary("Actualizar información de importación (§4.7)")
        .WithDescription("Editable en Borrador/Rechazada. Campo `numeroPedimento` se IGNORA aquí " +
            "(usar `PATCH .../numero-pedimento` que es editable post-autorización).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F3-PR1: transmitir (Borrador/Rechazada → EnAutorizacionJefeCompras) ---
        group.MapPost("/{id:guid}/transmitir", async (
            Guid id,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(new EnviarAAutorizacionOcCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("TransmitirOrdenCompra")
        .WithSummary("Transmitir OC Borrador/Rechazada → EnAutorización (§7.1)")
        .WithDescription(
            "Aplica invariantes pre-auth (§7.1): ≥1 línea, proveedor activo " +
            "(C10), cotización adjunta o excepción + correo (C11), ficha " +
            "técnica si importación, motivo + correo si sin-rq. Transiciona " +
            "a EnAutorizacionJefeCompras. Header `Idempotency-Key` " +
            "obligatorio (transición fiscal-relevante).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- GAP-9: cierre manual (Autorizada → Cerrada) ---
        group.MapPost("/{id:guid}/cerrar-manual", async (
            Guid id,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(new CerrarManualOcCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCerrarManual)
        .WithName("CerrarManualOrdenCompra")
        .WithSummary("Cerrar OC manualmente (GAP-9: servicios/residuales)")
        .WithDescription(
            "Cierra la OC sin esperar el cierre automático de las 3 " +
            "dimensiones (recepción/facturación/pago). Válvula de escape " +
            "para OCs de servicios (no se reciben en Almacén) o con " +
            "residuales que el proveedor no surtirá. Solo desde " +
            "`Autorizada`. Emite el mismo `OrdenCompraCerradaEvent` que el " +
            "cierre automático. Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F3-PR1: autorizar (registrar firma N1 / N2) ---
        group.MapPost("/{id:guid}/autorizaciones", async (
            Guid id,
            [FromBody] AutorizarOcRequest body,
            IMediator mediator,
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            ComprasDbContext scopeDb,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is not Guid userId)
            {
                return Results.Unauthorized();
            }
            if (currentEmpresa.Current is not Guid empresaId)
            {
                throw new ForbiddenException(
                    "EMPRESA_NO_SELECCIONADA",
                    "El usuario no tiene una empresa seleccionada en el JWT actual.");
            }

            var permisoRequerido = body.Nivel switch
            {
                NivelAutorizacion.Nivel1 => PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
                NivelAutorizacion.Nivel2 => PermisosCanonicos.ComprasOrdenesAutorizarNivel2,
                _ => throw new BusinessRuleException(
                    "NIVEL_INVALIDO",
                    $"Nivel de autorización inválido: {body.Nivel}."),
            };

            var permisos = await permissionCache.GetAsync(userId, empresaId, cancellationToken);
            if (permisos is null)
            {
                permisos = await permissionLoader.LoadForUserInEmpresaAsync(userId, empresaId, cancellationToken);
                await permissionCache.SetAsync(userId, empresaId, permisos, cancellationToken);
            }

            if (!permisos.Contains(permisoRequerido))
            {
                throw new ForbiddenException(
                    "AUTORIZAR_NIVEL_DENEGADO",
                    $"El usuario no tiene permiso para autorizar OCs en {body.Nivel}.");
            }

            await OcSucursalScope.VerificarAsync(id, scopeDb, currentUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new AutorizarOrdenCompraCommand(id, body.Nivel, body.Notas),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization()
        .WithName("AutorizarOrdenCompra")
        .WithSummary("Registrar firma de autorización N1 o N2")
        .WithDescription(
            "Permiso validado dinámicamente según `Nivel`: " +
            "`compras.ordenes.autorizar-nivel1` o `autorizar-nivel2`. " +
            "N2 setea FechaContabilizacion y emite OrdenCompraAutorizadaEvent " +
            "(consumido por servicio PDF + Notificaciones en fases " +
            "siguientes). Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // S2 (OC): autorización masiva desde la bandeja de pendientes. Reutiliza
        // AutorizarOrdenCompraCommand por id (mismas validaciones y bifurcación N1/N2).
        // El permiso del nivel se valida una vez para el lote; el alcance por sucursal y
        // los errores de dominio se evalúan por id sin abortar el resto.
        group.MapPost("/autorizaciones-masivas", async Task<IResult> (
            [FromBody] AutorizarOcMasivasRequest request,
            IMediator mediator,
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            ComprasDbContext scopeDb,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is not Guid userId)
            {
                return Results.Unauthorized();
            }
            if (currentEmpresa.Current is not Guid empresaId)
            {
                throw new ForbiddenException(
                    "EMPRESA_NO_SELECCIONADA",
                    "El usuario no tiene una empresa seleccionada en el JWT actual.");
            }

            var permisoRequerido = request.Nivel switch
            {
                NivelAutorizacion.Nivel1 => PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
                NivelAutorizacion.Nivel2 => PermisosCanonicos.ComprasOrdenesAutorizarNivel2,
                _ => throw new BusinessRuleException(
                    "NIVEL_INVALIDO",
                    $"Nivel de autorización inválido: {request.Nivel}."),
            };

            var permisos = await permissionCache.GetAsync(userId, empresaId, cancellationToken);
            if (permisos is null)
            {
                permisos = await permissionLoader.LoadForUserInEmpresaAsync(userId, empresaId, cancellationToken);
                await permissionCache.SetAsync(userId, empresaId, permisos, cancellationToken);
            }

            if (!permisos.Contains(permisoRequerido))
            {
                throw new ForbiddenException(
                    "AUTORIZAR_NIVEL_DENEGADO",
                    $"El usuario no tiene permiso para autorizar OCs en {request.Nivel}.");
            }

            if (request.Ids is null || request.Ids.Count is < 1 or > 50)
            {
                return Results.Problem(
                    title: "ids inválidos",
                    detail: "ids debe contener entre 1 y 50 órdenes de compra.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var resultados = new List<AutorizacionMasivaItemResultado>(request.Ids.Count);
            foreach (var id in request.Ids.Distinct())
            {
                try
                {
                    await OcSucursalScope.VerificarAsync(id, scopeDb, currentUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
                    await mediator.Send(
                        new AutorizarOrdenCompraCommand(id, request.Nivel, request.Notas),
                        cancellationToken);
                    resultados.Add(new AutorizacionMasivaItemResultado(id, true, null, null));
                }
                catch (DomainException ex)
                {
                    // Un fallo puede dejar cambios sin guardar en el DbContext compartido del
                    // request: se descartan para no colarlos en el siguiente id del lote.
                    scopeDb.ChangeTracker.Clear();
                    resultados.Add(new AutorizacionMasivaItemResultado(id, false, ex.Code, ex.Message));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Fallo no previsto: mismo descarte. El detalle va solo al log; al cliente
                    // se devuelve un mensaje genérico.
                    scopeDb.ChangeTracker.Clear();
                    loggerFactory.CreateLogger("OrdenesCompraEndpoints")
                        .LogError(ex, "Error inesperado en autorización masiva de la OC {OrdenCompraId}", id);
                    resultados.Add(new AutorizacionMasivaItemResultado(
                        id,
                        false,
                        "ERROR_INESPERADO",
                        "Ocurrió un error inesperado al autorizar la orden de compra. Intente de nuevo."));
                }
            }

            return Results.Ok(new AutorizacionMasivaResponse(resultados));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization()
        .WithName("AutorizarOrdenesCompraMasivas")
        .WithSummary("Autorización masiva de órdenes de compra")
        .WithDescription(
            "Registra la firma del nivel indicado para hasta 50 OCs. Cada id se procesa con las " +
            "mismas reglas que `POST /{id}/autorizaciones` y devuelve su resultado (`ok`, " +
            "`errorCodigo`, `mensaje`) con HTTP 200; un id fallido no aborta el lote. Requiere el " +
            "permiso del nivel (`compras.ordenes.autorizar-nivel1` o `nivel2`). Header " +
            "`Idempotency-Key` obligatorio.")
        .Produces<AutorizacionMasivaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F4-PR2: crear OC desde RQ (flujo §4.1) ---
        group.MapPost("/desde-requisicion", async (
            [FromBody] CrearOrdenCompraDesdeRequisicionCommand command,
            IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser, ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await RqSucursalScope.VerificarAsync(command.RequisicionId, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/compras/ordenes/{response.OrdenCompraId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("CrearOrdenCompraDesdeRequisicion")
        .WithSummary("Crear OC desde una RQ Autorizada (flujo §4.1)")
        .WithDescription(
            "Crea OC en Borrador con cabecera pre-llenada desde la RQ + " +
            "líneas heredadas con FK a (requisicion_id, linea_requisicion_id). " +
            "Marca la RQ como comprometida en la misma TX.")
        .Produces<CrearOrdenCompraDesdeRequisicionResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F4-PR2: agregar líneas desde RQ a OC en Borrador (§4.2) ---
        group.MapPost("/{id:guid}/lineas/desde-requisicion", async (
            Guid id,
            [FromBody] AgregarLineaDesdeRequisicionRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await RqSucursalScope.VerificarAsync(body.RequisicionId, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            var response = await mediator.Send(
                new AgregarLineaDesdeRequisicionCommand(id, body.RequisicionId),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("AgregarLineaDesdeRequisicionOc")
        .WithSummary("Agregar líneas de una RQ Autorizada a OC existente (consolidación §4.2)")
        .WithDescription(
            "RQ debe estar Autorizada + no comprometida + misma sucursal " +
            "que la OC (§10.5). Hereda todas las líneas con FK. Política " +
            "§3.bis.4: líneas con mismo articulo_id de RQs distintas NO " +
            "se suman.")
        .Produces<AgregarLineaDesdeRequisicionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F6-PR3: bandeja principal de OCs ---
        group.MapGet("/", async (
            IMediator mediator,
            [FromQuery] EstadoOrdenCompra? estado,
            [FromQuery] SubEstadoRecepcion? subEstadoRecepcion,
            [FromQuery] bool? soloConPendienteRecepcion,
            [FromQuery] SubEstadoFacturacion? subEstadoFacturacion,
            [FromQuery] SubEstadoPago? subEstadoPago,
            [FromQuery] EstadoComercialProveedorOc? estadoComercialProveedor,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? compradorTitularId,
            [FromQuery] DateOnly? fechaDesde,
            [FromQuery] DateOnly? fechaHasta,
            [FromQuery] string? referenciaProveedor,
            [FromQuery] string? q,
            // Defaults explícitos (= 0) hacen page/pageSize OPCIONALES en
            // la binding de minimal API. Sin el default, ASP.NET Core trata
            // el parámetro como required y truena con BadHttpRequestException
            // (HTTP 500) si la URL no los trae — bug detectado al wirear
            // <c>OrdenesCompraLayout</c> del frontend que llama al endpoint
            // sin params cuando la pantalla arranca sin search activo.
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 0,
            CancellationToken cancellationToken = default) =>
        {
            var response = await mediator.Send(
                new ListarOrdenesCompraQuery(
                    Estado: estado,
                    SubEstadoRecepcion: subEstadoRecepcion,
                    SoloConPendienteRecepcion: soloConPendienteRecepcion,
                    SubEstadoFacturacion: subEstadoFacturacion,
                    SubEstadoPago: subEstadoPago,
                    EstadoComercialProveedor: estadoComercialProveedor,
                    ProveedorId: proveedorId,
                    CompradorTitularId: compradorTitularId,
                    FechaDocumentoDesde: fechaDesde,
                    FechaDocumentoHasta: fechaHasta,
                    ReferenciaProveedor: referenciaProveedor,
                    Q: q,
                    Page: page == 0 ? 1 : page,
                    PageSize: pageSize == 0 ? 50 : pageSize),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ListarOrdenesCompra")
        .WithSummary("Bandeja principal de OCs con filtros y paginación (F6-PR3)")
        .WithDescription(
            "Filtros opcionales: estado, sub-estados, proveedor, comprador, " +
            "rango de fechas, referencia proveedor, `q` (folio o referencia, contiene, " +
            "sin distinguir mayúsculas). `soloConPendienteRecepcion=true` " +
            "lista solo OCs con recepción != Completa (las que aún aceptan recepción). " +
            "Paginación offset-based " +
            "(`page`, `pageSize`; default 50, max 200). Orden default: " +
            "FechaDocumento DESC, Folio DESC.")
        .Produces<ListarOrdenesCompraResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F6-PR3: bandeja de pendientes de autorización ---
        group.MapGet("/pendientes-autorizacion", async (
            IMediator mediator,
            [FromQuery] NivelAutorizacion? nivel,
            [FromQuery] bool soloAccionables = false,
            // Defaults explícitos (= 0): mismo motivo que la bandeja general
            // — sin esto el endpoint truena con 500 si la URL no trae los
            // params. Ver comentario en MapGet("/").
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 0,
            CancellationToken cancellationToken = default) =>
        {
            var response = await mediator.Send(
                new ListarPendientesAutorizacionOcQuery(
                    Nivel: nivel,
                    Page: page == 0 ? 1 : page,
                    PageSize: pageSize == 0 ? 50 : pageSize,
                    SoloAccionables: soloAccionables),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ListarPendientesAutorizacionOc")
        .WithSummary("Bandeja de OCs pendientes de autorización (F6-PR3)")
        .WithDescription(
            "Devuelve OCs en EnAutorizacionJefeCompras o EnAutorizacionDireccion. " +
            "Filtrar por `nivel` (Nivel1 = Jefe Compras, Nivel2 = Dirección) " +
            "restringe al inbox específico. Orden FIFO (más viejas arriba).")
        .Produces<ListarOrdenesCompraResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F7-PR1: reporte de partidas abiertas ---
        group.MapGet("/partidas-abiertas", async (
            IMediator mediator,
            [FromQuery] EstadoOrdenCompra? estado,
            [FromQuery] SubEstadoRecepcion? subEstadoRecepcion,
            [FromQuery] SubEstadoFacturacion? subEstadoFacturacion,
            [FromQuery] SubEstadoPago? subEstadoPago,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? compradorTitularId,
            [FromQuery] DateOnly? fechaDesde,
            [FromQuery] DateOnly? fechaHasta,
            [FromQuery] string? numeroContenedor,
            [FromQuery] string? codigoRuta,
            [FromQuery] string? semanaEmbarque,
            [FromQuery] int? diasAtrasadosMinimos,
            [FromQuery] bool? conIncidenciasAbiertas,
            // Defaults explícitos (= 0): mismo motivo que la bandeja general
            // — sin esto el endpoint truena con 500 si la URL no trae los
            // params. Ver comentario en MapGet("/").
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 0,
            CancellationToken cancellationToken = default) =>
        {
            var response = await mediator.Send(
                new ListarPartidasAbiertasQuery(
                    Estado: estado,
                    SubEstadoRecepcion: subEstadoRecepcion,
                    SubEstadoFacturacion: subEstadoFacturacion,
                    SubEstadoPago: subEstadoPago,
                    ProveedorId: proveedorId,
                    CompradorTitularId: compradorTitularId,
                    FechaDocumentoDesde: fechaDesde,
                    FechaDocumentoHasta: fechaHasta,
                    NumeroContenedor: numeroContenedor,
                    CodigoRuta: codigoRuta,
                    SemanaEmbarque: semanaEmbarque,
                    DiasAtrasadosMinimos: diasAtrasadosMinimos,
                    ConIncidenciasAbiertas: conIncidenciasAbiertas,
                    Page: page == 0 ? 1 : page,
                    PageSize: pageSize == 0 ? 50 : pageSize),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesReportesPartidasAbiertas)
        .WithName("ListarPartidasAbiertasOc")
        .WithSummary("Reporte de partidas abiertas (F7-PR1)")
        .WithDescription(
            "Devuelve OCs no terminales con al menos una dimensión sub-estado " +
            "abierta. Filtros: estado, sub-estados, proveedor, comprador, " +
            "rango de fechas documento, contenedor, ruta, semana embarque, " +
            "días atrasados (contra `fechaEntregaEsperada`). Orden por " +
            "`fechaEntregaEsperada` (NULLS LAST → fecha documento) + folio. " +
            "Indexado por `ix_oc_partidas_abiertas`.")
        .Produces<ListarPartidasAbiertasResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F1-COM-07: historial de incidencias del proveedor (base de COM-13) ---
        group.MapGet("/incidencias-proveedor", async (
            IMediator mediator,
            [FromQuery] Guid? proveedorId,
            [FromQuery] bool? soloAbiertas,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 0,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await mediator.Send(new ListarIncidenciasProveedorQuery(proveedorId, soloAbiertas,
                page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), cancellationToken)))
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesReportesPartidasAbiertas)
        .WithName("ListarIncidenciasProveedorOc")
        .WithSummary("Historial de incidencias del proveedor con cumplimiento de compromisos (F1-COM-07)")
        .Produces<ListarIncidenciasProveedorResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F7-PR3: KPIs de partidas abiertas ---
        group.MapGet("/partidas-abiertas/kpis", async (
            IMediator mediator,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? compradorTitularId,
            [FromQuery] DateOnly? fechaDesde,
            [FromQuery] DateOnly? fechaHasta,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new Millet.Compras.Application.Oc.ObtenerKpisPartidasAbiertas.ObtenerKpisPartidasAbiertasQuery(
                    proveedorId, compradorTitularId, fechaDesde, fechaHasta),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesReportesPartidasAbiertas)
        .WithName("ObtenerKpisPartidasAbiertas")
        .WithSummary("KPIs agregados de partidas abiertas (F7-PR3)")
        .WithDescription(
            "Devuelve 5 contadores agregados sobre las mismas OCs que el " +
            "reporte de partidas abiertas: total, atrasadas, con recepción " +
            "parcial, con facturación parcial, sin pago. Filtros: proveedor, " +
            "comprador, rango fechas (mismo conjunto que F7-PR1).")
        .Produces<Millet.Compras.Application.Oc.ObtenerKpisPartidasAbiertas.KpisPartidasAbiertasResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F7-PR3: historial cronológico de una OC ---
        group.MapGet("/{id:guid}/historico", async (
            Guid id,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            var response = await mediator.Send(
                new Millet.Compras.Application.Oc.ObtenerHistorico.ObtenerHistoricoOrdenCompraQuery(id),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerHistoricoOrdenCompra")
        .WithSummary("Historial cronológico de una OC desde audit_log (F7-PR3)")
        .WithDescription(
            "Lee `core.audit_log` filtrado por `aggregate_root_id = ocId` " +
            "y devuelve timeline ASC con operación, entidad afectada " +
            "(OC raíz o sus hijas), usuario y resumen de cambios.")
        .Produces<Millet.Compras.Application.Oc.ObtenerHistorico.ObtenerHistoricoOrdenCompraResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F7-PR3: OCs hermanas duplicadas desde un origen común ---
        group.MapGet("/{id:guid}/duplicadas", async (
            Guid id,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            var response = await mediator.Send(
                new Millet.Compras.Application.Oc.ListarHermanasDuplicadas.ListarHermanasDuplicadasQuery(id),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ListarOcsHermanasDuplicadas")
        .WithSummary("OCs hermanas duplicadas desde el origen común (F7-PR3)")
        .WithDescription(
            "Dado un id de OC, devuelve todas las OCs que fueron duplicadas " +
            "desde la misma (filtro `oc_origen_id = id`). Útil para detectar " +
            "duplicaciones por comprador.")
        .Produces<Millet.Compras.Application.Oc.ListarHermanasDuplicadas.ListarHermanasDuplicadasResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F4-PR1: selector de RQs disponibles para consolidar ---
        group.MapGet("/requisiciones-disponibles", async (
            [FromQuery] Guid sucursalId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (sucursalId == Guid.Empty)
            {
                throw new BusinessRuleException(
                    "SUCURSAL_REQUERIDA",
                    "El parámetro `sucursalId` es obligatorio (§10.5: una OC consolida RQs de una sola sucursal).");
            }
            var response = await mediator.Send(
                new ListarRequisicionesDisponiblesQuery(sucursalId),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("ListarRequisicionesDisponiblesParaOc")
        .WithSummary("Listar RQs disponibles para consolidar en una OC (§4.2)")
        .WithDescription(
            "Devuelve las RQs en estado EnSurtido que no están " +
            "comprometidas en ninguna OC activa y tienen al menos una línea " +
            "con saldo de compra (`faltantePorComprar` y `lineasConSaldo`), " +
            "filtradas por `sucursalId` (restricción §10.5 cerrado del mapa funcional). " +
            "Alimenta el selector multiselección del módulo OC para crear " +
            "OC consolidada N:1. Requiere permiso `compras.ordenes.crear`.")
        .Produces<IReadOnlyList<RequisicionDisponibleResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- F3-PR3: cancelar (no terminal + SinRecepcion → Cancelada) ---
        group.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            [FromBody] CancelarOcRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new CancelarOrdenCompraCommand(id, body.MotivoCancelacionId, body.MotivoCancelacionTexto),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCancelar)
        .WithName("CancelarOrdenCompra")
        .WithSummary("Cancelar OC sin recepciones (F3-PR3)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F5-PR4: cancelar con recepciones parciales (doble autorización) ---
        group.MapPost("/{id:guid}/cancelar-con-recepciones", async (
            Guid id,
            [FromBody] CancelarOcRequest body,
            IMediator mediator,
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            ComprasDbContext scopeDb,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is not Guid userId)
            {
                return Results.Unauthorized();
            }
            if (currentEmpresa.Current is not Guid empresaId)
            {
                throw new ForbiddenException(
                    "EMPRESA_NO_SELECCIONADA",
                    "El usuario no tiene una empresa seleccionada en el JWT actual.");
            }

            // P2: primera firma, jefe de Compras.
            var permisos = await permissionCache.GetAsync(userId, empresaId, cancellationToken);
            if (permisos is null)
            {
                permisos = await permissionLoader.LoadForUserInEmpresaAsync(userId, empresaId, cancellationToken);
                await permissionCache.SetAsync(userId, empresaId, permisos, cancellationToken);
            }

            string[] requeridos =
            [
                PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
            ];
            var faltantes = requeridos.Where(p => !permisos.Contains(p)).ToList();
            if (faltantes.Count > 0)
            {
                throw new ForbiddenException(
                    "OC_CANCELAR_DOBLE_DENEGADO",
                    "Para solicitar la cancelación necesitas permiso de autorización de primer nivel de Compras.");
            }

            await OcSucursalScope.VerificarAsync(id, scopeDb, currentUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new CancelarConRecepcionesCommand(id, body.MotivoCancelacionId, body.MotivoCancelacionTexto),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization()
        .WithName("CancelarOrdenCompraConRecepciones")
        .WithSummary("Solicitar cancelación de OC con recepciones (firma N1)")
        .WithDescription("Registra la primera firma y bloquea recepción y facturación hasta que Dirección confirme o rechace. Requiere motivo e Idempotency-Key.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/resolver-cancelacion", async (
            Guid id, [FromBody] ResolverCancelacionOcRequest body, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(new ResolverCancelacionOcCommand(id, body.Confirmar, body.Motivo), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesAutorizarNivel2)
        .WithName("ResolverCancelacionOrdenCompra")
        .WithSummary("Confirmar o rechazar cancelación de OC (firma de Dirección)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F1-COM-11: respuesta del proveedor (confirmación o rechazo) ---
        group.MapPost("/{id:guid}/respuestas-proveedor", async (
            Guid id, HttpRequest request, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            if (!request.HasFormContentType)
                throw new BusinessRuleException("OC_RESPUESTA_EVIDENCIA_REQUERIDA", "Adjunta el archivo de evidencia de la respuesta del proveedor.");
            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (InvalidDataException)
            {
                // Multipart vacío o mal formado: se trata igual que la falta de evidencia.
                throw new BusinessRuleException("OC_RESPUESTA_EVIDENCIA_REQUERIDA", "Adjunta el archivo de evidencia de la respuesta del proveedor.");
            }
            var archivo = form.Files.GetFile("archivo");
            if (archivo is null || archivo.Length == 0)
                throw new BusinessRuleException("OC_RESPUESTA_EVIDENCIA_REQUERIDA", "Adjunta el archivo de evidencia de la respuesta del proveedor.");
            RegistrarRespuestaProveedorRequest body;
            try
            {
                body = System.Text.Json.JsonSerializer.Deserialize<RegistrarRespuestaProveedorRequest>(
                    form["datos"].FirstOrDefault() ?? "{}", System.Text.Json.JsonSerializerOptions.Web)
                    ?? throw new System.Text.Json.JsonException();
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.Problem(statusCode: 400, title: "Los datos de la respuesta no tienen un formato válido.");
            }
            await using var stream = archivo.OpenReadStream();
            var response = await mediator.Send(new RegistrarRespuestaProveedorCommand(id, body.Tipo, body.FechaRespuesta,
                body.Evidencia, body.ContactoProveedor, body.Motivo, body.Compromisos ?? [], stream, archivo.FileName,
                archivo.ContentType), cancellationToken);
            return Results.Created($"/api/v1/compras/ordenes/{id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesRegistrarRespuestaProveedor)
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithName("RegistrarRespuestaProveedorOrdenCompra")
        .WithSummary("Registrar la confirmación o el rechazo del proveedor (F1-COM-11)")
        .WithDescription("Multipart con archivo obligatorio y datos JSON opcionales (tipo, fechaRespuesta, evidencia como nota, contactoProveedor, motivo, compromisos). Solo en OC autorizada. No modifica cantidades, recepción, facturación, pago ni el estado de la OC; una nueva fecha prometida es otra respuesta y conserva la anterior. Header `Idempotency-Key` obligatorio.")
        .Produces<RespuestaProveedorOcResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F1-COM-11: evidencia por respuesta del proveedor (servicio genérico G1.2, ADR-0058).
        group.MapGroup("/respuestas-proveedor/{id:guid}").MapAdjuntos("oc_respuesta_proveedor",
            PermisosCanonicos.ComprasOrdenesLeer, PermisosCanonicos.ComprasOrdenesRegistrarRespuestaProveedor,
            PermisosCanonicos.ComprasOrdenesRegistrarRespuestaProveedor);

        // --- F1-COM-07: incidencias del proveedor por línea (faltante, dañado, atraso) ---
        group.MapPost("/{id:guid}/incidencias-proveedor", async (
            Guid id, [FromBody] RegistrarIncidenciaProveedorRequest body, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            var response = await mediator.Send(new RegistrarIncidenciaProveedorCommand(id, body.LineaOrdenCompraId,
                body.Tipo, body.Cantidad, body.FechaCompromiso, body.ResponsableId, body.Descripcion), cancellationToken);
            return Results.Created($"/api/v1/compras/ordenes/{id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesRegistrarIncidenciaProveedor)
        .WithName("RegistrarIncidenciaProveedorOrdenCompra")
        .WithSummary("Registrar una incidencia del proveedor en una línea de la OC (F1-COM-07)")
        .WithDescription("Solo en OC autorizada. Faltante y atraso cubren piezas pendientes; dañado, piezas ya recibidas. No modifica cantidades, recepción, facturación ni pago. Header `Idempotency-Key` obligatorio.")
        .Produces<IncidenciaProveedorOcResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/incidencias-proveedor/{incidenciaId:guid}/reprogramar", async (
            Guid id, Guid incidenciaId, [FromBody] ReprogramarIncidenciaProveedorRequest body, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            return Results.Ok(await mediator.Send(new ReprogramarIncidenciaProveedorCommand(id, incidenciaId,
                body.FechaCompromiso, body.Motivo), cancellationToken));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesRegistrarIncidenciaProveedor)
        .WithName("ReprogramarIncidenciaProveedorOrdenCompra")
        .WithSummary("Nueva fecha compromiso de una incidencia abierta; conserva las anteriores (F1-COM-07)")
        .Produces<IncidenciaProveedorOcResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/incidencias-proveedor/{incidenciaId:guid}/cerrar", async (
            Guid id, Guid incidenciaId, [FromBody] CerrarIncidenciaProveedorRequest body, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            return Results.Ok(await mediator.Send(new CerrarIncidenciaProveedorCommand(id, incidenciaId, body.Motivo),
                cancellationToken));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesRegistrarIncidenciaProveedor)
        .WithName("CerrarIncidenciaProveedorOrdenCompra")
        .WithSummary("Cerrar a mano una incidencia abierta con motivo (F1-COM-07)")
        .Produces<IncidenciaProveedorOcResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F3-PR2: rechazar (EnAutorizacion N1/N2 → Rechazada) ---
        group.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            [FromBody] RechazarOcRequest body,
            IMediator mediator,
            ComprasDbContext db,
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, db, currentUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            if (currentUser.UserId is not Guid userId)
            {
                return Results.Unauthorized();
            }
            if (currentEmpresa.Current is not Guid empresaId)
            {
                throw new ForbiddenException(
                    "EMPRESA_NO_SELECCIONADA",
                    "El usuario no tiene una empresa seleccionada en el JWT actual.");
            }

            // Permiso dinámico según el estado actual de la OC:
            // EnAutorizacionJefeCompras → autorizar-nivel1
            // EnAutorizacionDireccion   → autorizar-nivel2.
            var estadoActual = await db.OrdenesCompra
                .AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => (EstadoOrdenCompra?)o.Estado)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(
                    "ORDEN_COMPRA_NO_ENCONTRADA",
                    $"No se encontró orden de compra con id '{id}'.");

            var permisoRequerido = estadoActual switch
            {
                EstadoOrdenCompra.EnAutorizacionJefeCompras => PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
                EstadoOrdenCompra.EnAutorizacionDireccion => PermisosCanonicos.ComprasOrdenesAutorizarNivel2,
                _ => throw new BusinessRuleException(
                    "OC_RECHAZAR_ESTADO_INVALIDO",
                    $"Solo se puede rechazar en EnAutorización (estado actual: {estadoActual})."),
            };

            var permisos = await permissionCache.GetAsync(userId, empresaId, cancellationToken);
            if (permisos is null)
            {
                permisos = await permissionLoader.LoadForUserInEmpresaAsync(userId, empresaId, cancellationToken);
                await permissionCache.SetAsync(userId, empresaId, permisos, cancellationToken);
            }
            if (!permisos.Contains(permisoRequerido))
            {
                throw new ForbiddenException(
                    "RECHAZAR_NIVEL_DENEGADO",
                    $"El usuario no tiene permiso para rechazar OCs en el nivel actual ({estadoActual}).");
            }

            await mediator.Send(
                new RechazarOrdenCompraCommand(id, body.MotivoRechazoId, body.MotivoRechazoTexto, body.Notas),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization()
        .WithName("RechazarOrdenCompra")
        .WithSummary("Rechazar OC en flujo de autorización (§5.3)")
        .WithDescription(
            "Permiso validado dinámicamente según el estado actual: " +
            "rechazo desde EnAutorizacionJefeCompras requiere " +
            "`compras.ordenes.autorizar-nivel1`; rechazo desde " +
            "EnAutorizacionDireccion requiere `:nivel2`. El motivo debe " +
            "aplicar al flujo de OC (bitmask 8). Si el motivo tiene " +
            "`permiteTextoLibre = true`, el texto es obligatorio. La OC " +
            "transiciona a `Rechazada`; los campos pueden editarse de " +
            "nuevo y re-transmitirse. Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Visualizar/descargar el contenido de un adjunto SUBIDO ---
        // El blobUrl crudo (file:// en dev) no es navegable desde el
        // browser; este GET hace stream del contenido por el backend
        // (ADR-0024, mismo patrón que el PDF generado de la OC). El front
        // lo baja como Blob → object URL y decide preview inline o descarga.
        group.MapGet("/{id:guid}/adjuntos/{adjuntoId:guid}/contenido", async (
            Guid id,
            Guid adjuntoId,
            ComprasDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            IAlmacenarBlobPort blobPort,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(
                id, db, currentUser, permisos, usuarioSucursales, cancellationToken);

            var adjunto = await db.AdjuntosOc
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.Id == adjuntoId && a.OrdenCompraId == id,
                    cancellationToken)
                ?? throw new EntityNotFoundException(
                    "OC_ADJUNTO_NO_ENCONTRADO",
                    $"La OC '{id}' no tiene un adjunto con id '{adjuntoId}'.");

            var stream = await blobPort.ObtenerStreamAsync(adjunto.BlobUrl, cancellationToken);
            return Results.File(
                fileStream: stream,
                contentType: adjunto.ContentType,
                fileDownloadName: adjunto.NombreArchivo);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLeer)
        .WithName("ObtenerContenidoAdjuntoOc")
        .WithSummary("Visualizar/descargar el contenido de un adjunto de OC")
        .WithDescription(
            "Devuelve el binario del adjunto subido haciendo stream por el " +
            "backend (ObtenerStreamAsync), evitando exponer el blobUrl crudo " +
            "(file:// en dev) que el browser no puede navegar desde una " +
            "página http. El front lo consume como Blob → object URL para " +
            "preview inline y descarga. Permiso `compras.ordenes.leer` — el " +
            "mismo con el que se ve el detalle de la OC y sus adjuntos. " +
            "404 si el adjunto no existe o no pertenece a la OC.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- F2-PR4: adjuntar documento (multipart/form-data) ---
        group.MapPost("/{id:guid}/adjuntos", async (
            Guid id,
            [FromForm] Guid tipoDocumentoId,
            IFormFile archivo,
            ComprasDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            IOptions<AdjuntosPoliticaOptions> politicaOptions,
            IMediator mediator,
            IAlmacenarBlobPort blob,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            // 1. Autorización comprobada contra el documento padre
            // Conserva el bypass explícito de adjuntos; adjuntar exige además su propio permiso.
            await OcSucursalScope.VerificarAsync(
                id, db, currentUser, permisos, usuarioSucursales, cancellationToken);

            // 2. Validación de archivo (presencia, formato, MIME, tamaño y firma) ANTES de tocar el storage
            if (archivo is null || archivo.Length == 0)
            {
                throw new BusinessRuleException(
                    "ARCHIVO_REQUERIDO",
                    "Debe enviarse un archivo en el campo 'archivo' del multipart.");
            }

            var nombreArchivoLimpio = Path.GetFileName(archivo.FileName);
            var contentType = archivo.ContentType ?? "application/octet-stream";

            byte[] cabecera = new byte[8];
            int bytesLeidos;
            await using (var previewStream = archivo.OpenReadStream())
            {
                bytesLeidos = await previewStream.ReadAsync(cabecera.AsMemory(0, 8), cancellationToken);
            }

            politicaOptions.Value.ValidarInstancia(
                nombreArchivoLimpio,
                contentType,
                archivo.Length,
                cabecera.AsSpan(0, bytesLeidos));

            // 3. Subir blob físico
            var adjuntoId = Guid.CreateVersion7();
            string blobUrl;
            await using (var stream = archivo.OpenReadStream())
            {
                blobUrl = await blob.SubirAsync(
                    adjuntoId,
                    stream,
                    contentType,
                    nombreArchivoLimpio,
                    cancellationToken);
            }

            // 4. Persistir metadata en BD con compensación: si falla, se elimina el blob físico
            AdjuntarDocumentoResponse response;
            try
            {
                response = await mediator.Send(
                    new AdjuntarDocumentoCommand(
                        OrdenCompraId: id,
                        TipoDocumentoId: tipoDocumentoId,
                        NombreArchivo: nombreArchivoLimpio,
                        BlobUrl: blobUrl,
                        ContentType: contentType,
                        TamañoBytes: archivo.Length),
                    cancellationToken);
            }
            catch (Exception)
            {
                try
                {
                    await blob.EliminarAsync(blobUrl, CancellationToken.None);
                }
                catch (Exception elimEx)
                {
                    var logger = loggerFactory.CreateLogger("OrdenesCompraEndpoints");
                    logger.LogWarning(
                        elimEx,
                        "Fallo al compensar (eliminar) blob huérfano en {BlobUrl} para OC {OrdenCompraId}",
                        blobUrl,
                        id);
                }
                throw;
            }

            return Results.Created($"/api/v1/compras/ordenes/{id}/adjuntos/{response.AdjuntoId}", response);
        })
        .DisableAntiforgery() // Form upload sin antiforgery (API REST, JWT bearer).
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesAdjuntar)
        .WithName("AdjuntarDocumentoOc")
        .WithSummary("Adjuntar documento a una OC (multipart/form-data, §4.11)")
        .WithDescription(
            "Sube un archivo al blob storage (stub filesystem en dev) y " +
            "registra el adjunto en BD con metadata. Campos del " +
            "multipart: `tipoDocumentoId` (guid del catálogo) y `archivo` " +
            "(IFormFile). Permitido en cualquier estado no terminal. " +
            "Permiso `compras.ordenes.adjuntar` + `Idempotency-Key` " +
            "obligatorio.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<AdjuntarDocumentoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR4: remover adjunto (solo Borrador) ---
        group.MapDelete("/{id:guid}/adjuntos/{adjuntoId:guid}", async (
            Guid id,
            Guid adjuntoId,
            ComprasDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(
                id, db, currentUser, permisos, usuarioSucursales, cancellationToken);

            await mediator.Send(new RemoverAdjuntoCommand(id, adjuntoId), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrear)
        .WithName("RemoverAdjuntoOc")
        .WithSummary("Remover adjunto de OC en Borrador (§4.11)")
        .WithDescription(
            "Borra la fila + el blob físico. Solo permitido en Borrador. " +
            "Adjuntos post-transmisión quedan para auditoría. No requiere " +
            "`Idempotency-Key` — la operación tiene impacto fiscal cero " +
            "en Borrador.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- F2-PR3: número pedimento (editable post-autorización) ---
        group.MapPatch("/{id:guid}/numero-pedimento", async (
            Guid id,
            [FromBody] NumeroPedimentoRequest body,
            IMediator mediator,
            ComprasDbContext scopeDb,
            ICurrentUserContext scopeUser,
            ICurrentUserPermissions scopePermisos,
            IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await OcSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            await mediator.Send(
                new ActualizarNumeroPedimentoCommand(
                    id,
                    body.NumeroPedimento,
                    body.NumeroBl,
                    body.FechaEstimadaArribo,
                    body.AgenteAduanalNaviera),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesLogistica)
        .WithName("ActualizarNumeroPedimentoOc")
        .WithSummary("Actualizar NumeroPedimento (editable post-autorización, §4.7)")
        .WithDescription("Permitido en cualquier estado no terminal (incluye Autorizada). " +
            "El pedimento se captura cuando llega el material — requiere " +
            "`compras.ordenes.logistica`.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Body del POST /{id}/autorizaciones (F3-PR1).</summary>
    public sealed record AutorizarOcRequest(NivelAutorizacion Nivel, string? Notas);

    /// <summary>Body del POST /autorizaciones-masivas de OC: 1 a 50 ids.</summary>
    public sealed record AutorizarOcMasivasRequest(IReadOnlyList<Guid> Ids, NivelAutorizacion Nivel, string? Notas);

    /// <summary>Body del POST /{id}/rechazar (F3-PR2).</summary>
    public sealed record RechazarOcRequest(Guid MotivoRechazoId, string? MotivoRechazoTexto, string? Notas);

    /// <summary>Body del POST /{id}/cancelar (F3-PR3).</summary>
    public sealed record CancelarOcRequest(Guid MotivoCancelacionId, string? MotivoCancelacionTexto);

    /// <summary>Body del POST /{id}/duplicar (F6-PR2).</summary>
    public sealed record DuplicarOcRequest(
        string SucursalCodigo,
        short FolioAnio,
        DateOnly FechaDocumento);

    /// <summary>Body del POST /{id}/lineas/desde-requisicion (F4-PR2).</summary>
    public sealed record AgregarLineaDesdeRequisicionRequest(Guid RequisicionId);

    /// <summary>Body del PATCH /referencia-proveedor (F2-PR3).</summary>
    public sealed record ReferenciaProveedorRequest(string? ReferenciaProveedor);

    /// <summary>Body del PATCH /contacto-proveedor (F2-PR3).</summary>
    public sealed record ContactoProveedorRequest(string? Nombre, string? Email, string? Telefono);

    /// <summary>Body del PATCH /informacion-logistica (F2-PR3).</summary>
    public sealed record InformacionLogisticaRequest(
        string? DireccionEntrega,
        Guid? TransportistaId,
        string? TransportistaTexto,
        string? NumeroGuia,
        string? InstruccionesEnvio);

    /// <summary>Body del PATCH /informacion-importacion (F2-PR3).</summary>
    // PLATFORM-TODO(F1-COM-06): campos de comercio exterior, supuesto Vilo.
    public sealed record InformacionImportacionRequest(
        Guid? IncotermId,
        string? PaisOrigen,
        string? NumeroContenedor,
        string? CodigoRuta,
        string? SemanaEmbarque,
        string? NumeroPedimento,
        TipoOperacionComercioExterior? TipoOperacion = null,
        string? NumeroBl = null,
        TipoTransporte? TipoTransporte = null,
        string? AgenteAduanalNaviera = null,
        string? PuertoAduanaEntrada = null,
        DateOnly? FechaEstimadaArribo = null,
        bool? CertificadoOrigen = null);

    /// <summary>
    /// Body del PATCH /numero-pedimento (F2-PR3). Reemplaza los cuatro campos
    /// post-autorización: null limpia el valor.
    /// </summary>
    public sealed record NumeroPedimentoRequest(
        string? NumeroPedimento,
        string? NumeroBl = null,
        DateOnly? FechaEstimadaArribo = null,
        string? AgenteAduanalNaviera = null);

    /// <summary>Body del PATCH cabecera (F2-PR2). Todos los campos opcionales.</summary>
    public sealed record ActualizarCabeceraOcRequest(
        Guid? ProveedorId,
        Guid? CondicionesPagoId,
        Guid? UsoPrincipalId,
        Guid? EncargadoComprasId,
        string? Moneda,
        decimal? TipoCambio,
        bool? EsImportacion,
        bool? CotizacionExcepcionada,
        string? Observaciones,
        DateOnly? FechaEntregaEsperada,
        DescuentoTipo? DescuentoGlobalTipo,
        decimal? DescuentoGlobalValor,
        decimal? GastosAdicionales,
        decimal? Redondeo,
        bool? LimpiarObservaciones,
        bool? LimpiarFechaEntregaEsperada,
        bool? LimpiarDescuentoGlobal,
        bool? LimpiarTipoCambio,
        string? UsoCfdiClave = null,
        string? MetodoPago = null,
        string? FormaPagoClave = null);

    /// <summary>Body del POST lineas (F2-PR2).</summary>
    public sealed record AgregarLineaManualOcRequest(
        Guid ArticuloId,
        decimal Cantidad,
        string UnidadMedida,
        decimal PrecioUnitario,
        Guid DepartamentoSolicitanteId,
        DescuentoTipo? DescuentoTipo,
        decimal? DescuentoValor,
        string? IndicadorImpuestos,
        string? DescripcionExtendida,
        DateTimeOffset? FechaEntregaLinea,
        // Fase E PR3 (fix #701 API boundary): el CC-Máquina elegido en el
        // Dim3Picker viaja aquí; sin este campo System.Text.Json lo descartaba.
        Guid? CentroCostoId,
        string? TextoAdicional, bool EsActivoFijo = false, Guid? ClaseActivoId = null);

    /// <summary>Body del PATCH lineas (F2-PR2).</summary>
    public sealed record ActualizarLineaOcRequest(
        Guid? ArticuloId,
        decimal? Cantidad,
        string? UnidadMedida,
        decimal? PrecioUnitario,
        DescuentoTipo? DescuentoTipo,
        decimal? DescuentoValor,
        string? IndicadorImpuestos,
        Guid? DepartamentoSolicitanteId,
        string? DescripcionExtendida,
        DateTimeOffset? FechaEntregaLinea,
        // Fase E PR3 (fix #701 API boundary): editar el CC-Máquina de una línea
        // manual. En heredada el dominio lo rechaza (LINEA_OC_CC_HEREDADO_INMUTABLE).
        Guid? CentroCostoId,
        bool? LimpiarDescripcionExtendida,
        bool? LimpiarFechaEntregaLinea,
        // PLATFORM-TODO(F1-COM-06): supuesto Vilo. Solo OC de comercio exterior.
        string? FraccionArancelaria = null,
        bool? LimpiarFraccionArancelaria = null,
        bool? EsActivoFijo = null, Guid? ClaseActivoId = null);

    /// <summary>Body del PATCH texto-adicional (F2-PR2).</summary>
    public sealed record ActualizarTextoAdicionalRequest(string? TextoAdicional);
    public sealed record ResolverCancelacionOcRequest(bool Confirmar, string Motivo);
    public sealed record RegistrarRespuestaProveedorRequest(TipoRespuestaProveedorOc? Tipo, DateOnly? FechaRespuesta, string? Evidencia, string? ContactoProveedor, string? Motivo, IReadOnlyList<CompromisoEntregaInput>? Compromisos);
    public sealed record RegistrarIncidenciaProveedorRequest(Guid LineaOrdenCompraId, TipoIncidenciaProveedorOc Tipo, decimal Cantidad, DateOnly FechaCompromiso, Guid ResponsableId, string? Descripcion);
    public sealed record ReprogramarIncidenciaProveedorRequest(DateOnly FechaCompromiso, string Motivo);
    public sealed record CerrarIncidenciaProveedorRequest(string Motivo);
    public sealed record RegistrarEnvioOcRequest(string? Destinatario, string Modo);
}
