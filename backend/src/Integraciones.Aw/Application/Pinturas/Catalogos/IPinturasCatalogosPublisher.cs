namespace Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

/// <summary>
/// Contrato para publicar catálogos del ERP hacia la base de integración MILLET_INTEGRACION
/// para el Portal de Pinturas (tablas pinturas_cliente, pinturas_servicio, pinturas_obra, pinturas_empleado).
/// </summary>
public interface IPinturasCatalogosPublisher
{
    /// <summary>
    /// Escribe de forma idempotente (upsert con baja lógica activo = 0) el lote de catálogos en base de datos.
    /// </summary>
    Task<PinturasPublicacionResultado> PublicarAsync(PinturasCatalogosBatch batch, CancellationToken cancellationToken);
}
