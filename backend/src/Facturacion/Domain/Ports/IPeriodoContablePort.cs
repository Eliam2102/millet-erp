namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto del candado de período contable (dueño: <c>Contabilidad</c>, aún
/// inexistente). El módulo <b>no</b> permite emitir, cancelar ni modificar un
/// comprobante con fecha de un período (mes) ya cerrado (D13). Hasta que exista
/// Contabilidad, el stub retorna siempre "abierto"
/// (<c>PLATFORM-TODO(&lt;PeriodoContableCerrado&gt;)</c>).
/// </summary>
public interface IPeriodoContablePort
{
    /// <summary>¿Está abierto el período (año, mes) para operar comprobantes?</summary>
    Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken);
}
