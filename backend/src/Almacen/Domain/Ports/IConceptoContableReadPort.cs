namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Contabilidad (futuro) para mapear concepto
/// contable → cuenta. Los eventos publicados por Almacén
/// (<c>EntradaInventarioValoradaEvent</c>, <c>SalidaRequisicionRegistradaEvent</c>,
/// etc.) incluyen el concepto; Contabilidad lo resuelve a cuenta al
/// generar la póliza. Almacén lo usa como validación pre-emisión.
/// </summary>
public interface IConceptoContableReadPort
{
    Task<ConceptoContableLectura?> ObtenerAsync(
        string codigoConcepto,
        CancellationToken cancellationToken);
}

public sealed record ConceptoContableLectura(
    string Codigo,
    string Nombre,
    string? CuentaDeudora,
    string? CuentaAcreedora,
    bool EsActivo);
