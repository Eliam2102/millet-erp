using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empleados;

/// <summary>
/// Validaciones cross-entity compartidas por los handlers de Empleado
/// (ADM-PR1). Las FKs físicas son la red final; estos checks devuelven
/// Problem Details legibles en vez de un error de constraint.
/// </summary>
internal static class ValidacionesEmpleado
{
    /// <summary>Puesto referenciado debe existir y estar activo.</summary>
    internal static async Task ValidarPuestoAsync(
        CompartidoDbContext db, Guid puestoId, CancellationToken ct)
    {
        var estatus = await db.Puestos.AsNoTracking()
            .Where(p => p.Id == puestoId)
            .Select(p => (EstatusCatalogo?)p.Estatus)
            .FirstOrDefaultAsync(ct);
        if (estatus is null)
        {
            throw new BusinessRuleException(
                "EMPLEADO_PUESTO_NO_EXISTE",
                $"No existe el puesto '{puestoId}'.");
        }
        if (estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "EMPLEADO_PUESTO_INACTIVO",
                $"El puesto '{puestoId}' no está activo.");
        }
    }

    /// <summary>Jefe directo debe existir y ser de la misma empresa.</summary>
    internal static async Task ValidarJefeDirectoAsync(
        CompartidoDbContext db, Guid jefeDirectoId, Guid empresaId, CancellationToken ct)
    {
        var empresaJefe = await db.Empleados.AsNoTracking()
            .Where(e => e.Id == jefeDirectoId)
            .Select(e => (Guid?)e.EmpresaId)
            .FirstOrDefaultAsync(ct);
        if (empresaJefe is null)
        {
            throw new BusinessRuleException(
                "EMPLEADO_JEFE_NO_EXISTE",
                $"No existe el empleado jefe directo '{jefeDirectoId}'.");
        }
        if (empresaJefe != empresaId)
        {
            throw new BusinessRuleException(
                "EMPLEADO_JEFE_OTRA_EMPRESA",
                "El jefe directo debe pertenecer a la misma empresa.");
        }
    }

    internal static async Task ValidarSucursalAsync(
        CompartidoDbContext db, Guid sucursalId, CancellationToken ct)
    {
        var existe = await db.Sucursales.AsNoTracking()
            .AnyAsync(s => s.Id == sucursalId, ct);
        if (!existe)
        {
            throw new BusinessRuleException(
                "EMPLEADO_SUCURSAL_NO_EXISTE",
                $"No existe la sucursal '{sucursalId}'.");
        }
    }

    internal static async Task ValidarDepartamentoAsync(
        CompartidoDbContext db, Guid departamentoId, CancellationToken ct)
    {
        var existe = await db.Departamentos.AsNoTracking()
            .AnyAsync(d => d.Id == departamentoId, ct);
        if (!existe)
        {
            throw new BusinessRuleException(
                "EMPLEADO_DEPARTAMENTO_NO_EXISTE",
                $"No existe el departamento '{departamentoId}'.");
        }
    }
}
