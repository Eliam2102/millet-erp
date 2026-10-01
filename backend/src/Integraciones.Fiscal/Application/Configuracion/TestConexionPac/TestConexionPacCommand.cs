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
/// <para>
/// Tras PR-13 los parámetros transitorios (<see cref="ApiKey"/>,
/// <see cref="BaseUrl"/>) se conservan en el contrato para
/// compatibilidad con el frontend, pero el SDK NuGet resuelve la
/// configuración persistida por empresa internamente. Si quieres probar
/// una key NUEVA antes de guardarla, primero la persistes con el
/// command de guardar y luego pruebas — el wrapper invalida cache y
/// resuelve la nueva.
/// </para>
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

    public TestConexionPacHandler(IFiscalApiSdkClient sdk) => _sdk = sdk;

    public async Task<TestConexionPacResponse> Handle(
        TestConexionPacCommand command, CancellationToken cancellationToken)
    {
        var result = await _sdk.PingAsync(command.EmpresaId, cancellationToken);

        return new TestConexionPacResponse(
            Exitosa: result.Exitosa,
            StatusCode: result.StatusCode,
            Mensaje: MensajePara(result),
            TiempoMs: result.TiempoMs,
            ConsultadoEn: result.ConsultadoEn);
    }

    private static string MensajePara(PingResultDto result) => result switch
    {
        { Exitosa: true } => "Conexión exitosa con FiscalAPI.",
        { StatusCode: 422 } => "Configura y activa las credenciales de FiscalAPI antes de probar la conexión.",
        { StatusCode: 501 } => "La integración fiscal no está habilitada en este ambiente. Contacta a TI.",
        { StatusCode: 503 } => "FiscalAPI no está disponible temporalmente. Intenta de nuevo.",
        { StatusCode: 0 } => "No fue posible conectar con FiscalAPI dentro del tiempo esperado.",
        _ => $"FiscalAPI rechazó la conexión (HTTP {result.StatusCode}). Revisa la configuración.",
    };
}
