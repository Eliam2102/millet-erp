using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compras.Domain;
using Millet.Compartido.Infrastructure.Persistence;
namespace Millet.Compras.Application;

internal static class CentroCostoRqResolver
{
    public static async Task<Guid> DepartamentoAsync(Requisicion rq, CompartidoDbContext organizacion, CancellationToken ct)
    {
        var empleado = await organizacion.Empleados.AsNoTracking()
            .Where(x => x.EmpresaId == rq.EmpresaId && x.UsuarioId == rq.RequisitanteId && x.Estatus == EstatusCatalogo.Activo)
            .Select(x => new { x.DepartamentoId }).SingleOrDefaultAsync(ct);
        // La RQ ya conserva sucursal/departamento validados al crear. Para usuarios
        // legados sin ficha de empleado y RQs Sistema se usa ese dato organizacional.
        return empleado is null ? rq.DepartamentoId : empleado.DepartamentoId ?? Guid.Empty;
    }
    public static async Task<Guid> ResolverAsync(Requisicion rq, CompartidoDbContext organizacion,
        ICentroCostoCapturaPort captura, Guid? elegido, CancellationToken ct) =>
        await captura.ResolverAsync(rq.SucursalId, await DepartamentoAsync(rq, organizacion, ct), elegido, ct);
}
