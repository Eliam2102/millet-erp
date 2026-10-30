using Millet.Documentos;

namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Encabezado de la empresa (razón social, domicilio y logo) para los documentos impresos de
/// Almacén. El adapter vive en Compartido y se cablea en <c>Program.cs</c>; null = la empresa
/// no existe y el documento sale sin encabezado.
/// </summary>
public interface IEmpresaImpresionReadPort
{
    Task<EmpresaEncabezado?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken);
}
