namespace Millet.Catalogos.Domain;

/// <summary>
/// Origen del tipo de cambio (F-Admin-PR5.1). Identifica cómo se capturó
/// el valor: <see cref="Manual"/> (admin lo capturó a mano),
/// <see cref="DOF"/> (publicación oficial del Diario Oficial de la
/// Federación, usado para CFDI y contabilidad), <see cref="Banxico"/>
/// (alternativo via SIE de Banco de México).
///
/// <para>
/// Persiste como <c>smallint</c>. Default <see cref="Manual"/> hasta que
/// se integre un importer DOF/Banxico (deferred).
/// </para>
/// </summary>
public enum OrigenTipoCambio : short
{
    Manual = 0,
    DOF = 1,
    Banxico = 2,
}
