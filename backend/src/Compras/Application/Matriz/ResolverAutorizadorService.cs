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
///   <item><c>JefeAlmacen</c> → scope = <c>requisicion.AlmacenDestinoId</c></item>
///   <item><c>AutorizadorN2</c> → scope = <c>requisicion.SucursalId</c></item>
/// </list>
/// </summary>
public sealed class ResolverAutorizadorService : IResolverAutorizadorPort
{
    private readonly ComprasDbContext _db;

    public ResolverAutorizadorService(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> ResolverN1Async(
        Requisicion requisicion,
        Naturaleza naturaleza,
        CancellationToken cancellationToken = default)
    {
        // Estandar → JefeAlmacen del almacén destino.
        // Servicio/Critico/Riesgo → JefeDpto del departamento del solicitante.
        // PLATFORM-TODO(<MatrizN1AlmacenNullable>): cuando se cablee la matriz
        // N1 (hoy este servicio es dead code — registrado en DI sin invocadores),
        // resolver el JefeAlmacen de otra forma para RQs manuales, que post-PR3
        // tienen AlmacenDestinoId = NULL (el requisitante ya no captura almacén):
        // p.ej. por sucursal, o descartar la ruta Estandar. El `?? Guid.Empty`
        // solo evita el NRE del compile; con scope Guid.Empty no resolvería
        // aprobador (WHERE departamento_id = Guid.Empty → null).
        var (rol, scopeId) = naturaleza == Naturaleza.Estandar
            ? (RolAprobador.JefeAlmacen, requisicion.AlmacenDestinoId ?? Guid.Empty)
            : (RolAprobador.JefeDpto, requisicion.DepartamentoId);

        return await ResolverVigenteAsync(requisicion.EmpresaId, scopeId, rol, cancellationToken);
    }

    public async Task<Guid?> ResolverN2Async(
        Requisicion requisicion,
        Naturaleza naturaleza,
        CancellationToken cancellationToken = default)
    {
        // En MVP: AutorizadorN2 por sucursal (no especialización por
        // naturaleza). Cuando el cliente afine "Riesgo químico → gerente
        // de seguridad", agregamos un segundo lookup específico.
        _ = naturaleza;

        return await ResolverVigenteAsync(
            requisicion.EmpresaId,
            requisicion.SucursalId,
            RolAprobador.AutorizadorN2,
            cancellationToken);
    }

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
