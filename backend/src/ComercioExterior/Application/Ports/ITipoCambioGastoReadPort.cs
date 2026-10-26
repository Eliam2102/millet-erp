namespace Millet.ComercioExterior.Application.Ports;
public interface ITipoCambioGastoReadPort
{
    Task<decimal> ObtenerBanxicoAsync(DateOnly fecha, CancellationToken ct);
}
