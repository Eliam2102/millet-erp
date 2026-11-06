namespace Millet.Tesoreria.Application.PublicPorts;
public sealed record AutorizacionAnticipoCe(Guid AnticipoId, string Estado, Guid? Nivel1UsuarioId, DateTimeOffset? Nivel1En, Guid? Nivel2UsuarioId, DateTimeOffset? Nivel2En);
public interface IAutorizacionPagoCeReadPort
{
    Task<decimal> PagadoAsync(Guid anticipoId, CancellationToken ct);
    Task<IReadOnlyList<AutorizacionAnticipoCe>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
