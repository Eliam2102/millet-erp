using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Reportes.CierreDia;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Conciliacion;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Conciliacion;

// M-314 (86e4084j3): FAC-CONCILIACION · resolver la diferencia fiscal y cerrar los folios del periodo.

// ---------------------------------------------------------------- Resolver la incidencia fiscal

public sealed record ResolverIncidenciaFiscalCommand(Guid ComprobanteId, string? Soporte, string? Nota) : IRequest<ResolucionIncidenciaFiscalResponse>;

public sealed record ResolucionIncidenciaFiscalResponse(Guid Id, Guid ComprobanteId, string Uuid, string? EstatusSatAntes, string EstatusSatDespues,
    string Soporte, string? Nota, string ResueltaPor, DateTimeOffset ResueltaEn)
{
    internal static ResolucionIncidenciaFiscalResponse De(ResolucionIncidenciaFiscal r) =>
        new(r.Id, r.ComprobanteId, r.Uuid, r.EstatusSatAntes, r.EstatusSatDespues, r.Soporte, r.Nota, r.ResueltaPor, r.ResueltaEn);
}

public sealed class ResolverIncidenciaFiscalValidator : AbstractValidator<ResolverIncidenciaFiscalCommand>
{
    public ResolverIncidenciaFiscalValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEmpty();
        RuleFor(c => c.Soporte).NotEmpty().MaximumLength(ResolucionIncidenciaFiscal.SoporteMaximo);
        RuleFor(c => c.Nota).MaximumLength(1000);
    }
}

/// <summary>
/// Una incidencia fiscal es la discrepancia que marca el monitor de CFDI. Para resolverla se consulta de nuevo el estatus del UUID ante el
/// SAT y se exige soporte: si la consulta nueva sigue mostrando diferencia, no se resuelve (el estatus consultado queda guardado).
/// </summary>
public sealed class ResolverIncidenciaFiscalHandler(FacturacionDbContext db, IConsultaEstatusCfdiPort estatusSat, ICurrentUserContext usuario,
    IClock clock) : IRequestHandler<ResolverIncidenciaFiscalCommand, ResolucionIncidenciaFiscalResponse>
{
    public async Task<ResolucionIncidenciaFiscalResponse> Handle(ResolverIncidenciaFiscalCommand request, CancellationToken cancellationToken)
    {
        var c = await db.Comprobantes.FirstOrDefaultAsync(x => x.Id == request.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", "No existe el comprobante solicitado.");
        if (c.Uuid is null || !MonitorCfdiHandler.EsDiscrepancia(c))
            throw new ConflictException("FAC_INCIDENCIA_NO_ABIERTA", "El comprobante no tiene una incidencia fiscal abierta: el SAT coincide con su estado.");
        // Falla antes de consultar si no hay soporte: no se gasta cuota del PAC en una resolución que se va a rechazar.
        if (string.IsNullOrWhiteSpace(request.Soporte) || request.Soporte.Trim().Length < ResolucionIncidenciaFiscal.SoporteMinimo)
            throw new BusinessRuleException("FAC_INCIDENCIA_SOPORTE_REQUERIDO", "Indique el soporte que respalda la resolución.");

        var antes = c.EstatusSat;
        var r = await estatusSat.ConsultarAsync(new ConsultaEstatusCfdi(
            c.EmpresaId, c.Uuid, c.RfcEmisor, c.ReceptorRfc, c.Total,
            c.SelloCfdi is { Length: >= 8 } sello ? sello[^8..] : null, c.Estado), cancellationToken);
        c.RegistrarEstatusSat(r.EstatusSat, r.EsCancelable, clock.UtcNow);

        if (MonitorCfdiHandler.EsDiscrepancia(c))
        {
            await db.SaveChangesAsync(cancellationToken); // el estatus nuevo se guarda aunque no se resuelva
            throw new BusinessRuleException("FAC_INCIDENCIA_SIGUE_CON_DIFERENCIA",
                $"La nueva consulta al SAT sigue mostrando diferencia (SAT: {r.EstatusSat}; sistema: {c.Estado}). Corrija el comprobante y vuelva a resolver.");
        }

        var resolucion = new ResolucionIncidenciaFiscal(Guid.CreateVersion7(), c.EmpresaId, c.Id, c.Uuid, c.SucursalId, c.Estado, antes,
            r.EstatusSat, request.Soporte, request.Nota, usuario.UserName ?? "sistema", clock.UtcNow);
        db.ResolucionesIncidenciaFiscal.Add(resolucion);
        await db.SaveChangesAsync(cancellationToken);
        return ResolucionIncidenciaFiscalResponse.De(resolucion);
    }
}

public sealed record ListarResolucionesIncidenciaFiscalQuery(Guid? ComprobanteId) : IRequest<IReadOnlyList<ResolucionIncidenciaFiscalResponse>>;

public sealed class ListarResolucionesIncidenciaFiscalHandler(FacturacionDbContext db)
    : IRequestHandler<ListarResolucionesIncidenciaFiscalQuery, IReadOnlyList<ResolucionIncidenciaFiscalResponse>>
{
    public async Task<IReadOnlyList<ResolucionIncidenciaFiscalResponse>> Handle(ListarResolucionesIncidenciaFiscalQuery request, CancellationToken cancellationToken)
    {
        var q = db.ResolucionesIncidenciaFiscal.AsNoTracking().AsQueryable();
        if (request.ComprobanteId is { } id) q = q.Where(r => r.ComprobanteId == id);
        var items = await q.OrderByDescending(r => r.ResueltaEn).Take(500).ToListAsync(cancellationToken);
        return items.Select(ResolucionIncidenciaFiscalResponse.De).ToList();
    }
}

// ---------------------------------------------------------------- Cierre de folios por serie y mes

public sealed record CierreFoliosSerieResponse(string Tipo, string Serie, long FolioInicial, long FolioFinal, int Usados, int Cancelados,
    int Descartados, IReadOnlyList<long> Pendientes, IReadOnlyList<long> Huecos, bool PuedeCerrar, Guid? CierreId, bool Cerrada,
    DateTimeOffset? CerradoEn, string? CerradoPor);

public sealed record CierreFoliosResponse(int Anio, int Mes, IReadOnlyList<CierreFoliosSerieResponse> Series);

public sealed record ConsultarCierreFoliosQuery(int Anio, int Mes) : IRequest<CierreFoliosResponse>;
public sealed record CerrarFoliosPeriodoCommand(int Anio, int Mes) : IRequest<CierreFoliosResponse>;
public sealed record ReabrirCierreFoliosCommand(Guid CierreId, string? Motivo) : IRequest<CierreFoliosSerieResponse>;

public sealed class CierreFoliosValidators : AbstractValidator<CerrarFoliosPeriodoCommand>
{
    public CierreFoliosValidators()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2000, 2100);
        RuleFor(c => c.Mes).InclusiveBetween(1, 12);
    }
}

public sealed class ConsultarCierreFoliosValidator : AbstractValidator<ConsultarCierreFoliosQuery>
{
    public ConsultarCierreFoliosValidator()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2000, 2100);
        RuleFor(c => c.Mes).InclusiveBetween(1, 12);
    }
}

public sealed class ReabrirCierreFoliosValidator : AbstractValidator<ReabrirCierreFoliosCommand>
{
    public ReabrirCierreFoliosValidator()
    {
        RuleFor(c => c.CierreId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(CierreFolios.MotivoMaximo);
    }
}

internal static class CierreFoliosComun
{
    /// <summary>Folios del mes (por fecha de creación, UTC; supuesto a validar) agrupados por tipo y serie, ya analizados.</summary>
    public static async Task<List<(TipoComprobante Tipo, string Serie, AnalisisFolios Analisis, string Reporte)>> AnalizarAsync(
        FacturacionDbContext db, int anio, int mes, CancellationToken ct)
    {
        var desde = new DateTimeOffset(anio, mes, 1, 0, 0, 0, TimeSpan.Zero);
        var hasta = desde.AddMonths(1);
        var folios = await db.Comprobantes.AsNoTracking()
            .Where(c => c.CreatedAt >= desde && c.CreatedAt < hasta && c.FolioNumero > 0 && c.Folio != "")
            .Select(c => new { c.Tipo, c.Folio, c.FolioNumero, c.Estado })
            .ToListAsync(ct);
        return folios.GroupBy(f => (f.Tipo, Serie: AnalisisFolios.SerieDe(f.Folio)))
            .OrderBy(g => g.Key.Tipo).ThenBy(g => g.Key.Serie, StringComparer.Ordinal)
            .Select(g =>
            {
                var a = AnalisisFolios.Analizar(g.Select(f => (f.FolioNumero, f.Estado)));
                var reporte = JsonSerializer.Serialize(new
                {
                    Tipo = g.Key.Tipo.ToString(), g.Key.Serie, a.FolioInicial, a.FolioFinal, a.Usados, a.Cancelados, a.Descartados,
                });
                return (g.Key.Tipo, g.Key.Serie, a, reporte);
            }).ToList();
    }

    public static CierreFoliosSerieResponse Fila(TipoComprobante tipo, string serie, AnalisisFolios a, CierreFolios? cierre) =>
        new(tipo.ToString(), serie, a.FolioInicial, a.FolioFinal, a.Usados.Count, a.Cancelados.Count, a.Descartados.Count, a.Pendientes, a.Huecos,
            a.PuedeCerrar, cierre?.Id, cierre is { Vigente: true }, cierre?.CerradoEn, cierre?.CerradoPor);

    public static CierreFoliosSerieResponse DeCierre(CierreFolios c) =>
        new(c.Tipo.ToString(), c.Serie, c.FolioInicial, c.FolioFinal, c.Usados, c.Cancelados, c.Descartados, [], [], true, c.Id, c.Vigente,
            c.CerradoEn, c.CerradoPor);
}

/// <summary>Vista del mes: lo vivo (con pendientes y huecos si los hay) y, si la serie está cerrada, su reporte guardado.</summary>
public sealed class ConsultarCierreFoliosHandler(FacturacionDbContext db) : IRequestHandler<ConsultarCierreFoliosQuery, CierreFoliosResponse>
{
    public async Task<CierreFoliosResponse> Handle(ConsultarCierreFoliosQuery request, CancellationToken cancellationToken)
    {
        var cierres = await db.CierresFolios.AsNoTracking().Where(c => c.Anio == request.Anio && c.Mes == request.Mes && c.Vigente)
            .ToListAsync(cancellationToken);
        var series = new List<CierreFoliosSerieResponse>();
        // Una serie cerrada muestra su reporte guardado (no se recalcula: lo que se cerró es lo que quedó).
        foreach (var c in cierres) series.Add(CierreFoliosComun.DeCierre(c));
        foreach (var (tipo, serie, a, _) in await CierreFoliosComun.AnalizarAsync(db, request.Anio, request.Mes, cancellationToken))
            if (!cierres.Any(c => c.Tipo == tipo && c.Serie == serie)) series.Add(CierreFoliosComun.Fila(tipo, serie, a, null));
        return new(request.Anio, request.Mes, series.OrderBy(s => s.Tipo).ThenBy(s => s.Serie, StringComparer.Ordinal).ToList());
    }
}

/// <summary>
/// Cierra todas las series del mes con folios: si alguna tiene folios pendientes o huecos, no se cierra ninguna (409/422 con el detalle).
/// Las series ya cerradas se respetan. El reporte final (usados, cancelados, descartados) queda guardado.
/// </summary>
public sealed class CerrarFoliosPeriodoHandler(FacturacionDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<CerrarFoliosPeriodoCommand, CierreFoliosResponse>
{
    public async Task<CierreFoliosResponse> Handle(CerrarFoliosPeriodoCommand request, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new BusinessRuleException("FAC_EMPRESA_REQUERIDA", "No hay empresa seleccionada.");
        var vigentes = await db.CierresFolios.Where(c => c.Anio == request.Anio && c.Mes == request.Mes && c.Vigente).ToListAsync(cancellationToken);
        var analisis = await CierreFoliosComun.AnalizarAsync(db, request.Anio, request.Mes, cancellationToken);
        if (analisis.Count == 0)
            throw new BusinessRuleException("FAC_CIERRE_FOLIOS_SIN_FOLIOS", "No hay folios en el periodo: no hay nada que cerrar.");
        var porCerrar = analisis.Where(a => !vigentes.Any(c => c.Tipo == a.Tipo && c.Serie == a.Serie)).ToList();
        var bloqueadas = porCerrar.Where(a => !a.Analisis.PuedeCerrar).ToList();
        if (bloqueadas.Count > 0)
            throw new BusinessRuleException("FAC_CIERRE_FOLIOS_NO_CUMPLE", string.Join(" ", bloqueadas.Select(b => b.Analisis.Motivo(b.Serie))));

        foreach (var (tipo, serie, a, reporte) in porCerrar)
            db.CierresFolios.Add(new CierreFolios(Guid.CreateVersion7(), empresaId, request.Anio, request.Mes, tipo, serie, a, reporte,
                usuario.UserName ?? "sistema", clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return await new ConsultarCierreFoliosHandler(db).Handle(new ConsultarCierreFoliosQuery(request.Anio, request.Mes), cancellationToken);
    }
}

/// <summary>Reabre un cierre de folios (permiso propio); queda motivo, quién y cuándo. Cerrar de nuevo exige volver a cumplir.</summary>
public sealed class ReabrirCierreFoliosHandler(FacturacionDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<ReabrirCierreFoliosCommand, CierreFoliosSerieResponse>
{
    public async Task<CierreFoliosSerieResponse> Handle(ReabrirCierreFoliosCommand request, CancellationToken cancellationToken)
    {
        var c = await db.CierresFolios.FirstOrDefaultAsync(x => x.Id == request.CierreId, cancellationToken)
            ?? throw new EntityNotFoundException("CIERRE_FOLIOS_NO_ENCONTRADO", "No existe el cierre de folios solicitado.");
        c.Reabrir(request.Motivo, usuario.UserName ?? "sistema", clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return CierreFoliosComun.DeCierre(c);
    }
}
