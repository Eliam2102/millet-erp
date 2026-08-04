namespace Millet.SharedKernel.Application.UnidadesMedida;

/// <summary>
/// Puerto de lectura compartido que resuelve, en batch, los decimales
/// permitidos por la unidad de medida de cada artículo
/// (<c>articulos.unidad_medida_id → unidades_medida.decimales</c>).
/// ADR-0046 Etapa 2.
///
/// <para>
/// Lo consume <see cref="IDecimalesUnidadGuard"/> para validar que una
/// cantidad capturada no exceda los decimales de su unidad. Batch (no
/// por-id) para resolver todas las líneas de un comando en un solo
/// round-trip, evitando N+1.
/// </para>
///
/// <para>
/// El valor es <c>null</c> cuando el artículo no tiene unidad de catálogo
/// (FK <c>unidad_medida_id</c> NULL — artículos legacy aún sin reconciliar,
/// ~8% tras 1b); en ese caso la validación se <b>omite</b> y la cantidad
/// cae al comportamiento previo. Los artículos inexistentes simplemente no
/// aparecen en el diccionario (el guard los trata igual que FK NULL).
/// </para>
/// </summary>
public interface IUnidadMedidaReadPort
{
    Task<IReadOnlyDictionary<Guid, int?>> ObtenerDecimalesPorArticulosAsync(
        IEnumerable<Guid> articuloIds,
        CancellationToken cancellationToken);
}
