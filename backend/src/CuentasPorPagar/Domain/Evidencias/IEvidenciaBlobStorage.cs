namespace Millet.CuentasPorPagar.Domain.Evidencias;

/// <summary>
/// Puerto de almacenamiento de archivos de evidencias polimórficas
/// (§4.10 del 00-levantamiento, F4-PR2). Convención de path:
/// <c>cxp/{año}/{mes}/evidencias/{documentoId}/{evidenciaId}.{ext}</c>.
///
/// <para>
/// Separado de <c>ICfdiBlobStorage</c> para que el adapter real (Azure
/// Blob) pueda configurar containers / SAS tokens distintos (las
/// evidencias pueden contener fotos personales / audios privados —
/// §6.2 del 04-cuidados-infra exige container privado con SAS de 1h).
/// </para>
/// </summary>
public interface IEvidenciaBlobStorage
{
    Task<string> GuardarAsync(
        Guid documentoId,
        Guid evidenciaId,
        DateTimeOffset ahora,
        string nombreArchivo,
        string contentType,
        Stream contenido,
        CancellationToken cancellationToken);
}
