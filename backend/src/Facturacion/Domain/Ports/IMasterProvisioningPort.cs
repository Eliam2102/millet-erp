namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Auto-provisión del master de Cliente/Producto desde las vistas de A+W
/// (§3.bis.6, D del diseño). <b>Único caso</b> de alta automática del master
/// desde una vista, y <b>sólo para origen A+W</b> (Planta Pintura exige
/// preexistencia). El alta real vive en <c>DatosMaestros</c> (cero escritura
/// directa al master de otro módulo); este puerto lo expone DatosMaestros, que
/// internamente consume los readers de <c>Integraciones.Aw</c>.
///
/// <para>
/// F3-PR2 lo cablea con un stub que <b>no</b> auto-crea (devuelve <c>null</c> →
/// la matriz cae a excepción) hasta que DatosMaestros soporte la provisión
/// Adapter real: <c>AwMasterProvisioningAdapter</c> (ADR-0048 PR4). El contrato y el wireup
/// de la matriz quedan listos.
/// </para>
/// </summary>
public interface IMasterProvisioningPort
{
    /// <summary>
    /// Asegura que el cliente referenciado exista en el master, creándolo desde
    /// la vista de clientes de A+W si falta. Devuelve sus datos fiscales, o
    /// <c>null</c> si no se pudo provisionar.
    /// </summary>
    Task<ClienteFiscalLectura?> EnsureClienteDesdeAwAsync(string clienteRef, CancellationToken cancellationToken);

    /// <summary>
    /// Asegura que el artículo referenciado exista en el master (con
    /// <c>origen = A+W</c>), creándolo desde la vista de artículos de A+W si
    /// falta. Devuelve sus datos fiscales, o <c>null</c> si no se pudo provisionar.
    /// </summary>
    Task<ProductoFiscalLectura?> EnsureArticuloDesdeAwAsync(string articuloRef, CancellationToken cancellationToken);
}
