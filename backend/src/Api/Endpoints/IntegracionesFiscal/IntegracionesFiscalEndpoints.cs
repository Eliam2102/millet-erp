using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Api.Endpoints.Adjuntos;
using Millet.Api.Infrastructure.Adapters;
using Millet.Integraciones.Fiscal.Application.Certificados;
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
using Millet.SharedKernel.Application;

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
            ICurrentEmpresaContext currentEmpresa,
            IMediator mediator,
            CancellationToken ct) =>
        {
            EnsureCurrentEmpresa(currentEmpresa, empresaId);
            var response = await mediator.Send(
                new ObtenerConfiguracionPacQuery(empresaId, (ProveedorPac)proveedor), ct);
            // Sin configuración = 204 (no 404): «Mi empresa» la pide siempre y el 404 ensuciaba la consola.
            return response is null ? Results.NoContent() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalLeer)
        .WithName("ObtenerConfiguracionPac")
        .WithSummary("Obtener la configuración del PAC para una empresa + proveedor")
        .Produces<ConfiguracionPacResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        config.MapPost("/{empresaId:guid}/{proveedor:int}/test", async (
            Guid empresaId,
            int proveedor,
            [FromBody] TestConexionPacPayload? payload,
            ICurrentEmpresaContext currentEmpresa,
            IMediator mediator,
            CancellationToken ct) =>
        {
            EnsureCurrentEmpresa(currentEmpresa, empresaId);
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
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] GuardarConfiguracionPacPayload payload,
            ICurrentEmpresaContext currentEmpresa,
            IMediator mediator,
            CancellationToken ct) =>
        {
            EnsureCurrentEmpresa(currentEmpresa, empresaId);
            var command = new GuardarConfiguracionPacCommand(
                EmpresaId: empresaId,
                Proveedor: (ProveedorPac)proveedor,
                BaseUrl: payload.BaseUrl,
                ApiKey: payload.ApiKey,
                Activo: payload.Activo,
                VersionEsperada: expectedVersion,
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

        // === F6.9: certificados CSD (historial, revocación, estado y evidencia) ===

        // M-232: última prueba de conexión registrada (va aparte de la configuración; 204 si no hay).
        config.MapGet("/{empresaId:guid}/{proveedor:int}/pruebas/ultima", async (
            Guid empresaId,
            int proveedor,
            ICurrentEmpresaContext currentEmpresa,
            IMediator mediator,
            CancellationToken ct) =>
        {
            EnsureCurrentEmpresa(currentEmpresa, empresaId);
            var r = await mediator.Send(new Millet.Integraciones.Fiscal.Application.Configuracion.UltimaPruebaConexion.UltimaPruebaConexionPacQuery(
                empresaId, (ProveedorPac)proveedor), ct);
            return r is null ? Results.NoContent() : Results.Ok(r);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalLeer)
        .WithName("UltimaPruebaConexionPac")
        .WithSummary("Última prueba de conexión con el timbrador (fecha, resultado y mensaje)")
        .Produces<Millet.Integraciones.Fiscal.Application.Configuracion.UltimaPruebaConexion.UltimaPruebaConexionPacResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status204NoContent);

        config.MapGet("/{empresaId:guid}/{proveedor:int}/certificados", async (
            Guid empresaId,
            int proveedor,
            ICurrentEmpresaContext currentEmpresa,
            IMediator mediator,
            CancellationToken ct) =>
        {
            EnsureCurrentEmpresa(currentEmpresa, empresaId);
            return Results.Ok(await mediator.Send(new ListarCertificadosCsdQuery(empresaId), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalLeer)
        .WithName("ListarCertificadosCsd")
        .WithSummary("Historial de CSD de la empresa (alta, sustitución, revocación)")
        .Produces<IReadOnlyList<CertificadoCsdItem>>(StatusCodes.Status200OK);

        config.MapPost("/{empresaId:guid}/{proveedor:int}/csd/revocar", async (
            Guid empresaId,
            int proveedor,
            [FromBody] RevocarCsdPayload payload,
            ICurrentEmpresaContext currentEmpresa,
            IMediator mediator,
            CancellationToken ct) =>
        {
            EnsureCurrentEmpresa(currentEmpresa, empresaId);
            return Results.Ok(await mediator.Send(new RevocarCsdCommand(empresaId, (ProveedorPac)proveedor, payload.Motivo), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesFiscalAdministrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RevocarCsd")
        .WithSummary("Revoca el CSD en uso; el ERP deja de timbrar con él")
        .Produces<CertificadoCsdItem>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Estado del CSD de la empresa actual para el aviso en el ERP: sin datos
        // sensibles, lo consulta cualquier usuario autenticado (lo muestra el banner).
        app.MapGet("/api/v1/integraciones/fiscal/csd/estado", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new EstadoCsdEmpresaQuery(), ct)))
        .WithTags("Integraciones.Fiscal")
        .RequireAuthorization()
        .WithName("EstadoCsdEmpresa")
        .WithSummary("Estado y vencimiento del CSD de la empresa actual")
        .Produces<EstadoCsdEmpresaResponse>(StatusCodes.Status200OK);

        // Evidencia por certificado del historial (acuses del SAT).
        app.MapGroup("/api/v1/integraciones/fiscal/certificados/{id:guid}")
            .WithTags("Integraciones.Fiscal")
            .RequireAuthorization()
            .MapAdjuntos(CertificadoCsdAdjuntoPropietario.Entidad,
                PermisosCanonicos.IntegracionesFiscalLeer,
                PermisosCanonicos.IntegracionesFiscalAdministrar,
                PermisosCanonicos.IntegracionesFiscalAdministrar);

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

    private static void EnsureCurrentEmpresa(ICurrentEmpresaContext currentEmpresa, Guid empresaId)
    {
        var actual = currentEmpresa.Current
            ?? throw new SharedKernel.Application.Exceptions.ForbiddenException(
                "EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada en el JWT.");
        if (actual != empresaId)
            throw new SharedKernel.Application.Exceptions.CrossTenantViolationException(
                nameof(ConfiguracionPac), actual, empresaId);
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

    /// <summary>Body de la revocación del CSD (F6.9).</summary>
    public sealed record RevocarCsdPayload(string Motivo);
}
