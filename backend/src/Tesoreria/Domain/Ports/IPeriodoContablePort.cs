namespace Millet.Tesoreria.Domain.Ports;

/// <summary>
/// Puerto del candado de período contable (RN-8; dueño real:
/// <c>Contabilidad</c>, aún inexistente). Toda mutación de movimientos
/// valida que el período (año, mes) de la fecha valor esté abierto. Puerto
/// propio en el namespace de Tesorería (hexagonal), misma semántica que el
/// de Facturación (01-diseño §6.1). Hasta que exista Contabilidad, el stub
/// retorna siempre "abierto"
/// (<c>PLATFORM-TODO(&lt;PeriodoContableCerrado&gt;)</c>).
/// </summary>
public interface IPeriodoContablePort
{
    /// <summary>¿Está abierto el período (año, mes) para registrar movimientos bancarios?</summary>
    Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken);
}
