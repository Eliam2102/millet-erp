using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Integraciones.Fiscal.Application.Configuracion;
using Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;
using Millet.Integraciones.Fiscal.Application.Configuracion.ObtenerConfiguracionPac;
using Millet.Integraciones.Fiscal.Application.Configuracion.TestConexionPac;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.ActualizarRfcReceptor;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.AgregarRfcReceptor;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.EliminarRfcReceptor;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.ListarRfcsReceptores;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.SubirFielReceptor;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Api.Endpoints.IntegracionesFiscal;

/// <summary>
/// Endpoints HTTP del módulo Integraciones.Fiscal (PR-4).
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/integraciones/fiscal/configuracion/{empresaId}/{proveedor}</c></item>
///   <item><c>PUT    /api/v1/integraciones/fiscal/configuracion/{empresaId}/{proveedor}</c></item>
///   <item><c>GET    /api/v1/integraciones/fiscal/rfcs-receptores?empresaId=...</c></item>
///   <item><c>POST   /api/v1/integraciones/fiscal/rfcs-receptores</c></item>
///   <item><c>PATCH  /api/v1/integraciones/fiscal/rfcs-receptores/{id}</c></item>
///   <item><c>DELETE /api/v1/integraciones/fiscal/rfcs-receptores/{id}</c></item>
/// </list>
///
/// <para>
/// <b>POST /configuracion/{empresaId}/test</b> se difiere a PR-5
/// (necesita <c>FiscalApiHttpClient</c> para hacer ping real al PAC).
/// </para>
/// </summary>
public static class IntegracionesFiscalEndpoints
{
    public static IEndpointRouteBuilder MapIntegracionesFiscalEndpoints(this IEndpointRouteBuilder app)
    {
        // === Configuración del PAC ===

        var config = app
            .MapGroup("/api/v1/integraciones/fiscal/configuracion")
            .WithTags("Integraciones.Fiscal")
            .RequireAuthorization();

        config.MapGet("/{empresaId:guid}/{proveedor:int}", async (
            Guid empresaId,
            int proveedor,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ObtenerConfiguracionPacQuery(empresaId, (ProveedorPac)proveedor), ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalLeer)
        .WithName("ObtenerConfiguracionPac")
        .WithSummary("Obtener la configuración del PAC para una empresa + proveedor")
        .Produces<ConfiguracionPacResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        config.MapPost("/{empresaId:guid}/{proveedor:int}/test", async (
            Guid empresaId,
            int proveedor,
            [FromBody] TestConexionPacPayload? payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new TestConexionPacCommand(
                    EmpresaId: empresaId,
                    Proveedor: (ProveedorPac)proveedor,
                    BaseUrl: payload?.BaseUrl,
                    ApiKey: payload?.ApiKey,
                    TimeoutSegundos: payload?.TimeoutSegundos),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalAdministrar)
        .WithName("TestConexionPac")
        .WithSummary("Probar conexión al PAC con credenciales transitorias o las persistidas")
        .Produces<TestConexionPacResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        config.MapPut("/{empresaId:guid}/{proveedor:int}", async (
            Guid empresaId,
            int proveedor,
            [FromBody] GuardarConfiguracionPacPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new GuardarConfiguracionPacCommand(
                EmpresaId: empresaId,
                Proveedor: (ProveedorPac)proveedor,
                BaseUrl: payload.BaseUrl,
                ApiKey: payload.ApiKey,
                Activo: payload.Activo,
                EmisorSandbox: payload.EmisorSandbox,
                ReceptorSandbox: payload.ReceptorSandbox,
                Csd: payload.Csd);

            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("GuardarConfiguracionPac")
        .WithSummary("Crear o actualizar la configuración del PAC (upsert idempotente)")
        .Produces<ConfiguracionPacResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // === RFCs receptores ===

        var rfcs = app
            .MapGroup("/api/v1/integraciones/fiscal/rfcs-receptores")
            .WithTags("Integraciones.Fiscal")
            .RequireAuthorization();

        rfcs.MapGet("/", async (
            [FromQuery] Guid empresaId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var list = await mediator.Send(new ListarRfcsReceptoresQuery(empresaId), ct);
            return Results.Ok(list);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalLeer)
        .WithName("ListarRfcsReceptores")
        .WithSummary("Listar los RFCs receptores configurados de una empresa")
        .Produces<IReadOnlyList<RfcReceptorResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        rfcs.MapPost("/", async (
            [FromBody] AgregarRfcReceptorCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/integraciones/fiscal/rfcs-receptores/{response.Id}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AgregarRfcReceptor")
        .WithSummary("Agregar un RFC receptor para una empresa")
        .Produces<RfcReceptorResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        rfcs.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarRfcReceptorPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarRfcReceptorCommand(id, payload.DescargaHabilitada, payload.RefreshHabilitada),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarRfcReceptor")
        .WithSummary("Toggle de los flags DescargaHabilitada / RefreshHabilitada")
        .Produces<RfcReceptorResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        rfcs.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new EliminarRfcReceptorCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("EliminarRfcReceptor")
        .WithSummary("Soft-delete de un RFC receptor")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // PR-10: subir FIEL (e.firma) del RFC receptor a FiscalAPI. Una
        // vez subida, la descarga masiva contra el SAT real funciona.
        // Body con bytes base64 — el cliente debe codificar los archivos
        // .cer y .key antes de mandar. Tamaño max validado en el handler.
        rfcs.MapPost("/{id:guid}/fiel", async (
            Guid id,
            [FromBody] SubirFielReceptorPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new SubirFielReceptorCommand(
                RfcReceptorId:    id,
                LegalName:        payload.LegalName,
                ZipCode:          payload.ZipCode,
                SatTaxRegimeCode: payload.SatTaxRegimeCode,
                Email:            payload.Email,
                CerBytes:         Convert.FromBase64String(payload.CerBase64),
                KeyBytes:         Convert.FromBase64String(payload.KeyBase64),
                Password:         payload.Password);
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("SubirFielReceptor")
        .WithSummary("Subir FIEL (e.firma) del RFC receptor a FiscalAPI")
        .Produces<RfcReceptorResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record GuardarConfiguracionPacPayload(
        string BaseUrl,
        string? ApiKey,
        bool Activo,
        IdentidadSandboxDto? EmisorSandbox = null,
        IdentidadSandboxDto? ReceptorSandbox = null,
        CsdDto? Csd = null);

    public sealed record ActualizarRfcReceptorPayload(
        bool DescargaHabilitada,
        bool RefreshHabilitada);

    /// <summary>
    /// Body de <c>POST /api/v1/integraciones/fiscal/rfcs-receptores/{id}/fiel</c>.
    /// Los archivos van en base64 dentro del JSON (no multipart) para
    /// consistencia con el resto del API + idempotency key + auth headers.
    /// </summary>
    public sealed record SubirFielReceptorPayload(
        string LegalName,
        string ZipCode,
        string SatTaxRegimeCode,
        string Email,
        string CerBase64,
        string KeyBase64,
        string Password);

    public sealed record TestConexionPacPayload(
        string? BaseUrl,
        string? ApiKey,
        int? TimeoutSegundos);
}
