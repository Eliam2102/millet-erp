using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="IEmpleadoReadPort"/> (ADM-PR2,
/// doc 10-catalogo-puestos-empleados). Reemplaza el
/// <c>NoOpEmpleadoReadPort</c> — cierra PLATFORM-TODO
/// &lt;EmpleadoReadPort&gt;.
///
/// <para>Lectura cross-módulo de <c>compartido.empleados</c> via
/// <see cref="CompartidoDbContext"/> con <c>AsNoTracking</c> y
/// <c>Bypass()</c> (el DbContext de catálogos no filtra por empresa; el
/// DTO expone <c>EmpresaId</c> real para que el consumidor decida).</para>
///
/// <para><b>Mapeo a <see cref="EmpleadoDto"/></b>: <c>Email</c> es
/// no-nullable en el contrato — un empleado sin email mapea a
/// <c>string.Empty</c>. <c>Activo</c> = <c>Estatus == Activo</c>.
/// <c>PuestoId</c> alimenta la política de viáticos (A18).</para>
/// </summary>
public sealed class EmpleadoReadPortAdapter : IEmpleadoReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public EmpleadoReadPortAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<EmpleadoDto?> ObtenerAsync(Guid empleadoId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Empleados
            .AsNoTracking()
            .Where(e => e.Id == empleadoId)
            .Select(e => new EmpleadoDto(
                e.Id,
                e.EmpresaId,
                e.Nombre,
                e.Email ?? string.Empty,
                e.PuestoId,
                e.SucursalId,
                e.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<EmpleadoDto?> ObtenerPorUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        // Solo empleados activos: un usuario re-vinculado tras una baja
        // resuelve a su empleado vigente, no al histórico.
        return await _db.Empleados
            .AsNoTracking()
            .Where(e => e.UsuarioId == usuarioId && e.Estatus == EstatusCatalogo.Activo)
            .OrderBy(e => e.Clave)
            .Select(e => new EmpleadoDto(
                e.Id,
                e.EmpresaId,
                e.Nombre,
                e.Email ?? string.Empty,
                e.PuestoId,
                e.SucursalId,
                true))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
