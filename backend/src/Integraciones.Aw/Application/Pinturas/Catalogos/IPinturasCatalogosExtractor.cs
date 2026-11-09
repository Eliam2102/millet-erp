namespace Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

/// <summary>
/// Contrato para extraer catálogos vigentes desde los datos maestros del ERP
/// proyectados hacia el esquema requerido por Planta Pintura.
/// </summary>
public interface IPinturasCatalogosExtractor
{
    /// <summary>
    /// Extrae los catálogos vigentes (clientes, servicios, obras, empleados) desde las fuentes de datos del ERP.
    /// </summary>
    Task<PinturasCatalogosBatch> ExtraerAsync(CancellationToken cancellationToken);
}
