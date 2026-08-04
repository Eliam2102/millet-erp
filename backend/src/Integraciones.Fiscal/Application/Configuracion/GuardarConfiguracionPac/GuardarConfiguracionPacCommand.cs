using MediatR;
using Millet.Integraciones.Fiscal.Application.Configuracion;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;

/// <summary>
/// Upsert idempotente de la configuración del PAC para una empresa
/// (PR-4 — §5 del 01-diseno):
/// <list type="bullet">
///   <item>Si no existe configuración para
///   <c>(empresaId, proveedor)</c> → la crea con <see cref="ApiKey"/>
///   cifrado.</item>
///   <item>Si existe y <see cref="ApiKey"/> es <c>null</c> → no rota
///   (solo actualiza políticas / schedule / BaseUrl).</item>
///   <item>Si existe y <see cref="ApiKey"/> coincide con el hash
///   actual → no-op respecto al secret (idempotente).</item>
///   <item>Si existe y <see cref="ApiKey"/> difiere → rota:
///   re-cifra y actualiza <c>UltimaRotacionAt</c>.</item>
/// </list>
///
/// <para>
/// El handler cifra el <see cref="ApiKey"/> y el <see cref="Csd"/> con
/// <c>FiscalSecretCipher</c> (ADR-0037) antes de persistir — los
/// plaintexts nunca llegan al dominio ni a la BD. <see cref="Csd"/>
/// sigue la misma semántica que el ApiKey: <c>null</c> = no tocar el
/// CSD persistido; con valor = capturar/rotar (idempotente por hash).
/// </para>
/// </summary>
public sealed record GuardarConfiguracionPacCommand(
    Guid EmpresaId,
    ProveedorPac Proveedor,
    string BaseUrl,
    string? ApiKey,
    bool Activo,
    IdentidadSandboxDto? EmisorSandbox = null,
    IdentidadSandboxDto? ReceptorSandbox = null,
    CsdDto? Csd = null) : IRequest<ConfiguracionPacResponse>;
