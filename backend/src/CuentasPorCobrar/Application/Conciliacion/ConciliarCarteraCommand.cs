using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Domain.Ports.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.CuentasPorCobrar.Application.Conciliacion;

/// <summary>Día contable de Millet: mismo criterio que Facturación (<c>EventosContablesFacturacion.FechaContable</c>).</summary>
public static class DiaContableCxc
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");

    public static DateOnly De(DateTimeOffset instante) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, Zona).DateTime);

    public static DateTimeOffset Ahora(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zona);
}

/// <summary>
/// C7.7: concilia un día contable de Facturación contra la cartera y deja una
/// corrida registrada por empresa, con o sin diferencias. A demanda concilia la
/// empresa del usuario; el worker corre en bypass y concilia todas.
/// </summary>
public sealed record ConciliarCarteraCommand(DateOnly Fecha, OrigenConciliacion Origen)
    : IRequest<IReadOnlyList<ConciliacionResumenResponse>>;

public sealed class ConciliarCarteraValidator : AbstractValidator<ConciliarCarteraCommand>
{
    public ConciliarCarteraValidator(IClock clock)
    {
        RuleFor(x => x.Fecha)
            .Must(f => f <= DiaContableCxc.De(clock.UtcNow))
            .WithMessage("No se puede conciliar un día que todavía no llega.");
        RuleFor(x => x.Origen).IsInEnum();
    }
}

public sealed class ConciliarCarteraHandler(
    CuentasPorCobrarDbContext db,
    IConciliacionFacturacionReadPort facturacion,
    ICurrentEmpresaContext empresa,
    ICurrentUserContext usuario,
    IClock clock,
    IIncidenciasPort incidencias,
    IIncidenciasHistorialPort historial,
    IAperturaIncidenciasConciliacionPort aperturas,
    IResponsableConciliacionReadPort responsables,
    ILogger<ConciliarCarteraHandler> logger) : IRequestHandler<ConciliarCarteraCommand, IReadOnlyList<ConciliacionResumenResponse>>
{
    /// <summary>Comprobantes de un día contable con lo que hay en cartera para compararlos.</summary>
    private sealed record DiaCargado(
        ComprobantesDelDia Dia, IReadOnlyCollection<FacturaEnCartera> Facturas, IReadOnlyCollection<MovimientoEnCartera> Movimientos)
    {
        public (ComprobantesDelDia DelDia, IReadOnlyList<DiferenciaCalculada> Diferencias) De(Guid empresaId)
        {
            var delDia = new ComprobantesDelDia(
                Dia.Facturas.Where(f => f.EmpresaId == empresaId).ToList(),
                Dia.Aplicaciones.Where(a => a.EmpresaId == empresaId).ToList());
            return (delDia, ComparadorConciliacion.Comparar(delDia, Facturas, Movimientos));
        }
    }

    private async Task<DiaCargado> CargarDiaAsync(DateOnly fecha, CancellationToken cancellationToken)
    {
        var dia = await facturacion.ConsultarDiaAsync(fecha, cancellationToken);

        var facturaIds = dia.Facturas.Select(f => f.FacturaVentaId)
            .Concat(dia.Aplicaciones.Select(a => a.FacturaVentaId))
            .Distinct().ToList();
        var facturasCartera = await db.FacturasCartera.AsNoTracking()
            .Where(f => facturaIds.Contains(f.FacturaVentaId))
            .Select(f => new FacturaEnCartera(f.Id, f.FacturaVentaId, f.Total, f.Estado))
            .ToListAsync(cancellationToken);
        var origenIds = dia.Aplicaciones.Select(a => a.OrigenId).Distinct().ToList();
        var movimientos = await db.MovimientosCartera.AsNoTracking()
            .Where(m => origenIds.Contains(m.OrigenComprobanteId))
            .Select(m => new MovimientoEnCartera(m.FacturaCarteraId, m.Tipo, m.OrigenComprobanteId, m.Importe, m.Revertido))
            .ToListAsync(cancellationToken);
        return new DiaCargado(dia, facturasCartera, movimientos);
    }

    public async Task<IReadOnlyList<ConciliacionResumenResponse>> Handle(
        ConciliarCarteraCommand request, CancellationToken cancellationToken)
    {
        if (request.Origen == OrigenConciliacion.Demanda && empresa.Current is null)
            throw new BusinessRuleException("CC_SIN_EMPRESA", "Selecciona la empresa a conciliar.");

        var cargado = await CargarDiaAsync(request.Fecha, cancellationToken);
        var dia = cargado.Dia;
        var facturasCartera = cargado.Facturas;

        // Una corrida por empresa: la del usuario, o en el worker cada empresa con
        // cartera o con comprobantes ese día (un día limpio también se registra).
        IReadOnlyList<Guid> empresas = empresa.Current is Guid actual
            ? [actual]
            : (await db.FacturasCartera.AsNoTracking().Select(f => f.EmpresaId).Distinct().ToListAsync(cancellationToken))
                .Concat(dia.Facturas.Select(f => f.EmpresaId))
                .Concat(dia.Aplicaciones.Select(a => a.EmpresaId))
                .Distinct().ToList();

        var ahora = clock.UtcNow;
        var corridas = new List<ConciliacionCartera>();
        foreach (var empresaId in empresas)
        {
            var (delDia, diferencias) = cargado.De(empresaId);
            corridas.Add(ConciliacionCartera.Registrar(empresaId, request.Fecha, request.Origen,
                request.Origen == OrigenConciliacion.Demanda ? usuario.UserId : null,
                ahora, delDia.Total, diferencias));
        }

        db.ConciliacionesCartera.AddRange(corridas);
        await db.SaveChangesAsync(cancellationToken);

        // M-236: la corrida ya quedó guardada; abrir y cerrar incidencias es seguimiento y no debe tumbarla.
        // Los días ya cargados se comparten entre las empresas de la corrida (en el worker son varias).
        var dias = new Dictionary<DateOnly, DiaCargado> { [request.Fecha] = cargado };
        foreach (var corrida in corridas)
        {
            try
            {
                await SeguirIncidenciasAsync(corrida, dias, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Conciliación de cartera {Fecha}: no se pudo dar seguimiento a las incidencias de la empresa {Empresa}.",
                    corrida.Fecha, corrida.EmpresaId);
            }
        }

        return corridas.Select(ConciliacionResumenResponse.De).ToList();
    }

    /// <summary>
    /// M-236: cada diferencia de la corrida abre una incidencia común con responsable, y las incidencias de días
    /// anteriores cuya diferencia ya no aparece (se reflejó en cartera) se cierran solas. El cierre manual es el
    /// «Resolver» de la bandeja común de incidencias.
    /// </summary>
    private async Task SeguirIncidenciasAsync(ConciliacionCartera corrida, Dictionary<DateOnly, DiaCargado> dias, CancellationToken ct)
    {
        await CerrarReflejadasAsync(corrida, dias, ct);
        if (corrida.Diferencias.Count == 0)
            return;

        var responsable = corrida.Origen == OrigenConciliacion.Demanda && usuario.UserId is Guid ejecutor
            ? ejecutor
            : await responsables.ObtenerAsync(corrida.EmpresaId, ct);
        if (responsable is not Guid resp)
        {
            logger.LogWarning(
                "Conciliación de cartera {Fecha}: {Total} diferencia(s) sin incidencia porque la empresa {Empresa} no tiene un usuario con permiso para ejecutar la conciliación.",
                corrida.Fecha, corrida.Diferencias.Count, corrida.EmpresaId);
            return;
        }

        foreach (var d in corrida.Diferencias)
        {
            // Una incidencia cerrada a mano es definitiva para esa diferencia (día y tipo): no se reabre en cada corrida.
            if (await historial.ExisteCerradaManualAsync(PasosIncidencia.CxcConciliacionDiferencia, d.ComprobanteId,
                    IncidenciasConciliacion.Clave(corrida.Fecha, d.Tipo), IncidenciasConciliacion.TextoCierreAutomatico(corrida.Fecha), ct))
                continue;
            var calculada = new DiferenciaCalculada(d.Tipo, d.ComprobanteId, d.TipoComprobante, d.Folio, d.Uuid, d.FacturaVentaId,
                d.ReceptorRfc, d.ReceptorNombre, d.ImporteFacturacion, d.ImporteCartera, d.CausaProbable);
            await aperturas.AbrirAsync(corrida.EmpresaId, IncidenciasConciliacion.Solicitud(corrida.Fecha, calculada, resp), ct);
        }
    }

    private async Task CerrarReflejadasAsync(ConciliacionCartera corrida, Dictionary<DateOnly, DiaCargado> dias, CancellationToken ct)
    {
        var desde = corrida.Fecha.AddDays(-IncidenciasConciliacion.VentanaSeguimientoDias);
        var previas = (await db.ConciliacionesCartera.AsNoTracking().Include(c => c.Diferencias)
                .Where(c => c.EmpresaId == corrida.EmpresaId && c.Id != corrida.Id && c.Fecha >= desde && c.Fecha <= corrida.Fecha)
                .ToListAsync(ct))
            .SelectMany(c => c.Diferencias.Select(d => (c.Fecha, d.ComprobanteId, d.Tipo)))
            .Distinct()
            .ToList();

        // Solo los comprobantes con incidencia pendiente; cada día se vuelve a comparar una sola vez y, para acotar el costo
        // (una consulta a Facturación por día), como máximo MaxDiasRevisionPorCorrida días por corrida, los más recientes primero.
        var conPendiente = new HashSet<Guid>();
        foreach (var id in previas.Select(p => p.ComprobanteId).Distinct())
            if (await incidencias.HayPendientesAsync(id, PasosIncidencia.CxcConciliacionDiferencia, ct))
                conPendiente.Add(id);

        var porDias = previas.Where(p => conPendiente.Contains(p.ComprobanteId))
            .GroupBy(p => p.Fecha).OrderByDescending(g => g.Key)
            .Take(IncidenciasConciliacion.MaxDiasRevisionPorCorrida);
        foreach (var porDia in porDias)
        {
            if (!dias.TryGetValue(porDia.Key, out var cargado))
                dias[porDia.Key] = cargado = await CargarDiaAsync(porDia.Key, ct);
            var vigentes = cargado.De(corrida.EmpresaId).Diferencias.Select(d => (d.ComprobanteId, d.Tipo)).ToHashSet();
            foreach (var (fecha, comprobanteId, tipo) in porDia)
            {
                if (vigentes.Contains((comprobanteId, tipo)))
                    continue;
                await incidencias.ResolverPorDocumentoAsync(PasosIncidencia.CxcConciliacionDiferencia, comprobanteId,
                    IncidenciasConciliacion.Clave(fecha, tipo), IncidenciasConciliacion.TextoCierreAutomatico(fecha), ct);
            }
        }
    }
}
