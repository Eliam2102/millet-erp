namespace Millet.Integraciones.Aw.Application.TipoCambio;

/// <summary>
/// Puerto de comunicación con la tabla puente dbo.erp_tipo_cambio y consulta de paridad en A+W (I13.2).
/// </summary>
public interface IAwTipoCambioPort
{
    /// <summary>
    /// Inserta una nueva fila de tipo de cambio en dbo.erp_tipo_cambio.
    /// Solo tiene permisos de INSERT y SELECT; denegados UPDATE y DELETE.
    /// </summary>
    Task<int?> PublicarFilaAsync(AwTipoCambioFila fila, CancellationToken ct = default);

    /// <summary>
    /// Obtiene los acuses de las filas existentes desde una fecha de aplicación dada para las monedas indicadas.
    /// </summary>
    Task<IReadOnlyList<AwTipoCambioAcuse>> ObtenerUltimosAcusesAsync(IReadOnlyList<string> monedas, DateOnly desde, CancellationToken ct = default);

    /// <summary>
    /// Consulta el tipo de cambio vigente registrado directamente en A+W (SYSADM.KA_WAEHRUNGEN) para supervisión de desfase.
    /// Devuelve VALUTAKURS tal cual: unidades de moneda extranjera por 1 MXN (inverso del valor del ERP).
    /// </summary>
    Task<decimal?> ObtenerTipoCambioVigenteAwAsync(string moneda, CancellationToken ct = default);
}
