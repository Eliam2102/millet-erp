using System.Text.Json;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.OrdenCompra;

namespace Millet.Compras.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="IGenerarSolicitudCompraPort"/>. Persiste cada
/// invocación como una fila en <c>compras.oc_borrador_stub</c> con el
/// payload de líneas serializado a JSON. Devuelve el id de la fila como
/// <c>OrdenCompraId</c> sustituto.
///
/// <para>
/// La <c>EmpresaId</c> de la fila la asigna automáticamente el
/// <c>EmpresaContextSaveChangesInterceptor</c> desde el JWT (ADR-0011);
/// el stub solo provee el resto de los datos.
/// </para>
///
/// PLATFORM-TODO(<![CDATA[<StubsTeardown>]]>): borrar cuando OC real exista.
/// </summary>
public sealed class InMemoryGenerarSolicitudCompraPort : IGenerarSolicitudCompraPort
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly ComprasDbContext _db;
    private readonly ILogger<InMemoryGenerarSolicitudCompraPort> _logger;

    public InMemoryGenerarSolicitudCompraPort(
        ComprasDbContext db,
        ILogger<InMemoryGenerarSolicitudCompraPort> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Guid> GenerarBorradorAsync(
        Guid origenRequisicionId,
        IReadOnlyList<LineaSaldo> saldoNoCubierto,
        CancellationToken cancellationToken)
    {
        var lineasJson = JsonSerializer.Serialize(saldoNoCubierto, JsonOptions);
        var stub = new Stubs.OcBorradorStub(
            id: Guid.CreateVersion7(),
            empresaId: Guid.Empty, // lo llena EmpresaContextSaveChangesInterceptor
            origenRequisicionId: origenRequisicionId,
            lineasJson: lineasJson);

        _db.OcBorradorStubs.Add(stub);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[InMemoryGenerarSolicitudCompraPort] origenRequisicionId={Origen} ocBorradorStubId={StubId} lineasCount={Lineas}",
            origenRequisicionId, stub.Id, saldoNoCubierto.Count);

        return stub.Id;
    }
}
