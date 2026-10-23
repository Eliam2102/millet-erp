using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.DatosMaestros.Application.Clientes;
internal static class ClasificacionClienteGuard
{
    // D7: la clasificación ya asignada al cliente (actual) no exige estar activa; solo el cambio o el alta la exigen.
    public static async Task ValidarAsync(CompartidoDbContext db, Guid? id, Guid? actual, CancellationToken ct)
    {
        if (id is null) throw new BusinessRuleException("CLIENTE_CLASIFICACION_REQUERIDA", "Elige una clasificación activa para guardar el cliente.");
        if (id == actual) return; // la FK garantiza que existe
        if (!await db.Set<Millet.DatosMaestros.Domain.ClasificacionCliente>().AnyAsync(c => c.Id == id && c.Activa, ct))
            throw new BusinessRuleException("CLIENTE_CLASIFICACION_REQUERIDA", "Elige una clasificación activa para guardar el cliente.");
    }
}
public sealed record ClienteExistente(Guid Id, string Clave, string RazonSocial);
public sealed class ClienteRfcDuplicadoException(IReadOnlyList<ClienteExistente> clientes)
    : BusinessRuleException("CLIENTE_RFC_DUPLICADO", "Ya existe un cliente con ese RFC. Usa el cliente existente.")
{
    public IReadOnlyList<ClienteExistente> Clientes { get; } = clientes;
}
