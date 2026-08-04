using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.TestConexionPac;

/// <summary>
/// Test de conexión al PAC. Usa el SDK NuGet oficial vía
/// <see cref="IFiscalApiSdkClient.PingAsync"/> que pega un GET barato
/// autenticado a <c>/api/v4/download-catalogs</c>. Si hay configuración
/// persistida, también actualiza el timestamp + resultado de la última
/// prueba para auditoría.
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
    private readonly IntegracionesFiscalDbContext _db;
    private readonly IFiscalApiSdkClient _sdk;
    private readonly IClock _clock;

    public TestConexionPacHandler(
        IntegracionesFiscalDbContext db,
        IFiscalApiSdkClient sdk,
        IClock clock)
    {
        _db = db;
        _sdk = sdk;
        _clock = clock;
    }

    public async Task<TestConexionPacResponse> Handle(
        TestConexionPacCommand command, CancellationToken cancellationToken)
    {
        var result = await _sdk.PingAsync(command.EmpresaId, cancellationToken);

        // Si hay config persistida, actualizar el timestamp del último test
        // para auditoría. Si no existe, el ping vino del flow "test antes
        // de guardar" — nada que actualizar.
        var existing = await _db.ConfiguracionesPac
            .FirstOrDefaultAsync(
                c => c.EmpresaId == command.EmpresaId && c.Proveedor == command.Proveedor,
                cancellationToken);

        if (existing is not null)
        {
            existing.RegistrarTestConexion(result.Exitosa, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new TestConexionPacResponse(
            Exitosa: result.Exitosa,
            StatusCode: result.StatusCode,
            Mensaje: result.Mensaje,
            TiempoMs: result.TiempoMs,
            ConsultadoEn: result.ConsultadoEn);
    }
}
