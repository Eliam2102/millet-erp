using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Application.Matriz;

/// <summary>
/// Implementación productiva de <see cref="IResolverAutorizadorPort"/>
/// (F9-PR2). Resuelve el usuario aprobador buscando la fila vigente en
/// <c>compras.aprobadores_departamento</c> con el rol y el scope
/// correspondientes.
///
/// <para>
/// El campo <c>departamento_id</c> de la tabla actúa como <b>scope_id
/// polimórfico</b> según el rol (ver doc-comment en
/// <c>AprobadorDepartamento</c>):
/// </para>
/// <list type="bullet">
///   <item><c>JefeDpto</c> → scope = <c>requisicion.DepartamentoId</c></item>
///   <item><c>JefeAlmacen</c> → scope = <c>requisicion.SucursalId</c> (N2)</item>
/// </list>
/// </summary>
public sealed class ResolverAutorizadorService : IResolverAutorizadorPort
{
    private readonly ComprasDbContext _db;

    public ResolverAutorizadorService(ComprasDbContext db)
    {
        _db = db;
    }

    // C3.1 / F-09: N1 = jefe del departamento del solicitante (toda naturaleza).
    public async Task<Guid?> ResolverN1Async(
        Requisicion requisicion,
        CancellationToken cancellationToken = default) =>
        await ResolverVigenteAsync(
            requisicion.EmpresaId, requisicion.DepartamentoId, RolAprobador.JefeDpto, cancellationToken);

    // C3.1 / F-09: N2 = jefe de almacén de la sucursal (el RQ manual no trae almacén destino).
    public async Task<Guid?> ResolverN2Async(
        Requisicion requisicion,
        CancellationToken cancellationToken = default) =>
        await ResolverVigenteAsync(
            requisicion.EmpresaId, requisicion.SucursalId, RolAprobador.JefeAlmacen, cancellationToken);

    private async Task<Guid?> ResolverVigenteAsync(
        Guid empresaId, Guid scopeId, RolAprobador rol, CancellationToken cancellationToken)
    {
        return await _db.AprobadoresDepartamento
            .AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                     && a.DepartamentoId == scopeId
                     && a.Rol == rol
                     && a.VigenteHasta == null)
            .Select(a => (Guid?)a.UsuarioId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
