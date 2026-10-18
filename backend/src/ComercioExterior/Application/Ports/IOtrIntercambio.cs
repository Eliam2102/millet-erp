using Millet.ComercioExterior.Domain;
namespace Millet.ComercioExterior.Application.Ports;
public sealed record OtrEntrada(string IdOrigen, long Version, OtrContenido Contenido, string? ErrorLectura = null, bool ErrorTecnico = false);
public sealed record OtrEntrega(Guid Id, string IdOrigen, string Tipo, int VersionErp, string ValorJson, int Intento = 1);
public sealed record OtrAcuse(Guid SalidaId, DateTimeOffset Fecha, string Resultado, string? Motivo);
public interface IOtrEntradaReader
{
    Task<IReadOnlyList<OtrEntrada>> LeerPendientesAsync(int max, CancellationToken ct);
    Task MarcarResultadoAsync(string idOrigen, long version, string resultado, string? motivo, CancellationToken ct);
}
public interface IOtrSalidaWriter
{
    Task EntregarAsync(OtrEntrega salida, CancellationToken ct);
    Task<IReadOnlyList<OtrAcuse>> LeerAcusesAsync(IReadOnlyCollection<Guid> salidas, CancellationToken ct);
}
