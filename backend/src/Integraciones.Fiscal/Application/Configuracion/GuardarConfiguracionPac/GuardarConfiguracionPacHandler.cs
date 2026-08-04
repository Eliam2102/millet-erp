using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Application.IntegrationEvents;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;

/// <summary>
/// Handler de <see cref="GuardarConfiguracionPacCommand"/>: upsert
/// idempotente por <c>(empresaId, proveedor)</c>. Cifra el
/// <c>ApiKey</c> con <see cref="FiscalSecretCipher"/> (ADR-0037)
/// dentro de la misma TX EF.
///
/// <para>
/// El handler corre con la <see cref="ICurrentEmpresaContext"/> del
/// JWT, que debe coincidir con el <c>EmpresaId</c> del command — para
/// que multi-tenancy se respete. Sin coincidencia, throw
/// <see cref="ForbiddenException"/> con
/// <c>CROSS_TENANT_VIOLATION</c>.
/// </para>
/// </summary>
public sealed class GuardarConfiguracionPacHandler
    : IRequestHandler<GuardarConfiguracionPacCommand, ConfiguracionPacResponse>
{
    private readonly IntegracionesFiscalDbContext _db;
    private readonly FiscalSecretCipher _cipher;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IConfiguracionPacResolver _resolver;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public GuardarConfiguracionPacHandler(
        IntegracionesFiscalDbContext db,
        FiscalSecretCipher cipher,
        IIntegrationEventPublisher publisher,
        IConfiguracionPacResolver resolver,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock)
    {
        _db = db;
        _cipher = cipher;
        _publisher = publisher;
        _resolver = resolver;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
    }

    public async Task<ConfiguracionPacResponse> Handle(
        GuardarConfiguracionPacCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var empresaActual = _currentEmpresa.Current
            ?? throw new SharedKernel.Application.Exceptions.ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT.");

        if (empresaActual != command.EmpresaId)
        {
            throw new SharedKernel.Application.Exceptions.CrossTenantViolationException(
                nameof(ConfiguracionPac), empresaActual, command.EmpresaId);
        }

        var ahora = _clock.UtcNow;

        var existente = await _db.ConfiguracionesPac
            .FirstOrDefaultAsync(
                c => c.EmpresaId == command.EmpresaId && c.Proveedor == command.Proveedor,
                ct);

        bool rotacion = false;
        ConfiguracionPac config;

        if (existente is null)
        {
            // Creación: ApiKey es obligatorio.
            if (string.IsNullOrWhiteSpace(command.ApiKey))
            {
                throw new SharedKernel.Application.Exceptions.BusinessRuleException(
                    "CONFIG_PAC_APIKEY_REQUERIDA",
                    "Al crear una configuración nueva, ApiKey es obligatorio.");
            }

            var cifrado = _cipher.Encrypt(command.ApiKey);
            var hash = FiscalSecretCipher.HashForChangeDetection(command.ApiKey);

            config = new ConfiguracionPac(
                id: Guid.CreateVersion7(),
                empresaId: command.EmpresaId,
                proveedor: command.Proveedor,
                baseUrl: command.BaseUrl,
                apiKeyCifrado: cifrado,
                apiKeyHash: hash,
                ahora: ahora);

            if (!command.Activo) config.Desactivar();

            _db.ConfiguracionesPac.Add(config);
            rotacion = true; // alta = rotación inicial
        }
        else
        {
            config = existente;

            // BaseUrl siempre se actualiza. Timeouts/retry/schedule
            // legacy quedaron eliminados en PR-13 (los maneja el SDK
            // NuGet y los workers nuevos con config global).
            config.ActualizarBaseUrl(command.BaseUrl);

            if (command.Activo) config.Activar(); else config.Desactivar();

            // Rotación del ApiKey solo si el caller mandó valor.
            if (!string.IsNullOrWhiteSpace(command.ApiKey))
            {
                var nuevoHash = FiscalSecretCipher.HashForChangeDetection(command.ApiKey);
                if (nuevoHash != config.ApiKeyHash)
                {
                    var nuevoCifrado = _cipher.Encrypt(command.ApiKey);
                    config.RotarApiKey(nuevoCifrado, nuevoHash, ahora);
                    rotacion = true;
                }
            }
        }

        // Identidades de prueba (upsert completo: null limpia). El domain
        // rechaza la captura si la BaseUrl no es la del sandbox.
        config.ConfigurarIdentidadesSandbox(
            MapIdentidad(command.EmisorSandbox),
            MapIdentidad(command.ReceptorSandbox));

        // CSD del emisor: misma semántica que el ApiKey (null = no tocar;
        // valor = capturar/rotar, idempotente por hash combinado).
        if (command.Csd is { } csd)
        {
            var csdHash = FiscalSecretCipher.HashForChangeDetection(
                $"{csd.CertificadoBase64}|{csd.LlavePrivadaBase64}|{csd.Password}");
            if (csdHash != config.CsdHash)
            {
                // Rechaza AQUÍ el trío inválido (password que no abre la
                // llave, .cer/.key de pares distintos, cert vencido) — sin
                // esto el error aparece hasta el timbrado (incidente
                // 2026-07-11: "The .KEY's password is incorrect").
                CsdValidador.Validar(csd.CertificadoBase64, csd.LlavePrivadaBase64, csd.Password, ahora);

                config.ConfigurarCsd(
                    certificadoCifrado: _cipher.Encrypt(csd.CertificadoBase64),
                    llavePrivadaCifrada: _cipher.Encrypt(csd.LlavePrivadaBase64),
                    passwordCifrado: _cipher.Encrypt(csd.Password),
                    hash: csdHash,
                    ahora: ahora);
                rotacion = true;
            }
        }

        await _db.SaveChangesAsync(ct);

        // Invalidar cache del resolver: el próximo ping/descarga ve los
        // datos frescos sin esperar al TTL de 60s.
        _resolver.Invalidar(config.EmpresaId, config.Proveedor);

        await _publisher.PublishAsync(
            new IntegracionesFiscalConfiguracionActualizadaEvent(
                ConfiguracionId: config.Id,
                EmpresaId: config.EmpresaId,
                Proveedor: (short)config.Proveedor,
                BaseUrl: config.BaseUrl,
                Activo: config.Activo,
                Rotacion: rotacion,
                OcurridoEn: ahora),
            ct);

        return MapToResponse(config);
    }

    internal static ConfiguracionPacResponse MapToResponse(ConfiguracionPac c)
    {
        return new ConfiguracionPacResponse(
            Id: c.Id,
            EmpresaId: c.EmpresaId,
            Proveedor: (short)c.Proveedor,
            ProveedorNombre: c.Proveedor.ToString(),
            BaseUrl: c.BaseUrl,
            ApiKey: "••••",
            ApiKeyConfigured: c.ApiKeyCifrado.Length > 0,
            Activo: c.Activo,
            UltimaRotacionAt: c.UltimaRotacionAt,
            UltimaTestConexionAt: c.UltimaTestConexionAt,
            UltimaTestConexionExitosa: c.UltimaTestConexionExitosa,
            EmisorSandbox: MapIdentidadDto(c.EmisorSandbox),
            ReceptorSandbox: MapIdentidadDto(c.ReceptorSandbox),
            CsdConfigurado: c.CsdConfigurado,
            CsdActualizadoAt: c.CsdActualizadoAt,
            CreatedAt: c.CreatedAt,
            UpdatedAt: c.UpdatedAt,
            Version: c.Version);
    }

    private static IdentidadSandbox? MapIdentidad(IdentidadSandboxDto? dto) =>
        dto is null ? null : new IdentidadSandbox(dto.Rfc, dto.RazonSocial, dto.RegimenFiscal, dto.CodigoPostal);

    private static IdentidadSandboxDto? MapIdentidadDto(IdentidadSandbox? i) =>
        i is null ? null : new IdentidadSandboxDto(i.Rfc, i.RazonSocial, i.RegimenFiscal, i.CodigoPostal);
}
