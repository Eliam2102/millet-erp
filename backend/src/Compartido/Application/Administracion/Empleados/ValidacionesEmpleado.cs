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

    /// <summary>Valida que el departamento esté asignado a la sucursal del empleado y activo en ella.</summary>
    internal static async Task ValidarDepartamentoDeSucursalAsync(
        CompartidoDbContext db, Guid sucursalId, Guid departamentoId, CancellationToken ct)
    {
        var deptoSucursal = await db.SucursalDepartamentos.AsNoTracking()
            .Where(sd => sd.SucursalId == sucursalId && sd.DepartamentoId == departamentoId)
            .Select(sd => new { sd.Estatus })
            .FirstOrDefaultAsync(ct);
        if (deptoSucursal is null)
        {
            throw new BusinessRuleException(
                "EMPLEADO_DEPARTAMENTO_NO_ASIGNADO_A_SUCURSAL",
                "El departamento no está asignado a la sucursal del empleado.");
        }
        if (deptoSucursal.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "EMPLEADO_DEPARTAMENTO_SUCURSAL_INACTIVO",
                "El departamento en la sucursal del empleado no está activo.");
        }
    }

    /// <summary>
    /// Valida que el puesto esté asignado a la sucursal del empleado y
    /// activo en ella. Desde F1-ADM-01.4 reabierta (2026-09-24) un mismo
    /// puesto puede estar asignado a varios departamentos de la misma
    /// sucursal (una fila <c>SucursalPuesto</c> por departamento):
    /// <list type="bullet">
    ///   <item>Si viene <paramref name="departamentoId"/>, se busca la
    ///         asignación exacta (sucursal, puesto, departamento). Si no
    ///         existe → <c>EMPLEADO_PUESTO_NO_ASIGNADO_A_SUCURSAL</c>
    ///         (cubre tanto "el puesto no está en la sucursal" como "no
    ///         está en ese departamento").</item>
    ///   <item>Si NO viene <paramref name="departamentoId"/>, se buscan
    ///         todas las asignaciones activas del puesto en la sucursal:
    ///         si hay exactamente una, se usa implícitamente (sin
    ///         error); si hay varias, el llamador debe especificar el
    ///         departamento → 422
    ///         <c>EMPLEADO_DEPARTAMENTO_REQUERIDO_PARA_PUESTO</c>.</item>
    /// </list>
    /// </summary>
    internal static async Task ValidarPuestoDeSucursalAsync(
        CompartidoDbContext db, Guid sucursalId, Guid puestoId, Guid? departamentoId, CancellationToken ct)
    {
        if (departamentoId is Guid deptoEspecificado)
        {
            var asignacion = await db.SucursalPuestos.AsNoTracking()
                .Where(sp => sp.SucursalId == sucursalId
                    && sp.PuestoId == puestoId
                    && sp.DepartamentoId == deptoEspecificado)
                .Select(sp => new { sp.Estatus })
                .FirstOrDefaultAsync(ct);
            if (asignacion is null)
            {
                throw new BusinessRuleException(
                    "EMPLEADO_PUESTO_NO_ASIGNADO_A_SUCURSAL",
                    "El puesto no está asignado a ese departamento en la sucursal del empleado.");
            }
            if (asignacion.Estatus != EstatusCatalogo.Activo)
            {
                throw new BusinessRuleException(
                    "EMPLEADO_PUESTO_SUCURSAL_INACTIVO",
                    "El puesto en la sucursal del empleado no está activo.");
            }
            return;
        }

        var asignaciones = await db.SucursalPuestos.AsNoTracking()
            .Where(sp => sp.SucursalId == sucursalId && sp.PuestoId == puestoId)
            .Select(sp => sp.Estatus)
            .ToListAsync(ct);
        if (asignaciones.Count == 0)
        {
            throw new BusinessRuleException(
                "EMPLEADO_PUESTO_NO_ASIGNADO_A_SUCURSAL",
                "El puesto no está asignado a la sucursal del empleado.");
        }

        var activas = asignaciones.Count(e => e == EstatusCatalogo.Activo);
        if (activas == 0)
        {
            throw new BusinessRuleException(
                "EMPLEADO_PUESTO_SUCURSAL_INACTIVO",
                "El puesto en la sucursal del empleado no está activo.");
        }
        if (activas > 1)
        {
            throw new BusinessRuleException(
                "EMPLEADO_DEPARTAMENTO_REQUERIDO_PARA_PUESTO",
                "El puesto está asignado a varios departamentos en la sucursal del empleado; " +
                "especifica a cuál departamento pertenece.");
        }
        // activas == 1: única asignación activa, se usa implícitamente.
    }
}
