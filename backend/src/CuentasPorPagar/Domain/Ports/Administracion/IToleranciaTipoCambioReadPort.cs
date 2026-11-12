namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>M-080: tolerancia (en %) del TC capturado contra el catálogo; es el mismo parámetro global de M-207.</summary>
public interface IToleranciaTipoCambioReadPort
{
    Task<decimal> ToleranciaPctAsync(CancellationToken cancellationToken);

    /// <summary>M-080: tolerancia absoluta en MXN por unidad de moneda extranjera (0 = apagada; entonces aplica el %).</summary>
    Task<decimal> ToleranciaAbsolutaMxnAsync(CancellationToken cancellationToken);
}
