namespace Millet.CuentasPorCobrar.Domain.Ports.Tesoreria;

/// <summary>
/// C7.4 · CA7.9 (regla R13): depósitos bancarios de cliente que llegaron sin
/// detalle (remittance) y siguen «sin identificar». Tesorería es dueña del
/// movimiento bancario; CxC solo lo lee por este puerto (ADR-0042, nunca JOIN
/// a tablas de otro módulo) para proponer la aplicación cuando el cliente manda
/// el detalle. Lo implementa
/// <c>Millet.Tesoreria.Infrastructure.PublicAdapters.DepositosPorIdentificarReadAdapter</c>.
///
/// <para>
/// «Sin identificar» = movimiento de INGRESO, no contramovimiento ni revertido,
/// con beneficiario tipo Cliente (el cliente puede no conocerse), con
/// <see cref="DepositoPorIdentificarDto.Disponible"/> &gt; 0 (monto menos lo ya
/// confirmado) y sin devolución solicitada, autorizada o pagada. Excluye los
/// depósitos de caja y de viáticos.
/// </para>
/// </summary>
public interface IDepositosPorIdentificarReadPort
{
    Task<IReadOnlyList<DepositoPorIdentificarDto>> ListarAsync(CancellationToken cancellationToken);

    /// <summary>El depósito si sigue identificable; <c>null</c> si no existe o ya no está disponible.</summary>
    Task<DepositoPorIdentificarDto?> ObtenerAsync(Guid movimientoBancarioId, CancellationToken cancellationToken);
}

/// <summary>
/// Depósito sin identificar visto desde CxC. <see cref="Disponible"/> es lo que
/// falta por aplicar según Tesorería (el sobrante de una confirmación previa
/// cuenta aquí); CxC descuenta además sus propias propuestas abiertas.
/// </summary>
public sealed record DepositoPorIdentificarDto(
    Guid MovimientoBancarioId,
    Guid CuentaBancariaId,
    string Cuenta,
    DateOnly FechaValor,
    string? Referencia,
    decimal Monto,
    decimal Disponible,
    string Moneda,
    Guid? SucursalId,
    Guid? ClienteId,
    // Propuestas de CxC que Tesorería ya confirmó contra este movimiento (su monto ya
    // está descontado de Disponible). CxC no las vuelve a reservar mientras le llega
    // el evento de confirmación.
    IReadOnlyList<Guid>? PropuestasConfirmadas = null);
