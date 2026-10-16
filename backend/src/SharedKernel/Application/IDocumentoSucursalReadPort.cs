namespace Millet.SharedKernel.Application;

public sealed record DocumentoSucursales(Guid Id, IReadOnlyList<Guid> Sucursales);
public interface IDocumentoSucursalReadPort
{
    Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct);
    async Task<DocumentoSucursales?> ObtenerAsync(string tipo, Guid id, CancellationToken ct)
        => (await ListarAsync(tipo, ct)).FirstOrDefault(x => x.Id == id);
}
public interface IAlmacenSucursalReadPort : IDocumentoSucursalReadPort;
public interface IComprasSucursalReadPort : IDocumentoSucursalReadPort;
public interface IFacturacionSucursalReadPort : IDocumentoSucursalReadPort;
public interface ICxpSucursalReadPort : IDocumentoSucursalReadPort;
public interface ICxcSucursalReadPort : IDocumentoSucursalReadPort;
public interface ITesoreriaSucursalReadPort : IDocumentoSucursalReadPort;
public interface IComercioExteriorSucursalReadPort : IDocumentoSucursalReadPort;
public interface IDocumentoScopedQuery : ISucursalScopedQuery
{
    string TipoDocumento { get; }
    IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}
