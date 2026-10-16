namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>
/// Puerto de lectura del catálogo de tipos de cambio (ADR-0014).
/// CxP lo consume al capturar facturas en moneda extranjera, anticipos
/// y movimientos de TC en USD.
///
/// <para>
/// Adapter real: <c>TipoCambioReadPortAdapter</c> sobre
/// <c>compartido.tipos_cambio</c> (C1.2). Devuelve <c>null</c> si no hay
/// TC registrado para la fecha; nunca un valor inventado.
/// </para>
/// </summary>
public interface ITipoCambioReadPort
{
    Task<decimal?> ObtenerAsync(
        string monedaOrigen,
        string monedaDestino,
        DateOnly fecha,
        CancellationToken cancellationToken);
}
