namespace Millet.SharedKernel.Application.UnidadesMedida;

/// <summary>
/// Una cantidad capturada a validar contra los decimales de la unidad de
/// su artículo. <see cref="UnidadEtiqueta"/> es el string snapshot de la
/// unidad en la línea (solo para enriquecer el mensaje de error; opcional).
/// </summary>
public sealed record CantidadAValidar(Guid ArticuloId, decimal Cantidad, string? UnidadEtiqueta = null);

/// <summary>
/// Guard compartido y agnóstico de módulo que aplica la regla de decimales
/// por unidad sobre un conjunto de cantidades capturadas (ADR-0046 Etapa 2;
/// regla de negocio con I/O → vive en el handler, ADR-0018).
///
/// <para>
/// Resuelve los decimales de todas las líneas en un solo round-trip vía
/// <see cref="IUnidadMedidaReadPort"/> y lanza
/// <c>BusinessRuleException(<see cref="DecimalesUnidad.CodigoError"/>)</c>
/// en la primera línea inválida. Las líneas cuyo artículo no resuelve a una
/// unidad de catálogo (FK NULL) se omiten.
/// </para>
/// </summary>
public interface IDecimalesUnidadGuard
{
    Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken cancellationToken);
}
