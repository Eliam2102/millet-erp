namespace Millet.Compras.Domain.Ports.OrdenCompra;

/// <summary>
/// Puerto in-proc para generar una orden de compra borrador a partir
/// del saldo no cubierto de una requisición autorizada (diseño §8.2).
///
/// <para>
/// Aunque vive en el mismo Bounded Context que Compras (mismo módulo
/// monolítico), la separación por puerto permite:
/// </para>
/// <list type="bullet">
///   <item>Aislar la dependencia: F4-PR2 solo conoce el contrato.</item>
///   <item>Stub durante F3-PR2 (escribe a tabla provisional) sin
///         depender de la implementación final del submódulo OC.</item>
///   <item>Reemplazar el adapter cuando OC esté implementada sin tocar
///         el handler de Autorizar.</item>
/// </list>
/// <para>
/// Devuelve el id del borrador creado para auditabilidad. La OC arranca
/// en estado pre-emisión; se confirma/emite en flujos posteriores del
/// submódulo OC, fuera del scope de Compras.
/// </para>
/// </summary>
public interface IGenerarSolicitudCompraPort
{
    Task<Guid> GenerarBorradorAsync(
        Guid origenRequisicionId,
        IReadOnlyList<LineaSaldo> saldoNoCubierto,
        CancellationToken cancellationToken);
}
