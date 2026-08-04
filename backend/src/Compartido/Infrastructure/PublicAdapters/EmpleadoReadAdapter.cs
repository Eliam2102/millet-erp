using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IEmpleadoReadPort"/>
/// declarado en <c>Almacen.Domain.Ports</c> (ADM-PR2,
/// doc 10-catalogo-puestos-empleados). Reemplaza el
/// <c>NoOpEmpleadoReadPort</c> de Almacén — cierra PLATFORM-TODO
/// &lt;EmpleadoReadAdapter&gt;.
///
/// <para>Lectura cross-módulo de <c>compartido.empleados</c> via
/// <see cref="CompartidoDbContext"/> con <c>AsNoTracking</c> y
/// <c>Bypass()</c>. Lo usan los handlers de conteos (responsables) y
/// devoluciones internas (solicitante). La persona destinataria de una
/// salida sigue resolviéndose por <c>IUsuarioReadPort</c> (ADR-0042),
/// no por aquí.</para>
///
/// <para><b>Mapeo a <see cref="EmpleadoLectura"/></b>:
/// <c>NombreCompleto</c> = <c>Empleado.Nombre</c>; <c>EmpresaId</c> es
/// el real del empleado; <c>EsActivo</c> = <c>Estatus == Activo</c>.</para>
/// </summary>
public sealed class EmpleadoReadAdapter : IEmpleadoReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public EmpleadoReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<EmpleadoLectura?> ObtenerAsync(
        Guid empleadoId,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Empleados
            .AsNoTracking()
            .Where(e => e.Id == empleadoId)
            .Select(e => new EmpleadoLectura(
                e.Id,
                e.Nombre,
                e.EmpresaId,
                e.DepartamentoId,
                e.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
