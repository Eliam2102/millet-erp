namespace Millet.Compras.Domain.Ports;
/// <summary>Validación de clase sugerida sin acceso al esquema dueño de Activos Fijos.</summary>
public interface IClaseActivoCompraReadPort
{
    Task<bool> EsActivaAsync(Guid id, CancellationToken ct);
}
