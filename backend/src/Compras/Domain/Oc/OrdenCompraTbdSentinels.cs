namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Guids sentinel "TBD" para cabecera mínima de una OC borrador creada
/// automáticamente desde la bifurcación de una RQ (F5-PR3). Los 3 campos
/// requeridos del agregado se llenan con estos placeholders hasta que el
/// comprador complete la cabecera vía
/// <see cref="OrdenCompra.CompletarCabeceraBorrador"/>.
///
/// <para>
/// Se eligen <c>Guid.AllBitsSet</c> con un byte distinto al inicio para
/// minimizar la probabilidad de colisión con datos reales. No se siembran
/// en ningún catálogo — el agregado verifica directamente la igualdad y
/// bloquea transitar la OC hasta que todos sean reemplazados por valores
/// reales.
/// </para>
///
/// <para>
/// La OC se considera <see cref="OrdenCompra.EsBorradorMinimo"/> mientras
/// cualquiera de los 3 campos siga igual a su sentinel. El método
/// <see cref="OrdenCompra.EnviarAAutorizacion"/> falla con
/// <c>OC_TRANSMITIR_BORRADOR_MINIMO</c> si se intenta transmitir en ese
/// estado.
/// </para>
/// </summary>
public static class OrdenCompraTbdSentinels
{
    public static readonly Guid Proveedor = new("ffffffff-0000-0000-0000-000000000001");
    public static readonly Guid CondicionesPago = new("ffffffff-0000-0000-0000-000000000002");
    public static readonly Guid UsoPrincipal = new("ffffffff-0000-0000-0000-000000000003");
}
