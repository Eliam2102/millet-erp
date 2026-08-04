using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Hubs;

/// <summary>
/// Hub SignalR del módulo Integraciones.Aw (PR C + PR #201). Notifica al
/// frontend cuando el <c>AwDropWorker</c> completa un drop con el outcome
/// final que A+W reportó (success → correlated, failed → rechazado,
/// stuck → drop terminal).
///
/// <para>
/// <b>Ruta:</b> <c>/hubs/integraciones-aw</c> (mapeada en <c>Program.cs</c>).
/// </para>
///
/// <para>
/// <b>Auth:</b> requiere <see cref="PermisosCanonicos.IntegracionesAwCotizacionesConsultar"/>.
/// Tanto humanos como service principals (Glass Agent) que tengan ese permiso
/// pueden suscribirse. La policy se construye dinámicamente vía
/// <see cref="PermissionPolicyProvider"/>.
/// </para>
///
/// <para>
/// <b>Aislamiento por empresa:</b> cada conexión se agrega al grupo
/// <c>aw-empresa:{id}</c> derivado del claim <c>current_empresa_id</c>
/// del JWT. Los broadcasts de los workers se hacen por grupo — un usuario
/// de la empresa A nunca recibe eventos de la B (segregación incluso en
/// fase 1 single-tenant). Si el JWT no trae <c>current_empresa_id</c>, la
/// conexión se aborta — sin empresa el grupo es ambiguo.
/// </para>
///
/// <para>
/// <b>NO implementa:</b> SoftLocks, presence, custom client→server methods.
/// El hub es server→client unidireccional para esta fase.
/// </para>
///
/// <para>
/// Eventos emitidos al cliente:
/// <list type="bullet">
///   <item><c>CotizacionEstadoActualizado</c></item>
///   <item><c>CorrelacionExitosa</c></item>
///   <item><c>CorrelacionExpirada</c></item>
///   <item><c>DropFallido</c></item>
///   <item><c>DropDeadLetter</c></item>
/// </list>
/// </para>
/// </summary>
[Authorize(Policy = PermissionPolicyProvider.Prefix +
    PermisosCanonicos.IntegracionesAwCotizacionesConsultar)]
public sealed class IntegracionesAwHub : Hub
{
    /// <summary>Prefijo del nombre de grupo por empresa.</summary>
    public const string EmpresaGroupPrefix = "aw-empresa:";

    /// <summary>Construye el nombre de grupo para una empresa dada.</summary>
    public static string GroupForEmpresa(Guid empresaId) => EmpresaGroupPrefix + empresaId;

    public override async Task OnConnectedAsync()
    {
        if (!TryGetEmpresaId(out var empresaId))
        {
            // Conexión sin current_empresa_id → no podemos saber a qué grupo
            // suscribir. El frontend debe seleccionar empresa antes de
            // conectar al hub.
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupForEmpresa(empresaId));
        await base.OnConnectedAsync();
    }

    private bool TryGetEmpresaId(out Guid empresaId)
    {
        var raw = Context.User?.FindFirst(MilletClaimTypes.CurrentEmpresaId)?.Value;
        return Guid.TryParse(raw, out empresaId);
    }
}

// ─────── Payloads de los eventos server→client ───────
//
// Forma estable para que el frontend no tenga que mapear shapes diferentes.
// Los workers usan estos records al hacer SendAsync(...).

/// <summary>
/// Drop al on-prem completó (o falló). El front actualiza la card de la
/// cotización en la bandeja.
/// </summary>
public sealed record CotizacionEstadoActualizadoPayload(
    Guid AggregateId,
    string QuoteReference,
    string Estado,
    DateTimeOffset OcurridoEn);

/// <summary>
/// Correlación exitosa con A+W (encontró el pedido espejo).
/// </summary>
public sealed record CorrelacionExitosaPayload(
    Guid AggregateId,
    string QuoteReference,
    long AwDocId,
    DateTimeOffset CorrelatedAt);

/// <summary>
/// Pasó el timeout sin match. Requiere atención manual.
/// </summary>
public sealed record CorrelacionExpiradaPayload(
    Guid AggregateId,
    string QuoteReference,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ExpiredAt);

/// <summary>
/// Drop falló transient (Service Bus reintentará).
/// </summary>
public sealed record DropFallidoPayload(
    Guid AggregateId,
    string QuoteReference,
    string Error,
    string ErrorKind,
    int RetryCount);

/// <summary>
/// Drop falló permanent (dead-letter, requiere atención manual).
/// </summary>
public sealed record DropDeadLetterPayload(
    Guid AggregateId,
    string QuoteReference,
    string Error,
    string ErrorKind);

/// <summary>
/// El PDF de A+W (oferta/pedido) quedó disponible. El front muestra el
/// link de descarga (endpoint proxy <c>GET /cotizaciones/{id}/pdf</c>).
/// </summary>
public sealed record DocumentoAdjuntadoPayload(
    Guid AggregateId,
    string QuoteReference,
    long AwDocId,
    string DocType,
    string PdfFilename,
    DateTimeOffset PdfUploadedAt);
