using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Conciliaciones.Actas;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Conciliaciones;

// ============================================================================
// 1. ListarConciliacionesQuery
// ============================================================================
public sealed record ListarConciliacionesQuery(
    Guid? CuentaBancariaId = null,
    int? Anio = null,
    int? Mes = null,
    EstadoConciliacion? Estado = null) : IRequest<IReadOnlyList<ConciliacionResumenResponse>>;

public sealed class ListarConciliacionesHandler(TesoreriaDbContext db)
    : IRequestHandler<ListarConciliacionesQuery, IReadOnlyList<ConciliacionResumenResponse>>
{
    public async Task<IReadOnlyList<ConciliacionResumenResponse>> Handle(
        ListarConciliacionesQuery query, CancellationToken cancellationToken)
    {
        var q = db.Conciliaciones
            .AsNoTracking()
            .Include(c => c.Lineas)
            .AsQueryable();

        if (query.CuentaBancariaId.HasValue)
            q = q.Where(c => c.CuentaBancariaId == query.CuentaBancariaId.Value);

        if (query.Anio.HasValue)
            q = q.Where(c => c.PeriodoAnio == query.Anio.Value);

        if (query.Mes.HasValue)
            q = q.Where(c => c.PeriodoMes == query.Mes.Value);

        if (query.Estado.HasValue)
            q = q.Where(c => c.Estado == query.Estado.Value);

        var conciliaciones = await q
            .OrderByDescending(c => c.PeriodoAnio)
            .ThenByDescending(c => c.PeriodoMes)
            .ToListAsync(cancellationToken);

        var cuentaIds = conciliaciones.Select(c => c.CuentaBancariaId).Distinct().ToList();
        var cuentas = await db.CuentasBancarias
            .AsNoTracking()
            .Where(c => cuentaIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return conciliaciones.Select(c =>
        {
            var cuenta = cuentas.GetValueOrDefault(c.CuentaBancariaId);
            string banco = cuenta?.Banco ?? "Desconocido";
            string numero = cuenta != null
                ? Millet.SharedKernel.Domain.Bancos.NumeroCuenta.Crear(cuenta.NumeroCuenta).Enmascarado
                : "****";
            string moneda = cuenta?.Moneda ?? "MXN";

            return new ConciliacionResumenResponse(
                c.Id,
                c.CuentaBancariaId,
                banco,
                numero,
                moneda,
                c.PeriodoAnio,
                c.PeriodoMes,
                c.SaldoExtracto,
                c.SaldoLibros,
                c.Diferencia,
                c.Estado,
                c.Lineas.Count,
                c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.Emparejada),
                c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.Pendiente),
                c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.EnRevision),
                c.Lineas.Count(l => l.Estado == EstadoExtractoLinea.EnTransito),
                c.CerradaEn,
                c.FirmanteNombre);
        }).ToList();
    }
}

// ============================================================================
// 2. ObtenerConciliacionDetalleQuery
// ============================================================================
public sealed record ObtenerConciliacionDetalleQuery(Guid Id) : IRequest<ConciliacionDetalleResponse>;

public sealed class ObtenerConciliacionDetalleHandler(TesoreriaDbContext db)
    : IRequestHandler<ObtenerConciliacionDetalleQuery, ConciliacionDetalleResponse>
{
    public async Task<ConciliacionDetalleResponse> Handle(
        ObtenerConciliacionDetalleQuery query, CancellationToken cancellationToken)
    {
        var c = await db.Conciliaciones
            .AsNoTracking()
            .Include(x => x.Lineas.OrderBy(l => l.NumeroLinea))
                .ThenInclude(l => l.Matches)
            .Include(x => x.Bitacora.OrderByDescending(b => b.Fecha))
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        var cuenta = await db.CuentasBancarias
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == c.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        var fechaInicio = new DateOnly(c.PeriodoAnio, c.PeriodoMes, 1);
        var fechaFin = new DateOnly(c.PeriodoAnio, c.PeriodoMes, DateTime.DaysInMonth(c.PeriodoAnio, c.PeriodoMes));

        // Movimientos del periodo para la vista de matching en dos columnas
        var movimientos = await db.MovimientosBancarios
            .AsNoTracking()
            .Where(m => m.CuentaBancariaId == c.CuentaBancariaId
                        && m.FechaValor >= fechaInicio
                        && m.FechaValor <= fechaFin)
            .OrderBy(m => m.FechaValor)
            .ToListAsync(cancellationToken);

        var conceptoIds = movimientos.Where(m => m.ConceptoId.HasValue).Select(m => m.ConceptoId!.Value).Distinct().ToList();
        var conceptos = await db.ConceptosMovimiento
            .AsNoTracking()
            .Where(x => conceptoIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);

        var movDtos = movimientos.Select(m => new MovimientoBancarioConciliacionDto(
            m.Id,
            m.FechaValor,
            m.Sentido,
            m.Monto,
            m.Moneda,
            m.ReferenciaBancaria,
            m.ConceptoId,
            m.ConceptoId.HasValue ? conceptos.GetValueOrDefault(m.ConceptoId.Value) : null,
            m.EstadoConciliacion)).ToList();

        var bitacoraDtos = c.Bitacora.Select(b => new BitacoraConciliacionResponse(
            b.Id,
            b.ExtractoLineaId,
            b.MovimientoBancarioId,
            b.Accion,
            b.Motivo,
            b.UsuarioId,
            b.Fecha)).ToList();

        var lineasDtos = c.Lineas.Select(ConciliacionMapper.ToLineaResponse).ToList();

        return new ConciliacionDetalleResponse(
            c.Id,
            c.CuentaBancariaId,
            cuenta.Banco,
            Millet.SharedKernel.Domain.Bancos.NumeroCuenta.Crear(cuenta.NumeroCuenta).Enmascarado,
            cuenta.Moneda,
            c.PeriodoAnio,
            c.PeriodoMes,
            c.SaldoExtracto,
            c.SaldoLibros,
            c.Diferencia,
            c.Estado,
            c.CerradaPor,
            c.CerradaEn,
            c.MotivoReapertura,
            c.FirmanteNombre,
            c.FirmantePuesto,
            c.ActaGeneradaEn,
            lineasDtos,
            bitacoraDtos,
            movDtos);
    }
}

// ============================================================================
// 3. DescargarActaConciliacionQuery
// ============================================================================
public sealed record DescargarActaConciliacionQuery(Guid Id) : IRequest<byte[]>;

public sealed class DescargarActaConciliacionHandler(TesoreriaDbContext db)
    : IRequestHandler<DescargarActaConciliacionQuery, byte[]>
{
    public async Task<byte[]> Handle(DescargarActaConciliacionQuery query, CancellationToken cancellationToken)
    {
        var conciliacion = await db.Conciliaciones
            .AsNoTracking()
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONCILIACION_NO_ENCONTRADA", "No se encontró la conciliación.");

        if (conciliacion.Estado != EstadoConciliacion.Cerrada)
            throw new BusinessRuleException("CONCILIACION_NO_CERRADA", "El acta solo está disponible para conciliaciones cerradas.");

        var cuenta = await db.CuentasBancarias
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conciliacion.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CUENTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");

        var partidasTransito = conciliacion.Lineas
            .Where(l => l.PartidaTransito)
            .Select(l => new DatosPartidaTransito(
                l.Fecha,
                l.Descripcion,
                l.Referencia,
                l.Monto,
                l.Sentido.ToString(),
                l.PartidaTransitoTipo?.ToString() ?? "Otro",
                l.PartidaTransitoResponsable ?? "No asignado"))
            .ToList();

        var datosActa = new DatosActaConciliacion(
            conciliacion.Id,
            "Vidrios Millet de México S.A. de C.V.",
            "VMI900101XYZ",
            cuenta.Banco,
            Millet.SharedKernel.Domain.Bancos.NumeroCuenta.Crear(cuenta.NumeroCuenta).Enmascarado,
            cuenta.Moneda,
            conciliacion.PeriodoAnio,
            conciliacion.PeriodoMes,
            conciliacion.SaldoExtracto,
            conciliacion.SaldoLibros,
            conciliacion.Lineas.Where(l => l.Cargo.HasValue).Sum(l => l.Cargo!.Value),
            conciliacion.Lineas.Where(l => l.Abono.HasValue).Sum(l => l.Abono!.Value),
            conciliacion.Lineas.Count,
            conciliacion.Lineas.Count(l => l.Estado == EstadoExtractoLinea.Emparejada),
            partidasTransito,
            conciliacion.FirmanteNombre ?? "Gerente de Tesorería",
            conciliacion.FirmantePuesto ?? "Gerente de Tesorería",
            conciliacion.CerradaEn ?? DateTimeOffset.UtcNow);

        return ActaConciliacionPdfGenerator.Generar(datosActa);
    }
}
