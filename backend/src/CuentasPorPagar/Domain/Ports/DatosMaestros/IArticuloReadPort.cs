namespace Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;

/// <summary>
/// Puerto de lectura del catálogo de artículos (vive en DatosMaestros).
/// CxP lo consume al conciliar líneas de factura contra OC para mostrar
/// código/descripción y unidades.
/// </summary>
public interface IArticuloReadPort
{
    Task<ArticuloDto?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken);
}

public sealed record ArticuloDto(Guid Id, string Codigo, string Descripcion, string UnidadMedida);
