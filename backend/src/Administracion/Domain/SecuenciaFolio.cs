using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Contador secuencial atómico de folios para una <see cref="Serie"/>
/// dentro de una clave de período (F-Admin-PR6.1).
///
/// <para>
/// Una fila por (<see cref="SerieId"/>, <see cref="PeriodoClave"/>). Con
/// <see cref="ReinicioPeriodo.None"/> la <see cref="PeriodoClave"/> es
/// string vacío y existe una sola fila por serie; con Anual una fila por
/// año; con Mensual una fila por (año, mes).
/// </para>
///
/// <para>
/// La reserva atómica se hace en <c>ReservarFolioCommand</c> mediante
/// SELECT ... FOR UPDATE + UPDATE + RETURNING dentro de la transacción
/// del handler. El método <see cref="ReservarSiguiente"/> de dominio
/// modela la transición conceptual (estado: incrementar contador) y se
/// usa cuando ya cargamos la fila bloqueada en EF.
/// </para>
/// </summary>
public sealed class SecuenciaFolio : BaseEntity, IAuditable
{
    public Guid SerieId { get; private set; }
    public string PeriodoClave { get; private set; } = string.Empty;
    public long UltimoNumero { get; private set; }

    private SecuenciaFolio() { } // EF Core

    public SecuenciaFolio(
        Guid id,
        Guid serieId,
        string periodoClave,
        long ultimoNumero = 0) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("SECUENCIA_FOLIO_ID_INVALIDO",
                "El id es obligatorio.");
        if (serieId == Guid.Empty)
            throw new BusinessRuleException("SECUENCIA_FOLIO_SERIE_INVALIDA",
                "SerieId es obligatorio.");
        if (ultimoNumero < 0)
            throw new BusinessRuleException("SECUENCIA_FOLIO_NUMERO_INVALIDO",
                "UltimoNumero no puede ser negativo.");

        SerieId = serieId;
        PeriodoClave = periodoClave ?? string.Empty;
        UltimoNumero = ultimoNumero;
    }

    /// <summary>
    /// Incrementa el contador en 1 y devuelve el nuevo número reservado.
    /// El llamador es responsable de garantizar atomicidad (typicamente
    /// un SELECT FOR UPDATE previo + SaveChanges + commit).
    /// </summary>
    public long ReservarSiguiente()
    {
        UltimoNumero += 1;
        return UltimoNumero;
    }
}
