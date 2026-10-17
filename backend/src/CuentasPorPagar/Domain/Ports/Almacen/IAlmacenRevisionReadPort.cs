namespace Millet.CuentasPorPagar.Domain.Ports.Almacen;

/// <summary>
/// Puerto de lectura sobre Almacén para saber si una línea recibida quedó en el
/// sub-almacén de material en revisión (MAT-REV, ALM-04). CxP lo usa para detener
/// facturas con motivo «Daños» (P8.4a / CA8.7) sin cambiar el contrato del evento
/// <c>almacen.oc_recepcion.registrada.v1</c>.
/// </summary>
public interface IAlmacenRevisionReadPort
{
    /// <summary>Devuelve los ids de la lista cuyo sub-almacén es de tipo material en revisión.</summary>
    Task<IReadOnlySet<Guid>> FiltrarSubAlmacenesEnRevisionAsync(
        IReadOnlyCollection<Guid> subAlmacenIds,
        CancellationToken cancellationToken);
}
