namespace Millet.Compras.Domain;

/// <summary>
/// Estados del ciclo de vida de una <see cref="Requisicion"/>. State machine
/// del diseño §5: 8 estados base + 2 terminales de cierre manual (ADR-0043) =
/// 10. "AutorizadaParcial" / "AutorizadaCompleta" se derivan de las
/// autorizaciones registradas; "EnSurtido" / "Surtida" se derivan del
/// cubrimiento de las líneas.
///
/// Persistido como <c>smallint</c> con CHECK constraint
/// (<c>estado BETWEEN 0 AND 9</c>); ver diseño §10.2.1 sobre enums en
/// código vs tabla de lookup.
/// </summary>
public enum EstadoRequisicion : short
{
    Borrador = 0,
    EnAutorizacion = 1,
    Autorizada = 2,
    EnSurtido = 3,
    Cerrada = 4,
    Cancelada = 5,
    Rechazada = 6,
    Eliminada = 7,

    /// <summary>
    /// Cierre administrativo del jefe de almacén (ADR-0043 R3): la RQ se
    /// cerró sin haber entregado nada al solicitante. Terminal. Aditivo en
    /// el PR #1 (nadie transiciona aquí todavía); el PR #2 lo hace alcanzable.
    /// </summary>
    CerradaSinSurtir = 8,

    /// <summary>
    /// Cierre administrativo del jefe de almacén (ADR-0043 R3): se entregó
    /// una parte al solicitante y el resto ya no se entregará. Terminal.
    /// Aditivo en el PR #1; el PR #2 lo hace alcanzable.
    /// </summary>
    CerradaSurtidaParcial = 9,
}
