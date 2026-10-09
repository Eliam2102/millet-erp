using MediatR;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.TestConexionPac;

/// <summary>
/// Test de conexión al PAC. Usa el SDK NuGet oficial vía
/// <see cref="IFiscalApiSdkClient.PingAsync"/> que pega un GET barato
/// autenticado a <c>/api/v4/download-catalogs</c>. Es una operación de solo
/// lectura: nunca cambia la configuración persistida.
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

    public TestConexionPacHandler(IFiscalApiSdkClient sdk,
        Infrastructure.Persistence.IntegracionesFiscalDbContext db,
        Infrastructure.Cifrado.FiscalSecretCipher cipher, IPacCandidatoProbe probe)
        => (_sdk, _db, _cipher, _probe) = (sdk, db, cipher, probe);

    public async Task<TestConexionPacResponse> Handle(
        TestConexionPacCommand command, CancellationToken cancellationToken)
    {
        PingResultDto result;
        if (command.BaseUrl is not null || !string.IsNullOrWhiteSpace(command.ApiKey))
        {
            if (command.Proveedor != ProveedorPac.FiscalApi)
                throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException(
                    "CONFIG_PAC_PROVEEDOR_INVALIDO", "Solo FiscalAPI está habilitado.");
            var actual = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                _db.ConfiguracionesPac.Where(c => c.EmpresaId == command.EmpresaId && c.Proveedor == command.Proveedor), cancellationToken);
            var baseUrl = command.BaseUrl ?? actual?.BaseUrl;
            var key = string.IsNullOrWhiteSpace(command.ApiKey)
                ? actual is null ? null : _cipher.Decrypt(actual.ApiKeyCifrado)
                : command.ApiKey;
            if (baseUrl is null || string.IsNullOrWhiteSpace(key))
                throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException(
                    "CONFIG_PAC_APIKEY_REQUERIDA", "Captura las credenciales antes de probar la conexión.");
            ConfiguracionPac.ValidarBaseUrl(baseUrl);
            result = await _probe.ProbarAsync(baseUrl, key, cancellationToken);
        }
        else
            result = await _sdk.PingAsync(command.EmpresaId, cancellationToken);

        return new TestConexionPacResponse(
            Exitosa: result.Exitosa,
            StatusCode: result.StatusCode,
            Mensaje: MensajePara(result),
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
