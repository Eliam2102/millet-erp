using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compras.Application.Autorizar;
using Millet.Compras.Application.Cancelar;
using Millet.Compras.Application.CerrarManual;
using Millet.Compras.Application.CrearRequisicion;
using Millet.Compras.Application.Eliminar;
using Millet.Compras.Application.EnviarAAutorizacion;
using Millet.Compras.Application.Listar;
using Millet.Compras.Application.Motivos;
using Millet.Compras.Application.ObtenerRequisicionPorId;
using Millet.Compras.Application.PreviewCubrimiento;
using Millet.Compras.Application.Rechazar;
using Millet.Compras.Domain;
using Millet.Identidad.Application;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Endpoints HTTP del módulo Compras / submódulo Requisiciones.
/// F1-PR2: Crear (POST) y Obtener por id (GET).
/// F2-PR3: Transmitir + Autorizaciones + catálogo motivos.
/// F2-PR4: Rechazar + Eliminar + bandejas (general y pendientes).
/// F4-PR3: Cancelar (libera reservas + aborta OC borrador).
///
/// Nota arquitectónica: el chequeo del permiso
/// <c>compras.requisiciones.seleccionar-requisitante</c> (delegación) se
/// hace aquí en el endpoint y no en el handler porque
/// <c>Compras.Application</c> no debe depender de <c>Identidad</c>; Api
/// es el host integrador y sí tiene acceso a ambos.
/// </summary>
public static class RequisicionesEndpoints
{
    public static IEndpointRouteBuilder MapRequisicionesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/compras/requisiciones").WithTags("Compras");

        group.MapPost("/", async (
            [FromBody] CrearRequisicionCommand command,
            IMediator mediator,
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            CancellationToken cancellationToken) =>
        {
            // Si el caller envió RequisitanteId distinto al current user,
            // exige el permiso de delegación. Mantiene la regla del
            // diseño §8.3 sin filtrar contra el JWT en el handler.
            if (command.RequisitanteId is Guid requested
                && currentUser.UserId is Guid userId
                && requested != userId)
            {
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

                if (!permisos.Contains(PermisosCanonicos.ComprasRequisicionesSeleccionarRequisitante))
                {
                    throw new ForbiddenException(
                        "SELECCIONAR_REQUISITANTE_DENEGADO",
                        "El usuario no tiene permiso para crear requisiciones a nombre de otro requisitante.");
                }
            }

            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/compras/requisiciones/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesCrear)
        .WithName("CrearRequisicion")
        .WithSummary("Crear requisición en Borrador")
        .WithDescription(
            "Crea una nueva requisición de compra en estado Borrador. " +
            "Requiere permiso `compras.requisiciones.crear`. Si `RequisitanteId` " +
            "difiere del usuario autenticado, exige adicionalmente " +
            "`compras.requisiciones.seleccionar-requisitante`. Header " +
            "`Idempotency-Key` (UUID v4) obligatorio (ADR-0020). " +
            "Devuelve 201 con la URI del recurso en `Location`.")
        .Produces<CrearRequisicionResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ObtenerRequisicionPorIdQuery(id), cancellationToken);
            // ETag con Version (cuidado §2.4 [P1]). El cliente devuelve
            // este valor en If-Match al hacer mutaciones futuras.
            httpContext.Response.Headers.ETag = $"\"{response.Version}\"";
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesLeer)
        .WithName("ObtenerRequisicionPorId")
        .WithSummary("Obtener requisición por id")
        .WithDescription(
            "Devuelve detalle completo (cabecera + líneas + autorizaciones + " +
            "estado actual). Header `ETag` se setea con `Version` para " +
            "concurrencia optimista — el cliente lo manda en `If-Match` al " +
            "mutar (ADR-0012).")
        .Produces<RequisicionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // PR-C: preview read-only del cubrimiento estimado (estimación de
        // stock por línea ANTES de autorizar). NO reserva ni persiste; reusa
        // el IConsultarStockPort que ya consume la bifurcación. Gated en
        // `leer` (lo tiene quien abre el detalle: autorizador y requisitante).
        group.MapGet("/{id:guid}/cubrimiento-estimado", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new PreviewCubrimientoQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesLeer)
        .WithName("ObtenerCubrimientoEstimado")
        .WithSummary("Preview del cubrimiento estimado de una RQ (PR-C)")
        .WithDescription(
            "Estimación read-only, por línea, de cuánto se cubriría de stock " +
            "de almacén vs. compra según el disponible actual — SIN reservar " +
            "ni persistir (las columnas de cubrimiento siguen en 0 hasta " +
            "autorizar). Solo aplica mientras la RQ está `EnAutorizacion`; en " +
            "otros estados devuelve `aplica=false` con lista vacía. Las " +
            "cantidades son estimación sujeta a disponibilidad: el stock es " +
            "móvil. Requiere `compras.requisiciones.leer`.")
        .Produces<PreviewCubrimientoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // B.4: PATCH cabecera (solo Borrador). Patch parcial: campos null
        // no se modifican; flags limpiarX vacían un nullable.
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] EditarCabeceraRequisicionRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new Millet.Compras.Application.EditarCabecera.EditarCabeceraRequisicionCommand(
                    RequisicionId: id,
                    Descripcion: body.Descripcion,
                    FechaEntregaDeseada: body.FechaEntregaDeseada,
                    Prioridad: body.Prioridad,
                    ProveedorSugeridoId: body.ProveedorSugeridoId,
                    Clasificacion: body.Clasificacion,
                    LimpiarDescripcion: body.LimpiarDescripcion ?? false,
                    LimpiarFechaEntregaDeseada: body.LimpiarFechaEntregaDeseada ?? false,
                    LimpiarProveedorSugeridoId: body.LimpiarProveedorSugeridoId ?? false),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesEditar)
        .WithName("EditarCabeceraRequisicion")
        .WithSummary("Editar cabecera de RQ en Borrador (PATCH parcial, B.4)")
        .WithDescription(
            "PATCH parcial sobre los campos editables de cabecera: " +
            "`descripcion`, `fechaEntregaDeseada`, `prioridad`, " +
            "`proveedorSugeridoId`, `clasificacion`. Solo en estado " +
            "Borrador → 422 `EDITAR_CABECERA_SOLO_EN_BORRADOR` si la RQ " +
            "ya transmitió. Inmutables (sucursalId, departamentoId, " +
            "almacenDestinoId, requisitanteId): requieren recrear la " +
            "RQ. Para limpiar un nullable a null, mandar el flag " +
            "`limpiarX = true` (sin él, null se interpreta como \"no " +
            "tocar\"). Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F2-PR3: transmitir (Borrador → EnAutorizacion).
        group.MapPost("/{id:guid}/transmitir", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(new EnviarAAutorizacionCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesEditar)
        .WithName("TransmitirRequisicion")
        .WithSummary("Transmitir Borrador → EnAutorización")
        .WithDescription(
            "Envía la requisición a la cadena de aprobación. Requiere estado " +
            "Borrador y al menos una línea (TRANSMITIR_SIN_LINEAS = 422). " +
            "Devuelve 204 No Content. Header `Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F2-PR3: autorizar (registrar firma de Nivel1 o Nivel2). El
        // permiso del nivel se valida en el endpoint para no contaminar
        // el handler con dependencias a Identidad.
        group.MapPost("/{id:guid}/autorizaciones", async (
            Guid id,
            [FromBody] AutorizarRequisicionRequest request,
            IMediator mediator,
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
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
                NivelAutorizacion.Nivel1 => PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
                NivelAutorizacion.Nivel2 => PermisosCanonicos.ComprasRequisicionesAutorizarNivel2,
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
                    $"El usuario no tiene permiso para autorizar en {request.Nivel}.");
            }

            await mediator.Send(
                new AutorizarRequisicionCommand(id, request.Nivel, request.Notas),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization()
        .WithName("AutorizarRequisicion")
        .WithSummary("Registrar autorización Nivel1 / Nivel2")
        .WithDescription(
            "Registra la firma del autorizador en el nivel indicado. El " +
            "permiso se valida en el endpoint según `Nivel`: " +
            "`compras.requisiciones.autorizar.nivel1` o `nivel2`. Si la matriz " +
            "queda satisfecha, dispara bifurcación stock-aware (reservas + " +
            "OC borrador) en una sola TX EF (F4-PR2). Header " +
            "`Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F2-PR3: catálogo motivos rechazo (root, no anidado en una RQ
        // específica). Lo lee la UI para poblar selectores en flujos de
        // rechazo / eliminación / cancelación.
        app.MapGet("/api/v1/compras/motivos-rechazo", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ListarMotivosRechazoQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesLeer)
        .WithTags("Compras")
        .WithName("ListarMotivosRechazo")
        .WithSummary("Listar catálogo de motivos de rechazo/eliminación/cancelación")
        .WithDescription(
            "Devuelve los motivos activos con bitmask `aplicaA` (Rechazo, " +
            "Eliminacion, Cancelacion) y flag `permiteTextoLibre`. Lo consume " +
            "la UI para poblar selectores en los flujos terminales.")
        .Produces<IReadOnlyList<MotivoRechazoResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // F2-PR4: rechazar (EnAutorizacion → Rechazada). El motivo y
        // texto van en el body. Se usa POST (no DELETE) para preservar
        // el cuerpo de manera estándar y porque la transición es a un
        // estado terminal específico, no un borrado físico.
        group.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            [FromBody] TerminarRequisicionRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new RechazarRequisicionCommand(id, request.MotivoId, request.MotivoTexto),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesRechazar)
        .WithName("RechazarRequisicion")
        .WithSummary("Rechazar EnAutorización → Rechazada")
        .WithDescription(
            "Marca la requisición como Rechazada con motivo (cross-table " +
            "validation contra catálogo) y texto libre opcional según " +
            "`permiteTextoLibre` del motivo. Header `Idempotency-Key` " +
            "obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F2-PR4: eliminar (Borrador o EnAutorizacion → Eliminada). POST
        // con body por las mismas razones que /rechazar; el flujo no
        // toca DeletedAt — solo cambia estado.
        group.MapPost("/{id:guid}/eliminar", async (
            Guid id,
            [FromBody] TerminarRequisicionRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new EliminarRequisicionCommand(id, request.MotivoId, request.MotivoTexto),
                cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesEliminar)
        .WithName("EliminarRequisicion")
        .WithSummary("Eliminar pre-autorización (Borrador o EnAutorización → Eliminada)")
        .WithDescription(
            "Cierre lógico antes de la matriz de autorización. NO requiere " +
            "header `Idempotency-Key` — la operación no tiene impacto fiscal " +
            "(la RQ nunca llegó a Autorizada). Cambia estado, no toca " +
            "DeletedAt (registros disponibles para auditoría).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F4-PR3: cancelar (Autorizada o EnSurtido → Cancelada). El
        // handler libera reservas en Almacén y aborta OC borrador
        // dentro de la misma TX EF. POST con body siguiendo el patrón
        // de rechazar/eliminar.
        group.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            [FromBody] TerminarRequisicionRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new CancelarRequisicionCommand(id, request.MotivoId, request.MotivoTexto),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesCancelar)
        .WithName("CancelarRequisicion")
        .WithSummary("Cancelar Autorizada/EnSurtido → Cancelada")
        .WithDescription(
            "Cancela una requisición ya autorizada. Libera reservas en Almacén " +
            "(idempotente vía port) y aborta OC borrador en la misma TX EF. " +
            "Si cualquier port falla, rollback completo y error 422 " +
            "`CANCELAR_FALLO`. Header `Idempotency-Key` obligatorio (impacto " +
            "fiscal: aborta cadena de OC).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ADR-0043: cierre manual del jefe de almacén / almacenista. POST con
        // body (motivo) siguiendo el patrón de cancelar.
        group.MapPost("/{id:guid}/cerrar-manual", async (
            Guid id,
            [FromBody] TerminarRequisicionRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new CerrarManualRequisicionCommand(id, request.MotivoId, request.MotivoTexto),
                cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesCerrarManual)
        .WithName("CerrarManualRequisicion")
        .WithSummary("Cierre manual Autorizada/EnSurtido → CerradaSinSurtir/CerradaSurtidaParcial")
        .WithDescription(
            "Cierre manual de una RQ que el requisitante ya no necesita (ADR-0043). " +
            "El estado terminal se deriva de lo entregado: nada → CerradaSinSurtir; " +
            "algo → CerradaSurtidaParcial. Libera las reservas del tramo de stock en " +
            "Almacén (idempotente) en la misma TX; el material recibido por compra " +
            "queda como stock libre y las OC en vuelo siguen su curso (el FE avisa). " +
            "Si un port falla, rollback completo y 422 `CIERRE_MANUAL_FALLO`. Header " +
            "`Idempotency-Key` obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F2-PR4: bandeja general con filtros opcionales por estado,
        // departamento y requisitante. Paginación offset-based.
        group.MapGet("/", async (
            [FromQuery] EstadoRequisicion? estado,
            [FromQuery] Guid? departamentoId,
            [FromQuery] Guid? requisitanteId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarRequisicionesQuery(
                    estado,
                    departamentoId,
                    requisitanteId,
                    offset ?? 0,
                    limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesLeer)
        .WithName("ListarRequisiciones")
        .WithSummary("Bandeja general con filtros opcionales")
        .WithDescription(
            "Lista paginada (offset-based, default `limit=50`) con filtros " +
            "opcionales por `estado`, `departamentoId`, `requisitanteId`. " +
            "Sin filtros, lista todas las RQs de la empresa actual ordenadas " +
            "por `fecha_solicitud DESC`. Filtrada siempre por `empresa_id` " +
            "del JWT (multi-tenant, ADR-0011).")
        .Produces<PagedResponse<RequisicionListItemResponse>>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // F2-PR4: bandeja específica de pendientes de autorización.
        // Vive bajo /api/v1/compras/... a nivel root para que la UI no
        // lo confunda con un sub-recurso de una RQ específica.
        app.MapGet("/api/v1/compras/pendientes-autorizacion", async Task<IResult> (
            [FromQuery] Guid? departamentoId,
            [FromQuery] short? nivelPendiente,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            // nivelPendiente (PR-A): 1 = falta N1, 2 = falta N2. Cualquier otro
            // valor es 400 (no lo degradamos silenciosamente a "sin filtro").
            NivelAutorizacion? nivel = null;
            if (nivelPendiente is short n)
            {
                if (n != (short)NivelAutorizacion.Nivel1
                    && n != (short)NivelAutorizacion.Nivel2)
                {
                    return Results.Problem(
                        title: "nivelPendiente inválido",
                        detail: "nivelPendiente debe ser 1 (N1) o 2 (N2).",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                nivel = (NivelAutorizacion)n;
            }

            var response = await mediator.Send(
                new ListarPendientesAutorizacionQuery(
                    DepartamentoId: departamentoId,
                    NivelPendiente: nivel,
                    Offset: offset ?? 0,
                    Limit: limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesLeer)
        .WithTags("Compras")
        .WithName("ListarPendientesAutorizacion")
        .WithSummary("Bandeja de pendientes de autorización (estado=EnAutorización)")
        .WithDescription(
            "Atajo a la bandeja general con `estado=EnAutorizacion` fijo. " +
            "Filtros opcionales por `departamentoId` para que un autorizador " +
            "vea solo las RQs de su área. Paginación offset-based.")
        .Produces<PagedResponse<RequisicionListItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // B.2: histórico de auditoría de una RQ. Lee core.audit_log filtrado
        // por aggregate_root_id (poblado por el AuditSaveChangesInterceptor
        // enriquecido).
        group.MapGet("/{id:guid}/historico", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new Millet.Compras.Application.Historico.ObtenerHistoricoQuery(id),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesLeer)
        .WithName("ObtenerHistoricoRequisicion")
        .WithSummary("Histórico de auditoría de la requisición")
        .WithDescription(
            "Trae todas las transiciones de la RQ (root + líneas + " +
            "autorizaciones) ordenadas por timestamp ascendente. Cada " +
            "entrada incluye `tipo` (enum legible: Creada, " +
            "LineaAgregada, Transmitida, AutorizadaN1/N2, Rechazada, " +
            "Cancelada, etc.) inferido de `(operacion, entidad, " +
            "cambios)`. El `cambios` raw también se devuelve para que el " +
            "FE muestre detalle expandible. Tope defensivo de 500 filas.")
        .Produces<IReadOnlyList<Millet.Compras.Application.Historico.HistoricoEntryResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Body del POST /autorizaciones.</summary>
    public sealed record AutorizarRequisicionRequest(NivelAutorizacion Nivel, string? Notas);

    /// <summary>Body común para POST /rechazar y POST /eliminar.</summary>
    public sealed record TerminarRequisicionRequest(Guid MotivoId, string? MotivoTexto);

    /// <summary>
    /// Body del PATCH /requisiciones/{id} (B.4). Todos los campos
    /// opcionales (PATCH parcial). Para limpiar un nullable a null
    /// usar el flag `limpiarX`.
    /// </summary>
    public sealed record EditarCabeceraRequisicionRequest(
        string? Descripcion,
        DateOnly? FechaEntregaDeseada,
        Prioridad? Prioridad,
        Guid? ProveedorSugeridoId,
        Clasificacion? Clasificacion,
        bool? LimpiarDescripcion,
        bool? LimpiarFechaEntregaDeseada,
        bool? LimpiarProveedorSugeridoId);
}
