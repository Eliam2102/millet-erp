namespace Millet.Tesoreria.Domain.Devoluciones;

/// <summary>
/// Secuencia atómica del folio «DEV-T-0001» por empresa. La reserva es un upsert
/// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> (mismo patrón que
/// <c>cuentas_por_pagar.folio_secuencias_nota_cargo</c>); <c>Siguiente</c> es el próximo número a entregar.
/// </summary>
public sealed class FolioSecuenciaDevolucionCliente
{
    public Guid EmpresaId { get; private set; }
    public int Siguiente { get; private set; }

    private FolioSecuenciaDevolucionCliente() { }
}
