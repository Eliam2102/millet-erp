using MediatR;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.TestConexionPac;

/// <summary>
/// Test de conexión al PAC. Usa el SDK NuGet oficial vía
/// <see cref="IFiscalApiSdkClient.PingAsync"/> que pega un GET barato
/// autenticado a <c>/api/v4/download-catalogs</c>. Nunca cambia la
/// configuración persistida. M-232: cuando lo probado ES la configuración
/// guardada (sin candidato, o candidato con la misma URL y ApiKey) registra la
/// prueba en <see cref="PruebaConexionPac"/> (aparte de la configuración) y, si
/// el timbrador la rechaza, abre una incidencia común (se cierra con la
/// siguiente prueba exitosa).
///
/// <para>Con BaseUrl o ApiKey prueba el candidato en memoria. Los valores
/// vacíos conservan los secretos vigentes. Sin candidato prueba lo persistido.
/// Guardar vuelve a probar el candidato exacto antes de escribir.</para>
/// </summary>
public sealed record TestConexionPacCommand(
    Guid EmpresaId,
    ProveedorPac Proveedor,
    string? BaseUrl,
    string? ApiKey,
    int? TimeoutSegundos) : IRequest<TestConexionPacResponse>;

public sealed record TestConexionPacResponse(
    bool Exitosa,
    int StatusCode,
    string Mensaje,
    long TiempoMs,
    DateTimeOffset ConsultadoEn);

public sealed class TestConexionPacHandler
    : IRequestHandler<TestConexionPacCommand, TestConexionPacResponse>
{
    private readonly IFiscalApiSdkClient _sdk;
    private readonly Infrastructure.Persistence.IntegracionesFiscalDbContext _db;
    private readonly Infrastructure.Cifrado.FiscalSecretCipher _cipher;
    private readonly IPacCandidatoProbe _probe;
    private readonly Millet.SharedKernel.Application.Incidencias.IIncidenciasPort? _incidencias;
    private readonly Millet.SharedKernel.Application.ICurrentUserContext? _user;
    private readonly Microsoft.Extensions.Logging.ILogger<TestConexionPacHandler>? _logger;

    public TestConexionPacHandler(IFiscalApiSdkClient sdk,
        Infrastructure.Persistence.IntegracionesFiscalDbContext db,
        Infrastructure.Cifrado.FiscalSecretCipher cipher, IPacCandidatoProbe probe,
        Millet.SharedKernel.Application.Incidencias.IIncidenciasPort? incidencias = null,
        Millet.SharedKernel.Application.ICurrentUserContext? user = null,
        Microsoft.Extensions.Logging.ILogger<TestConexionPacHandler>? logger = null)
        => (_sdk, _db, _cipher, _probe, _incidencias, _user, _logger) = (sdk, db, cipher, probe, incidencias, user, logger);

    public async Task<TestConexionPacResponse> Handle(
        TestConexionPacCommand command, CancellationToken cancellationToken)
    {
        PingResultDto result;
        var actual = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.ConfiguracionesPac.Where(c => c.EmpresaId == command.EmpresaId && c.Proveedor == command.Proveedor), cancellationToken);
        var esLaPersistida = actual is not null;
        if (command.BaseUrl is not null || !string.IsNullOrWhiteSpace(command.ApiKey))
        {
            if (command.Proveedor != ProveedorPac.FiscalApi)
                throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException(
                    "CONFIG_PAC_PROVEEDOR_INVALIDO", "Solo FiscalAPI está habilitado.");
            var baseUrl = command.BaseUrl ?? actual?.BaseUrl;
            var key = string.IsNullOrWhiteSpace(command.ApiKey)
                ? actual is null ? null : _cipher.Decrypt(actual.ApiKeyCifrado)
                : command.ApiKey;
            if (baseUrl is null || string.IsNullOrWhiteSpace(key))
                throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException(
                    "CONFIG_PAC_APIKEY_REQUERIDA", "Captura las credenciales antes de probar la conexión.");
            ConfiguracionPac.ValidarBaseUrl(baseUrl);
            esLaPersistida = actual is not null && baseUrl == actual.BaseUrl
                && Infrastructure.Cifrado.FiscalSecretCipher.HashForChangeDetection(key!) == actual.ApiKeyHash;
            result = await _probe.ProbarAsync(baseUrl, key!, cancellationToken);
        }
        else
            result = await _sdk.PingAsync(command.EmpresaId, cancellationToken);

        var mensaje = MensajePara(result);
        if (esLaPersistida && actual is not null)
        {
            // M-232: un candidato distinto no es la configuración; solo se registra la prueba de lo persistido,
            // en su propia tabla: la configuración (y su versión) no cambia.
            _db.PruebasConexionPac.Add(new PruebaConexionPac(actual.EmpresaId, actual.Proveedor, actual.Id,
                OrigenPruebaConexionPac.Prueba, result.Exitosa, result.StatusCode, mensaje,
                result.ConsultadoEn == default ? DateTimeOffset.UtcNow : result.ConsultadoEn, _user?.UserName));
            await _db.SaveChangesAsync(cancellationToken);
            if (result.Exitosa)
                await IncidenciasConexionPac.ResolverAsync(_incidencias, _logger, actual.Id, cancellationToken);
            else
                await IncidenciasConexionPac.AbrirAsync(_incidencias, _logger, actual.Id, _user?.UserId,
                    actual.BaseUrl, result, mensaje, cancellationToken);
        }

        return new TestConexionPacResponse(
            Exitosa: result.Exitosa,
            StatusCode: result.StatusCode,
            Mensaje: mensaje,
            TiempoMs: result.TiempoMs,
            ConsultadoEn: result.ConsultadoEn);
    }

    internal static string MensajePara(PingResultDto result) => result switch
    {
        { Exitosa: true } => "Conexión exitosa con FiscalAPI.",
        { StatusCode: 422 } => "Configura y activa las credenciales de FiscalAPI antes de probar la conexión.",
        { StatusCode: 501 } => "La integración fiscal no está habilitada en este ambiente. Contacta a TI.",
        { StatusCode: 503 } => "FiscalAPI no está disponible temporalmente. Intenta de nuevo.",
        { StatusCode: 0 } => "No fue posible conectar con FiscalAPI dentro del tiempo esperado.",
        _ => $"FiscalAPI rechazó la conexión (HTTP {result.StatusCode}). Revisa la configuración.",
    };
}
