using Millet.Almacen.Domain.Ports;

namespace Millet.Almacen.UnitTests.TestSupport;

/// <summary>
/// Fake de <see cref="ICxpDocumentosReadPort"/> para validar el vínculo CFDI de una recepción (F3).
/// Solo <see cref="ObtenerCfdiAsync"/> responde; el resto devuelve diccionarios vacíos.
/// </summary>
public sealed class CfdiFake(CfdiRecibidoLectura? cfdi) : ICxpDocumentosReadPort
{
    /// <summary>CFDI válido (PorProcesar/Ingreso) del RFC indicado; el id es aleatorio y el handler no lo compara.</summary>
    public static CfdiFake Valido(string rfc = "AAA010101AAA") =>
        new(new CfdiRecibidoLectura(Guid.NewGuid(), rfc, "PorProcesar", "Ingreso"));

    public Task<CfdiRecibidoLectura?> ObtenerCfdiAsync(Guid cfdiRecibidoId, CancellationToken cancellationToken) =>
        Task.FromResult(cfdi);

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(
        IReadOnlyCollection<Guid> facturaIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(
        IReadOnlyCollection<Guid> cfdiRecibidoIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(
        IReadOnlyCollection<Guid> notaCreditoIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

/// <summary>
/// Fake de <see cref="IProveedorReadPort"/> que responde cualquier proveedor con el RFC indicado.
/// </summary>
public sealed class ProveedorRfcFake(string rfc = "AAA010101AAA") : IProveedorReadPort
{
    public Task<ProveedorLectura?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken) =>
        Task.FromResult<ProveedorLectura?>(new ProveedorLectura(proveedorId, "P", "Proveedor", rfc, true));

    public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(new Dictionary<Guid, ProveedorLectura>());
}
