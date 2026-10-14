using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Reportes.LiquidacionCaja;

/// <summary>
/// Reporte Liquidación de caja, redefinido sobre las sesiones de efectivo
/// (`[Decisión 12-11]`, CAJAS-PR4): una fila por sesión CERRADA en el rango
/// (por fecha de cierre; sucursal de operación opcional), con fondo,
/// esperado/declarado de efectivo y diferencia del arqueo. Sustituye a la
/// versión pre-cajas que agrupaba facturas por forma de pago. Contrato JSON
/// ADR-0036; export client-side; misma firma de filtros (el FE no cambia).
/// FIX-12: incluye el cajero responsable (Manual de Sucursales §2.4) y el id
/// de la sesión (no es columna) para abrir su corte desde la fila.
/// </summary>
public sealed record LiquidacionCajaQuery(
    Guid? SucursalId,
    DateTimeOffset Desde,
    DateTimeOffset Hasta) : IRequest<ReporteResponse<LiquidacionCajaFila>>;

public sealed record LiquidacionCajaFila(
    Guid SesionId,
    string DiaOperacion,
    string Caja,
    string Cajero,
    string FechaCierre,
    decimal FondoApertura,
    decimal EfectivoTeorico,
    decimal EfectivoDeclarado,
    decimal Diferencia,
    string CierreExtemporaneo);

public sealed class LiquidacionCajaHandler
    : IRequestHandler<LiquidacionCajaQuery, ReporteResponse<LiquidacionCajaFila>>
{
    private readonly FacturacionDbContext _db;
    private readonly IClock _clock;
    private readonly IUsuariosReadPort _usuarios;

    public LiquidacionCajaHandler(FacturacionDbContext db, IClock clock, IUsuariosReadPort usuarios)
    {
        _db = db;
        _clock = clock;
        _usuarios = usuarios;
    }

    public async Task<ReporteResponse<LiquidacionCajaFila>> Handle(
        LiquidacionCajaQuery query, CancellationToken cancellationToken)
    {
        var sesiones = _db.CajaSesiones.AsNoTracking()
            .Where(s => s.Estado == EstadoCajaSesion.Cerrada
                        && s.FechaCierre >= query.Desde.ToUniversalTime() && s.FechaCierre <= query.Hasta.ToUniversalTime());
        if (query.SucursalId is { } suc) sesiones = sesiones.Where(s => s.SucursalId == suc);

        var rows = await sesiones
            .OrderBy(s => s.DiaOperacion).ThenBy(s => s.FechaCierre)
            .ToListAsync(cancellationToken);

        var cajaIds = rows.Select(s => s.CajaId).Distinct().ToList();
        var cajas = await _db.Cajas.AsNoTracking()
            .Where(c => cajaIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Nombre })
            .ToListAsync(cancellationToken);
        var nombrePorCaja = cajas.ToDictionary(c => c.Id, c => c.Nombre);
        var cajeros = await _usuarios.ObtenerNombresAsync(
            rows.Select(s => s.ResponsableUsuarioId).Distinct().ToList(), cancellationToken);

        var filas = rows.Select(s => new LiquidacionCajaFila(
            s.Id,
            s.DiaOperacion.ToString("yyyy-MM-dd"),
            nombrePorCaja.GetValueOrDefault(s.CajaId, s.CajaId.ToString()),
            cajeros.GetValueOrDefault(s.ResponsableUsuarioId) ?? s.ResponsableUsuarioId.ToString(),
            s.FechaCierre!.Value.ToString("o"),
            s.FondoApertura,
            s.EfectivoTeorico ?? 0m,
            s.EfectivoDeclarado ?? 0m,
            s.Diferencia ?? 0m,
            s.CierreExtemporaneo ? "Sí" : "No")).ToList();

        return new ReporteResponse<LiquidacionCajaFila>(
            Titulo: "Liquidación de caja",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>
            {
                ["sucursalId"] = query.SucursalId?.ToString(),
                ["desde"] = query.Desde.ToString("o"),
                ["hasta"] = query.Hasta.ToString("o"),
            },
            Columnas:
            [
                new("diaOperacion", "Día de operación", "texto"),
                new("caja", "Caja", "texto"),
                new("cajero", "Cajero", "texto"),
                new("fechaCierre", "Cierre", "fecha"),
                new("fondoApertura", "Fondo", "moneda", "derecha"),
                new("efectivoTeorico", "Efectivo esperado", "moneda", "derecha"),
                new("efectivoDeclarado", "Efectivo contado", "moneda", "derecha"),
                new("diferencia", "Diferencia", "moneda", "derecha"),
                new("cierreExtemporaneo", "Extemporáneo", "texto"),
            ],
            Filas: filas,
            Totales: new Dictionary<string, decimal>
            {
                ["efectivoTeorico"] = filas.Sum(f => f.EfectivoTeorico),
                ["efectivoDeclarado"] = filas.Sum(f => f.EfectivoDeclarado),
                ["diferencia"] = filas.Sum(f => f.Diferencia),
            });
    }
}
