namespace Millet.Tesoreria.Application.PublicPorts;

public sealed record PagoTrazabilidad(Guid Id, Guid FacturaId, string Folio, string Estado, DateTimeOffset Fecha);
public interface IPagosTrazabilidadReadPort
{
    Task<IReadOnlyList<PagoTrazabilidad>> ListarAsync(Guid? facturaId, Guid? pagoId, CancellationToken ct);
}
