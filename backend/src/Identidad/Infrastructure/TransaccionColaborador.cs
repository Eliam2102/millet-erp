using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Identidad.Infrastructure;

/// <summary>
/// Unidad de trabajo del alta de colaborador (plan 15, §5.4): une
/// <see cref="CompartidoDbContext"/> a la conexión y la transacción de
/// <see cref="IdentidadDbContext"/> para que Usuario, Empleado, rol,
/// sucursal y su auditoría hagan commit o rollback juntos.
///
/// <para>
/// Excepción acotada a ADR-0030 (un DbContext por módulo, sin
/// transacciones entre contextos) validada en el spike F0
/// (<c>AltaColaboradorTransaccionSpikeTests</c>); requiere que ambos
/// contextos usen la misma cadena de conexión. Solo para alta, baja y
/// "dar acceso" de colaborador — pendiente de ADR propio (F2½).
/// </para>
///
/// Scoped: comparte las instancias de DbContext del request, las mismas
/// que reciben los handlers que el orquestador invoca por MediatR.
/// </summary>
public sealed class TransaccionColaborador
{
    private readonly IdentidadDbContext _identidad;
    private readonly CompartidoDbContext _compartido;

    public TransaccionColaborador(IdentidadDbContext identidad, CompartidoDbContext compartido)
    {
        _identidad = identidad;
        _compartido = compartido;
    }

    /// <summary>
    /// Ejecuta <paramref name="operacion"/> dentro de la transacción
    /// compartida: commit si termina, rollback si lanza. Al salir, Compartido
    /// queda desenganchado de la transacción.
    /// </summary>
    public async Task<T> EjecutarAsync<T>(Func<Task<T>> operacion, CancellationToken ct)
    {
        await using var tx = await _identidad.Database.BeginTransactionAsync(ct);
        // Compartido no debe tener su propia conexión abierta al unirse (F0 §4.4).
        await _compartido.Database.CloseConnectionAsync();
        _compartido.Database.SetDbConnection(_identidad.Database.GetDbConnection());
        await _compartido.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        try
        {
            var resultado = await operacion();
            await tx.CommitAsync(ct);
            return resultado;
        }
        finally
        {
            // Sin commit, el dispose de tx hace rollback.
            await _compartido.Database.UseTransactionAsync(null, CancellationToken.None);
        }
    }
}
