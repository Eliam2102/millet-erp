namespace Millet.Almacen.Domain.Ports;

/// <summary>Delega en IDim3ElegibilidadPort sin crear el ciclo Almacén → Compartido → Almacén.</summary>
public interface ICentroCostoElegibilidadPort
{
    Task ValidarAsync(Guid centroCostoId, CancellationToken ct);
}
