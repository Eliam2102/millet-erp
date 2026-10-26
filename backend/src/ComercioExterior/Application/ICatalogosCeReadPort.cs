using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.Application;
public sealed record ReferenciaCe(Guid Id, string Nombre);
public interface ICatalogosCeReadPort
{
    Task<IReadOnlyList<ReferenciaCe>> ReferenciasAsync(string tipo, string q, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> NombresTercerosAsync(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    Task<Guid> IncotermDdpAsync(CancellationToken ct);
    Task ValidarAsync(Guid incoterm, string moneda, Guid sucursal, Guid responsable, CancellationToken ct);
    Task ValidarResponsableAsync(Guid sucursal, Guid responsable, CancellationToken ct);
    Task ValidarParticipanteAsync(Guid? proveedor, Guid? cliente, CancellationToken ct);
    Task<IReadOnlyList<RequisitoDocumento>> RequisitosAsync(TipoExpediente tipo, CancellationToken ct);
    Task<IReadOnlyList<Guid>> AdjuntosVigentesAsync(Guid expediente, CancellationToken ct);
    Task ValidarAdjuntoAsync(Guid expediente, Guid tipo, Guid adjunto, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IEnumerable<Guid> ids, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> SucursalesAsync(IEnumerable<Guid> ids, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> IncotermsAsync(IEnumerable<Guid> ids, CancellationToken ct);
}
