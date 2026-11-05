using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Periodos;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Revaluacion;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.Application.Revaluacion;

public sealed record RevaluacionOmitida(Guid FacturaId, string Documento, string Motivo);

public sealed record RevaluacionCierreResumen(Guid Id, int Anio, int Mes, DateOnly FechaCierre, DateOnly FechaReversa, int Documentos,
    decimal TotalPerdida, decimal TotalUtilidad, IReadOnlyList<RevaluacionOmitida> Omitidas);

/// <summary>
/// CXP-12 · cierre de mes: revalúa cada factura abierta en moneda extranjera con el TC del último día del mes (diferencia no realizada
/// contra el TC de la factura) y publica un evento por proveedor y sucursal para la póliza. La reversa del día 1 la publica
/// <see cref="PublicarReversasRevaluacionHandler"/>. Una sola vez por mes y empresa.
/// </summary>
public sealed record EjecutarRevaluacionCierreCommand(int Anio, int Mes) : IRequest<RevaluacionCierreResumen>;

public sealed class EjecutarRevaluacionCierreValidator : AbstractValidator<EjecutarRevaluacionCierreCommand>
{
    public EjecutarRevaluacionCierreValidator()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2000, 2100).WithMessage("Año inválido.");
        RuleFor(c => c.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
    }
}

public sealed class EjecutarRevaluacionCierreHandler(CuentasPorPagarDbContext db, ICurrentEmpresaContext empresa, IClock clock,
    ITipoCambioReadPort tiposCambio, IIntegrationEventPublisher publisher) : IRequestHandler<EjecutarRevaluacionCierreCommand, RevaluacionCierreResumen>
{
    public const string Mxn = "MXN";

    public async Task<RevaluacionCierreResumen> Handle(EjecutarRevaluacionCierreCommand c, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(RevaluacionCierre));
        var cierre = new DateOnly(c.Anio, c.Mes, DateTime.DaysInMonth(c.Anio, c.Mes));
        if (cierre > FechaNegocio.Hoy(clock.UtcNow))
            throw new BusinessRuleException("REVALUACION_MES_NO_CERRADO", "La revaluación se corre al cierre: el último día del mes aún no llega.");
        if (await db.RevaluacionesCierre.AnyAsync(r => r.Anio == c.Anio && r.Mes == c.Mes, cancellationToken))
            throw new ConflictException("REVALUACION_YA_EJECUTADA", $"El mes {c.Mes:00}/{c.Anio} ya se revaluó.");

        var fin = SaldosHistoricos.FinExclusivo(cierre);
        var facturas = await db.FacturasProveedor.AsNoTracking().Include(f => f.Movimientos)
            .Where(f => f.Moneda != Mxn && f.FechaDocumento < fin && (f.FechaCancelacion == null || f.FechaCancelacion >= fin))
            .OrderBy(f => f.FechaDocumento).ToListAsync(cancellationToken);

        var detalles = new List<RevaluacionDetalle>();
        var omitidas = new List<RevaluacionOmitida>();
        var tcCierre = new Dictionary<string, decimal>();
        foreach (var f in facturas)
        {
            SaldosHistoricos.VerificarHistorial(f);
            // Pagada (o aplicada del todo) antes del cierre: sin saldo, no se revalúa (CXP-12-b).
            var saldo = f.Total - f.Movimientos.Where(m => m.Fecha <= cierre).Sum(m => m.Monto);
            if (saldo <= 0) continue;
            if (f.TipoCambio is not > 0)
            {
                omitidas.Add(new(f.Id, Documento(f), "La factura no tiene tipo de cambio de registro."));
                continue;
            }
            if (!tcCierre.TryGetValue(f.Moneda, out var tc))
            {
                tc = await tiposCambio.ObtenerAsync(f.Moneda, Mxn, cierre, cancellationToken) ??
                    throw new BusinessRuleException("REVALUACION_SIN_TC_CIERRE",
                        $"Falta el tipo de cambio de {f.Moneda} del {cierre:dd/MM/yyyy}. Cárgalo en el catálogo y vuelve a correr la revaluación.");
                tcCierre[f.Moneda] = tc;
            }
            detalles.Add(new(f.Id, f.ProveedorId, f.SucursalId, f.Moneda, saldo, f.TipoCambio.Value, tc));
        }

        var revaluacion = new RevaluacionCierre(empresaId, c.Anio, c.Mes, detalles);
        db.RevaluacionesCierre.Add(revaluacion);
        foreach (var (proveedorId, sucursalId, neto) in NetoPorProveedorYSucursal(detalles))
            await publisher.PublishAsync(new RevaluacionCierreRegistradaIntegrationEvent(empresaId, clock.UtcNow, revaluacion.Id, proveedorId,
                sucursalId, cierre, Math.Max(neto, 0), Math.Max(-neto, 0)), cancellationToken);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new ConflictException("REVALUACION_YA_EJECUTADA", $"El mes {c.Mes:00}/{c.Anio} ya se revaluó.");
        }
        return new(revaluacion.Id, c.Anio, c.Mes, revaluacion.FechaCierre, revaluacion.FechaReversa, detalles.Count,
            revaluacion.TotalPerdida, revaluacion.TotalUtilidad, omitidas);
    }

    /// <summary>Diferencia neta por proveedor y sucursal (todas sus monedas); lo que queda en cero no genera póliza.</summary>
    internal static IEnumerable<(Guid ProveedorId, Guid SucursalId, decimal Neto)> NetoPorProveedorYSucursal(IEnumerable<RevaluacionDetalle> detalles) =>
        detalles.GroupBy(d => (d.ProveedorId, d.SucursalId)).Select(g => (g.Key.ProveedorId, g.Key.SucursalId, g.Sum(d => d.Diferencia)))
            .Where(x => x.Item3 != 0);

    private static string Documento(Factura f) => string.Join('-', new[] { f.SerieProveedor, f.FolioProveedor }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } d ? d : f.Id.ToString();
}

/// <summary>
/// CXP-12 · reversa automática: el día 1 del periodo siguiente publica, por proveedor, el evento que Contabilidad asienta con los
/// lados contrarios. Idempotente: cada revaluación se revierte una sola vez. Lo corre el proceso diario (sin request) y también
/// puede lanzarse a mano.
/// </summary>
public sealed record PublicarReversasRevaluacionCommand : IRequest<int>;

public sealed class PublicarReversasRevaluacionHandler(CuentasPorPagarDbContext db, IClock clock, IIntegrationEventPublisher publisher)
    : IRequestHandler<PublicarReversasRevaluacionCommand, int>
{
    public async Task<int> Handle(PublicarReversasRevaluacionCommand _, CancellationToken cancellationToken)
    {
        var hoy = FechaNegocio.Hoy(clock.UtcNow);
        var pendientes = await db.RevaluacionesCierre.Include(r => r.Detalles)
            .Where(r => r.ReversaPublicadaEn == null && r.FechaReversa <= hoy).OrderBy(r => r.FechaCierre).ToListAsync(cancellationToken);
        foreach (var r in pendientes)
        {
            foreach (var (proveedorId, sucursalId, neto) in EjecutarRevaluacionCierreHandler.NetoPorProveedorYSucursal(r.Detalles))
                await publisher.PublishAsync(new RevaluacionCierreRevertidaIntegrationEvent(r.EmpresaId, clock.UtcNow, r.Id, proveedorId,
                    sucursalId, r.FechaReversa, Math.Max(neto, 0), Math.Max(-neto, 0)), cancellationToken);
            r.MarcarReversaPublicada(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
        return pendientes.Count;
    }
}
