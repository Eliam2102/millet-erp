namespace Millet.CuentasPorPagar.Domain.NotaCargo;

/// <summary>
/// Secuencia atómica de folios NCG por <c>(empresa, año)</c>. El
/// handler de creación hace upsert atómico:
/// <code>
/// INSERT INTO folio_secuencias_nota_cargo (empresa_id, anio, siguiente)
/// VALUES ({empresaId}, {anio}, 2)
/// ON CONFLICT (empresa_id, anio) DO UPDATE
///   SET siguiente = folio_secuencias_nota_cargo.siguiente + 1
/// RETURNING (siguiente - 1);
/// </code>
/// Mismo patrón que <c>compras.folio_secuencias</c>.
/// </summary>
public sealed class FolioSecuenciaNotaCargo
{
    public Guid EmpresaId { get; private set; }
    public short Anio { get; private set; }
    public int Siguiente { get; private set; }

    private FolioSecuenciaNotaCargo() { }
}
