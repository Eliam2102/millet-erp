using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Movimientos;

/// <summary>
/// Contador atómico de folios por <c>(prefijo, año)</c> (A2). El
/// handler que pasa un movimiento a <see cref="EstadoMovimiento.Registrado"/>
/// reserva el siguiente número en la misma transacción del INSERT del
/// movimiento — el lock pesimista (`SELECT ... FOR UPDATE` sobre la
/// fila) garantiza secuencias sin huecos por race condition.
///
/// <para>
/// Tabla <c>almacen.folio_secuencias_movimiento</c> con UNIQUE
/// <c>(prefijo, año)</c>. Reinicia por año al primer movimiento del
/// año siguiente — el handler hace upsert (`ON CONFLICT (prefijo, año)
/// DO UPDATE SET ultimo_numero = ultimo_numero + 1`).
/// </para>
/// </summary>
public sealed class FolioSecuenciaMovimiento : BaseEntity
{
    public string Prefijo { get; private set; } = string.Empty;
    public int Anio { get; private set; }
    public int UltimoNumero { get; private set; }

    private FolioSecuenciaMovimiento() { }

    public FolioSecuenciaMovimiento(
        Guid id,
        string prefijo,
        int anio,
        int ultimoNumero = 0) : base(id)
    {
        Prefijo = prefijo;
        Anio = anio;
        UltimoNumero = ultimoNumero;
    }

    /// <summary>
    /// Incrementa y retorna el siguiente número. Debe llamarse dentro
    /// de una transacción con la fila bloqueada (`FOR UPDATE`).
    /// </summary>
    public int Incrementar()
    {
        UltimoNumero += 1;
        return UltimoNumero;
    }
}
