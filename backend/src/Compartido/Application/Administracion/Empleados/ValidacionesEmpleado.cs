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
        CompartidoDbContext db, Guid puestoId, Guid empresaId, CancellationToken ct)
    {
        var puesto = await db.Puestos.AsNoTracking()
            .Where(p => p.Id == puestoId)
            .Select(p => new { p.EmpresaId, p.Estatus })
            .FirstOrDefaultAsync(ct);
        if (puesto is null)
        {
            throw new BusinessRuleException(
                "EMPLEADO_PUESTO_NO_EXISTE",
                $"No existe el puesto '{puestoId}'.");
        }
        if (puesto.EmpresaId != empresaId)
            throw new BusinessRuleException("EMPLEADO_PUESTO_OTRA_EMPRESA", "El puesto debe pertenecer a la misma empresa.");
        if (puesto.Estatus != EstatusCatalogo.Activo)
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
        CompartidoDbContext db, Guid sucursalId, Guid empresaId, CancellationToken ct)
    {
        var sucursal = await db.Sucursales.AsNoTracking()
            .Where(s => s.Id == sucursalId)
            .Select(s => new { s.EmpresaId, s.Estatus })
            .FirstOrDefaultAsync(ct);
        if (sucursal is null)
        {
            throw new BusinessRuleException(
                "EMPLEADO_SUCURSAL_NO_EXISTE",
                $"No existe la sucursal '{sucursalId}'.");
        }
        if (sucursal.EmpresaId != empresaId)
            throw new BusinessRuleException("EMPLEADO_SUCURSAL_OTRA_EMPRESA", "La sucursal debe pertenecer a la misma empresa.");
        if (sucursal.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException("EMPLEADO_SUCURSAL_INACTIVA", "La sucursal no está activa.");
    }

    internal static async Task ValidarDepartamentoAsync(
        CompartidoDbContext db, Guid departamentoId, Guid empresaId, CancellationToken ct)
    {
        var departamento = await db.Departamentos.AsNoTracking()
            .Where(d => d.Id == departamentoId)
            .Select(d => new { d.EmpresaId, d.Estatus })
            .FirstOrDefaultAsync(ct);
        if (departamento is null)
        {
            throw new BusinessRuleException(
                "EMPLEADO_DEPARTAMENTO_NO_EXISTE",
                $"No existe el departamento '{departamentoId}'.");
        }
        if (departamento.EmpresaId != empresaId)
            throw new BusinessRuleException("EMPLEADO_DEPARTAMENTO_OTRA_EMPRESA", "El departamento debe pertenecer a la misma empresa.");
        if (departamento.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException("EMPLEADO_DEPARTAMENTO_INACTIVO", "El departamento no está activo.");
    }
}
