using Millet.Contabilidad.Application.ContabilidadElectronica;

namespace Millet.Contabilidad.Application.Ports;

/// <summary>K10.8 · puerto de CONSUMIDOR: RFC de la empresa para los archivos del SAT. Lo implementa Compartido (dueño de empresas).</summary>
public interface IEmpresaContabilidadReadPort
{
    /// <summary>RFC vigente de la empresa; null si no existe o no lo tiene capturado.</summary>
    Task<string?> RfcAsync(Guid empresaId, CancellationToken ct);
}

/// <summary>
/// K10.8 · puerto de CONSUMIDOR: datos de un CFDI por UUID para el nodo <c>CompNal</c> de las pólizas (RFC de la contraparte y total).
/// Lo compone <c>Millet.Api</c> con los puertos públicos de CxP (CFDI recibidos) y Facturación (CFDI emitidos).
/// </summary>
public interface IComprobanteFiscalContabilidadReadPort
{
    /// <summary>Comprobantes encontrados, por UUID en mayúsculas. Los que no se encuentran no aparecen.</summary>
    Task<IReadOnlyDictionary<string, ComprobanteNacionalContable>> ObtenerAsync(IReadOnlyCollection<string> uuids, CancellationToken ct);
}
