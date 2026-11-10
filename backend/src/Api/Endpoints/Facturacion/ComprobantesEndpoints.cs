using MediatR;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Cancelaciones.AutorizarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.Queries;
using Millet.Facturacion.Application.Cancelaciones.RechazarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.RetirarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Timbrado.DescartarComprobante;
using Millet.Facturacion.Application.Timbrado.Queries;
using Millet.Facturacion.Application.Timbrado.Adopcion;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Application.Trazabilidad;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints transversales de comprobantes del módulo Facturación. F5-PR2:
/// cancelación SAT (solicitud + estatus); F6.5: autorización del Contador General,
/// rechazo, retiro y bandeja. <c>/api/v1/facturacion/comprobantes</c>.
/// </summary>
public static class ComprobantesEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionComprobantesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/comprobantes")
            .WithTags("Facturacion");

        // POST: solicita la cancelación SAT 4.0 de un comprobante timbrado.
        group.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            SolicitarCancelacionRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new SolicitarCancelacionCommand(id, body.MotivoSat, body.UuidSustituto), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCancelacionesSolicitar)
        .WithName("SolicitarCancelacion")
        .WithSummary("Solicita la cancelación SAT 4.0 de un comprobante")
        .Produces<SolicitarCancelacionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: estatus de la cancelación de un comprobante.
        group.MapGet("/{id:guid}/cancelar", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ConsultarCancelacionQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCancelacionesConsultar)
        .WithName("ConsultarCancelacion")
        .WithSummary("Consulta el estatus de cancelación de un comprobante")
        .Produces<ConsultarCancelacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // F6.5: el Contador General autoriza la cancelación pendiente; solo entonces se envía al PAC.
        group.MapPost("/{id:guid}/cancelar/autorizar", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new AutorizarCancelacionCommand(id), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCancelacionesAutorizar)
        .WithName("AutorizarCancelacion")
        .WithSummary("Autoriza una cancelación pendiente y la envía al PAC")
        .Produces<SolicitarCancelacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F6.5: el Contador General no autoriza la cancelación (con motivo); el comprobante sigue vigente.
        group.MapPost("/{id:guid}/cancelar/rechazar", async (
            Guid id,
            RechazarCancelacionRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new RechazarCancelacionCommand(id, body.Motivo), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCancelacionesAutorizar)
        .WithName("RechazarCancelacion")
        .WithSummary("Rechaza una cancelación pendiente con motivo")
        .Produces<SolicitarCancelacionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F6.5: Ingresos retira una cancelación que aún no se autoriza.
        group.MapPost("/{id:guid}/cancelar/retirar", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new RetirarCancelacionCommand(id), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCancelacionesSolicitar)
        .WithName("RetirarCancelacion")
        .WithSummary("Retira una cancelación pendiente de autorización")
        .Produces<SolicitarCancelacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // F6.5: bandeja del Contador General (por omisión, pendientes de autorización;
        // B3: todos=true trae todas las solicitudes, en cualquier estado).
        group.MapGet("/cancelaciones", async (
            EstadoSolicitudCancelacion? estado,
            bool? todos,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new BandejaCancelacionesQuery(estado, todos == true), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCancelacionesConsultar)
        .WithName("BandejaCancelaciones")
        .WithSummary("Solicitudes de cancelación por estado")
        .Produces<IReadOnlyList<CancelacionBandejaItem>>(StatusCodes.Status200OK);

        // POST: reintenta el timbrado de un comprobante en TimbradoFallido
        // (cualquier tipo — mismo folio interno, no re-emite).
        group.MapPost("/{id:guid}/reintentar-timbrado", async (
            Guid id,
            ReintentarTimbradoRequest? body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ReintentarTimbradoCommand(id, body?.ConfirmarNoDuplicado ?? false), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionComprobantesReintentarTimbrado)
        .WithName("ReintentarTimbrado")
        .WithSummary("Reintenta el timbrado de un comprobante en TimbradoFallido")
        .Produces<ReintentarTimbradoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST (M-284): adopta el CFDI que el PAC sí timbró (XML del portal) en un comprobante en
        // proceso o fallido sin respuesta. soloRevisar=true devuelve el cotejo ERP contra XML sin cambios.
        group.MapPost("/{id:guid}/adoptar-cfdi", async (
            Guid id,
            AdoptarCfdiRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new AdoptarCfdiTimbradoCommand(id, body.Xml, body.SoloRevisar), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionComprobantesAdoptarCfdi)
        .WithName("AdoptarCfdiTimbrado")
        .WithSummary("Adopta el CFDI que el PAC sí timbró en un comprobante sin respuesta")
        .Produces<AdoptarCfdiTimbradoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: descarta un comprobante en TimbradoFallido que no se
        // reintentará ([Decisión 01-G] G3) — terminal; libera el pedido.
        group.MapPost("/{id:guid}/descartar", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new DescartarComprobanteCommand(id), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionComprobantesDescartar)
        .WithName("DescartarComprobante")
        .WithSummary("Descarta un comprobante fallido (terminal; quema el folio y libera el pedido)")
        .Produces<DescartarComprobanteResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: historial de intentos de timbrado (bitácora, [Decisión 01-G] G4).
        group.MapGet("/{id:guid}/intentos-timbrado", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new IntentosTimbradoQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("IntentosTimbrado")
        .WithSummary("Historial de intentos de timbrado de un comprobante")
        .Produces<IReadOnlyList<IntentoTimbradoItem>>(StatusCodes.Status200OK);

        // GET: árbol de trazabilidad documento-céntrico (doc 13, 13-D). El
        // handler resuelve la familia del comprobante; mismo permiso que la
        // bitácora de intentos (precedente F13-PR1).
        group.MapGet("/{id:guid}/arbol-documentos", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ArbolDocumentosFacturacionQuery(
                    TipoNodoTrazabilidadFacturacion.FacturaVenta /* se re-resuelve por TPT */, id),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("ArbolDocumentosComprobante")
        .WithSummary("Árbol de trazabilidad de un comprobante (pedido/relaciones/NC/REPP)")
        .Produces<ArbolDocumentosFacturacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Body de la solicitud de cancelación.</summary>
    public sealed record SolicitarCancelacionRequest(string MotivoSat, string? UuidSustituto);

    /// <summary>Body del rechazo de una cancelación pendiente.</summary>
    public sealed record RechazarCancelacionRequest(string Motivo);

    /// <summary>
    /// Body del reintento de timbrado. <c>ConfirmarNoDuplicado</c> debe ir
    /// en <c>true</c> cuando el fallo fue por código ambiguo (PAC_TIMEOUT /
    /// PAC_SIN_RESPUESTA / PAC_RESPUESTA_INCOMPLETA): el operador verifica
    /// primero en el dashboard de FiscalAPI que no exista timbre.
    /// </summary>
    public sealed record ReintentarTimbradoRequest(bool ConfirmarNoDuplicado = false);

    /// <summary>M-284: XML del CFDI timbrado (texto) y si solo se coteja.</summary>
    public sealed record AdoptarCfdiRequest(string Xml, bool SoloRevisar = false);
}
