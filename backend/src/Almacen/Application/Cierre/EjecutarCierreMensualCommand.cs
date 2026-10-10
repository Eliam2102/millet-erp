using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Cierre;

/// <summary>
/// Cierra un periodo mensual del módulo Almacén (F8-PR2). Validaciones
/// pre-cierre (cuidado §6.2 del 04-cuidados-infra):
/// <list type="bullet">
///   <item>No hay conteos pendientes iniciados o planificados hasta el fin del mes
///   (deben aplicarse o rechazarse antes).</item>
///   <item>No hay movimientos Borrador/Validado con fecha del mes
///   (deben firmarse o cancelarse).</item>
/// </list>
///
/// <para>
/// Tras cerrar, todos los handlers de movimiento rechazan operaciones
/// con <c>fecha_movimiento</c> dentro del periodo cerrado.
/// </para>
/// </summary>
public sealed record EjecutarCierreMensualCommand(int Anio, int Mes) : IRequest<EjecutarCierreMensualResponse>;

public sealed record EjecutarCierreMensualResponse(Guid PeriodoId, DateTimeOffset CerradoAt);

public sealed class EjecutarCierreMensualValidator : AbstractValidator<EjecutarCierreMensualCommand>
{
    public EjecutarCierreMensualValidator()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2020, 2099);
        RuleFor(c => c.Mes).InclusiveBetween(1, 12);
    }
}

public sealed class EjecutarCierreMensualHandler
    : IRequestHandler<EjecutarCierreMensualCommand, EjecutarCierreMensualResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public EjecutarCierreMensualHandler(
        AlmacenDbContext db,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentUser = currentUser; _currentEmpresa = currentEmpresa;
    }

    public async Task<EjecutarCierreMensualResponse> Handle(
        EjecutarCierreMensualCommand request, CancellationToken cancellationToken)
    {
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "CIERRE_SIN_EMPRESA", "Contexto de empresa requerido.");
        var cerradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "CIERRE_SIN_USUARIO", "Se requiere usuario autenticado.");

        // Un periodo ya cerrado se rechaza explícitamente.
        var existe = await _db.Set<PeriodoCerrado>().AsNoTracking()
            .AnyAsync(p => p.EmpresaId == empresaId
                && p.Anio == request.Anio
                && p.Mes == request.Mes, cancellationToken);
        if (existe)
        {
            throw new BusinessRuleException(
                "PERIODO_YA_CERRADO",
                $"El periodo {request.Anio}/{request.Mes:00} ya está cerrado.");
        }

        // Validación: no conteos pendientes con fecha del mes.
        var inicioMes = new DateOnly(request.Anio, request.Mes, 1);
        var finMes = inicioMes.AddMonths(1).AddDays(-1);
        var finMesUtc = new DateTimeOffset(inicioMes.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var conteosPendientes = await _db.Set<ConteoInventario>().AsNoTracking()
            .Where(c => (c.FechaPlanificada <= finMes || c.FechaInicio < finMesUtc)
                && c.Estado != EstadoConteo.Aplicado
                && c.Estado != EstadoConteo.Rechazado)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (conteosPendientes.Count > 0)
        {
            throw new BusinessRuleException(
                "CIERRE_CONTEOS_PENDIENTES",
                $"No se puede cerrar: {conteosPendientes.Count} conteo(s) pendientes que afectan al mes. " +
                $"Aplicar o rechazar antes: {string.Join(", ", conteosPendientes.Take(5))}.");
        }

        if (await _db.Movimientos.AsNoTracking().AnyAsync(m =>
            m.FechaMovimiento >= inicioMes && m.FechaMovimiento <= finMes
            && m.Tipo == TipoMovimiento.SalidaPorVale && m.PendienteRegularizacion
            && m.Estado == EstadoMovimiento.Registrado, cancellationToken))
            throw new BusinessRuleException("CIERRE_VALES_PENDIENTES", "No se puede cerrar el mes: hay vales urgentes pendientes de regularizar con una requisición.");

        // Validación: no movimientos Borrador/Validado con fecha del mes.
        var movimientosPendientes = await _db.Movimientos.AsNoTracking()
            .Where(m => m.FechaMovimiento >= inicioMes
                && m.FechaMovimiento <= finMes
                && (m.Estado == EstadoMovimiento.Borrador
                    || m.Estado == EstadoMovimiento.Validado))
            .CountAsync(cancellationToken);
        if (movimientosPendientes > 0)
        {
            throw new BusinessRuleException(
                "CIERRE_MOVIMIENTOS_PENDIENTES",
                $"No se puede cerrar: {movimientosPendientes} movimiento(s) en Borrador/Validado del mes. " +
                "Firmar o cancelar antes.");
        }

        var periodo = new PeriodoCerrado(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            anio: request.Anio,
            mes: request.Mes,
            cerradoPor: cerradoPor);
        _db.Set<PeriodoCerrado>().Add(periodo);
        await _db.SaveChangesAsync(cancellationToken);

        return new EjecutarCierreMensualResponse(periodo.Id, periodo.CerradoAt);
    }
}

/// <summary>
/// Servicio compartido para validar periodo cerrado desde TODOS los
/// handlers de movimiento (cuidado §6.1 del 04-cuidados-infra).
/// </summary>
public static class PeriodoCerradoValidator
{
    /// <summary>Los dos candados son independientes; abrir Contabilidad no elimina el cierre de inventario (D18).</summary>
    public static async Task LanzarSiCerradoAsync(
        AlmacenDbContext db,
        Guid empresaId,
        DateOnly fechaMovimiento,
        IPeriodoContableReadPort periodoContable,
        CancellationToken cancellationToken)
    {
        await LanzarSiCerradoAsync(db, empresaId, fechaMovimiento, cancellationToken);
        if (!await periodoContable.EstaAbiertoAsync(fechaMovimiento.Year, fechaMovimiento.Month, cancellationToken))
        {
            throw new BusinessRuleException(
                "PERIODO_CONTABLE_NO_ADMITE",
                $"El periodo {fechaMovimiento.Year:0000}-{fechaMovimiento.Month:00} está cerrado o no está abierto en Contabilidad; " +
                "no se registran movimientos de almacén con esa fecha.");
        }
    }

    public static async Task LanzarSiCerradoAsync(
        AlmacenDbContext db,
        Guid empresaId,
        DateOnly fechaMovimiento,
        CancellationToken cancellationToken)
    {
        var cerrado = await db.Set<PeriodoCerrado>().AsNoTracking()
            .AnyAsync(p => p.EmpresaId == empresaId
                && p.Anio == fechaMovimiento.Year
                && p.Mes == fechaMovimiento.Month, cancellationToken);
        if (cerrado)
        {
            throw new BusinessRuleException(
                "PERIODO_CERRADO",
                $"El periodo {fechaMovimiento.Year}/{fechaMovimiento.Month:00} está cerrado. " +
                "No se aceptan movimientos con fecha del mes cerrado.");
        }
    }
}
