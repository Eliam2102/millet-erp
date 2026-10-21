using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Reportes.ComparativaAnticipos;

/// <summary>
/// Estado del anticipo en la comparativa (F6.2). Derivado de lo cobrado contra
/// lo aplicado con egreso vigente a la fecha de corte; no es el
/// <see cref="EstadoAnticipo"/> persistido del agregado.
/// </summary>
public enum EstadoComparativaAnticipo : short
{
    /// <summary>Sin aplicaciones vigentes a la fecha de corte.</summary>
    Pendiente = 1,

    /// <summary>Aplicado en parte; queda saldo.</summary>
    Parcial = 2,

    /// <summary>Saldo cero (o menor) a la fecha de corte.</summary>
    Aplicado = 3,

    /// <summary>El anticipo o su CFDI está cancelado.</summary>
    Cancelado = 4,
}

/// <summary>
/// Comparativa de anticipos contra aplicaciones y relación CFDI (F6.2). Vista
/// tipo libro mayor: por cada factura de anticipo una fila «Anticipo» con lo
/// cobrado y el saldo a la fecha de corte, seguida de sus aplicaciones (factura
/// final + CFDI de egreso tipo 07 que amortiza el anticipo). Contrato JSON
/// ADR-0036.
///
/// <para>
/// Reglas: una aplicación resta saldo solo si su egreso está Timbrado o en
/// CancelacionPendiente y se timbró a más tardar en la fecha de corte. Un
/// egreso Cancelado se muestra pero no resta. Las vinculaciones sin egreso
/// timbrado van a la columna «Comprometido» y tampoco restan. El periodo
/// (<c>Desde</c>/<c>Hasta</c>) filtra por fecha de timbrado del anticipo y el
/// alcance es la Capa A de Cajas sobre la sucursal del anticipo.
/// </para>
/// </summary>
public sealed record ComparativaAnticiposQuery(
    Guid? SucursalId,
    Guid? ClienteId,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta,
    DateTimeOffset? Corte,
    EstadoComparativaAnticipo? Estado) : IRequest<ReporteResponse<ComparativaAnticipoFila>>;

/// <summary>
/// Fila de la comparativa. <c>Movimiento</c> = «Anticipo», «Aplicación» o
/// «Vinculación sin egreso». En las filas de aplicación <c>Cobrado*</c> y
/// <c>Saldo</c> van en <c>null</c>; en la del anticipo, <c>Aplicado*</c> y
/// <c>Comprometido</c> llevan el acumulado.
/// </summary>
public sealed record ComparativaAnticipoFila(
    string Sucursal,
    string Cliente,
    string FolioAnticipo,
    string? UuidAnticipo,
    DateTimeOffset? FechaAnticipo,
    string Movimiento,
    string? FacturaFinalFolio,
    string? FacturaFinalUuid,
    string? EgresoFolio,
    string? EgresoUuid,
    DateTimeOffset? FechaAplicacion,
    string? EstadoEgreso,
    decimal? CobradoSubtotal,
    decimal? CobradoIva,
    decimal? Cobrado,
    decimal? AplicadoSubtotal,
    decimal? AplicadoIva,
    decimal? Aplicado,
    decimal? Comprometido,
    decimal? Saldo,
    string Estado,
    string? Observacion,
    Guid AnticipoId,
    Guid FacturaAnticipoId);

public sealed class ComparativaAnticiposHandler
    : IRequestHandler<ComparativaAnticiposQuery, ReporteResponse<ComparativaAnticipoFila>>
{
    public const string MovimientoAnticipo = "Anticipo";
    public const string MovimientoAplicacion = "Aplicación";
    public const string MovimientoVinculacionSinEgreso = "Vinculación sin egreso";

    private static readonly CultureInfo EsMx = CultureInfo.GetCultureInfo("es-MX");

    private static readonly EstadoTimbrado[] EstadosAnticipoIncluidos =
        [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente, EstadoTimbrado.Cancelado];

    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly ISucursalesReadPort _sucursales;
    private readonly IClock _clock;

    public ComparativaAnticiposHandler(
        FacturacionDbContext db,
        IAlcanceCajaEvaluator alcance,
        ISucursalesReadPort sucursales,
        IClock clock)
    {
        _db = db;
        _alcance = alcance;
        _sucursales = sucursales;
        _clock = clock;
    }

    public async Task<ReporteResponse<ComparativaAnticipoFila>> Handle(
        ComparativaAnticiposQuery query, CancellationToken cancellationToken)
    {
        var ahora = _clock.UtcNow;
        // PostgreSQL (timestamptz) solo acepta parámetros en UTC.
        var corte = (query.Corte ?? ahora).ToUniversalTime();

        // 1) Facturas de anticipo bajo la Capa A (sucursal × canal del CFDI).
        var facturasQ = _db.FacturasAnticipo.AsNoTracking()
            .Where(f => EstadosAnticipoIncluidos.Contains(f.Estado)
                        && f.FechaTimbrado != null && f.FechaTimbrado <= corte);
        if (query.SucursalId is { } sucursalId) facturasQ = facturasQ.Where(f => f.SucursalId == sucursalId);
        if (query.Desde is { } desde)
        {
            var desdeUtc = desde.ToUniversalTime();
            facturasQ = facturasQ.Where(f => f.FechaTimbrado >= desdeUtc);
        }
        if (query.Hasta is { } hasta)
        {
            var hastaUtc = hasta.ToUniversalTime();
            facturasQ = facturasQ.Where(f => f.FechaTimbrado <= hastaUtc);
        }

        var alcance = await _alcance.ResolverAsync(cancellationToken);
        facturasQ = alcance.AplicarA(facturasQ);

        var facturas = await facturasQ
            .Select(f => new
            {
                f.Id, f.Folio, f.FolioNumero, f.Uuid, f.FechaTimbrado, f.Subtotal,
                f.ImpuestosTrasladados, f.ReceptorNombre, f.SucursalId, f.Estado,
            })
            .ToListAsync(cancellationToken);

        // 2) Agregado Anticipo (saldo, vinculaciones) de esas facturas.
        var facturaIds = facturas.Select(f => f.Id).ToList();
        var anticiposQ = _db.Anticipos.AsNoTracking()
            .Include(a => a.Vinculaciones)
            .Where(a => facturaIds.Contains(a.FacturaAnticipoId));
        if (query.ClienteId is { } clienteId) anticiposQ = anticiposQ.Where(a => a.ClienteId == clienteId);
        var anticipos = facturaIds.Count == 0 ? [] : await anticiposQ.ToListAsync(cancellationToken);
        var anticipoPorFactura = anticipos.ToDictionary(a => a.FacturaAnticipoId);

        // 3) Facturas finales y egresos (NC de amortización). Sin Capa A: el
        // anticipo ya pasó el alcance y su cadena se muestra completa.
        var vinculaciones = anticipos.SelectMany(a => a.Vinculaciones).ToList();
        var facturaVentaIds = vinculaciones.Select(v => v.FacturaVentaId).Distinct().ToList();
        var ncIds = vinculaciones
            .Where(v => v.NcAmortizacionId is not null)
            .Select(v => v.NcAmortizacionId!.Value).Distinct().ToList();

        var facturasVenta = facturaVentaIds.Count == 0
            ? []
            : await _db.FacturasVenta.AsNoTracking()
                .Where(f => facturaVentaIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Folio, f.Uuid })
                .ToListAsync(cancellationToken);
        var facturaVentaPorId = facturasVenta.ToDictionary(f => f.Id);

        var ncs = ncIds.Count == 0
            ? []
            : await _db.NotasCredito.AsNoTracking()
                .Where(n => ncIds.Contains(n.Id))
                .Select(n => new
                {
                    n.Id, n.Folio, n.Uuid, n.Estado, n.FechaTimbrado, n.Subtotal, n.ImpuestosTrasladados, n.Total,
                })
                .ToListAsync(cancellationToken);
        var ncPorId = ncs.ToDictionary(n => n.Id);

        // 4) Nombre de sucursal («CLAVE — Nombre»), una lectura por sucursal.
        var nombreSucursal = new Dictionary<Guid, string>();
        foreach (var sucId in facturas.Select(f => f.SucursalId).Distinct())
        {
            nombreSucursal[sucId] = await _sucursales.ObtenerNombreAsync(sucId, cancellationToken)
                                    ?? sucId.ToString();
        }

        // 5) Libro mayor: fila del anticipo + sus aplicaciones.
        var filas = new List<ComparativaAnticipoFila>();
        var filasAnticipo = new List<ComparativaAnticipoFila>();

        var ordenadas = facturas
            .Where(f => anticipoPorFactura.ContainsKey(f.Id))
            .OrderBy(f => f.ReceptorNombre, StringComparer.Create(EsMx, ignoreCase: true))
            .ThenBy(f => f.FolioNumero);

        foreach (var f in ordenadas)
        {
            var anticipo = anticipoPorFactura[f.Id];
            var sucursal = nombreSucursal[f.SucursalId];

            var aplicaciones = new List<(DateTimeOffset? Orden, DateTimeOffset CreadoEn, ComparativaAnticipoFila Fila)>();
            decimal aplicadoSubtotal = 0m, aplicadoIva = 0m, aplicado = 0m, comprometido = 0m;

            foreach (var v in anticipo.Vinculaciones.Where(v => v.CreadoEn.ToUniversalTime() <= corte))
            {
                var fv = facturaVentaPorId.GetValueOrDefault(v.FacturaVentaId);
                var nc = v.NcAmortizacionId is { } ncId ? ncPorId.GetValueOrDefault(ncId) : null;

                var egresoTimbrado = nc is not null && nc.FechaTimbrado is not null
                    && nc.Estado is EstadoTimbrado.Timbrado or EstadoTimbrado.CancelacionPendiente or EstadoTimbrado.Cancelado;

                // Egreso timbrado después del corte: no existía a esa fecha.
                if (egresoTimbrado && nc!.FechaTimbrado!.Value.ToUniversalTime() > corte) continue;

                ComparativaAnticipoFila fila;
                if (!egresoTimbrado)
                {
                    // Vinculación M2 sin NC timbrada: compromete saldo, no lo resta.
                    comprometido += v.Importe;
                    fila = Aplicacion(
                        f.Id, anticipo.Id, sucursal, f.ReceptorNombre, f.Folio, f.Uuid, f.FechaTimbrado,
                        MovimientoVinculacionSinEgreso, fv?.Folio, fv?.Uuid,
                        nc?.Folio, nc?.Uuid, null, nc is null ? null : DescribirEstado(nc.Estado),
                        null, null, null, v.Importe,
                        nc is null ? null : "Egreso sin timbrar: no resta saldo");
                }
                else if (nc!.Estado == EstadoTimbrado.Cancelado)
                {
                    fila = Aplicacion(
                        f.Id, anticipo.Id, sucursal, f.ReceptorNombre, f.Folio, f.Uuid, f.FechaTimbrado,
                        MovimientoAplicacion, fv?.Folio, fv?.Uuid,
                        nc.Folio, nc.Uuid, nc.FechaTimbrado, DescribirEstado(nc.Estado),
                        null, null, null, null,
                        "Egreso cancelado: no resta saldo");
                }
                else
                {
                    aplicadoSubtotal += nc.Subtotal;
                    aplicadoIva += nc.ImpuestosTrasladados;
                    aplicado += nc.Total;
                    fila = Aplicacion(
                        f.Id, anticipo.Id, sucursal, f.ReceptorNombre, f.Folio, f.Uuid, f.FechaTimbrado,
                        MovimientoAplicacion, fv?.Folio, fv?.Uuid,
                        nc.Folio, nc.Uuid, nc.FechaTimbrado, DescribirEstado(nc.Estado),
                        nc.Subtotal, nc.ImpuestosTrasladados, nc.Total, null, null);
                }

                aplicaciones.Add((fila.FechaAplicacion, v.CreadoEn, fila));
            }

            var cobrado = anticipo.MontoCobrado;
            var saldo = cobrado - aplicado;
            var estado = anticipo.Estado == EstadoAnticipo.Cancelado || f.Estado == EstadoTimbrado.Cancelado
                ? EstadoComparativaAnticipo.Cancelado
                : aplicado == 0m
                    ? EstadoComparativaAnticipo.Pendiente
                    : saldo <= 0m
                        ? EstadoComparativaAnticipo.Aplicado
                        : EstadoComparativaAnticipo.Parcial;

            if (query.Estado is { } filtroEstado && filtroEstado != estado) continue;

            // Solo sin fecha de corte (corte = hoy) el saldo calculado debe
            // coincidir con el persistido; con un corte pasado difiere por diseño.
            string? observacion = null;
            if (query.Corte is null && saldo != anticipo.Saldo)
                observacion = $"Saldo en sistema {anticipo.Saldo.ToString("C2", EsMx)}";

            var filaAnticipo = new ComparativaAnticipoFila(
                sucursal, f.ReceptorNombre, f.Folio, f.Uuid, f.FechaTimbrado,
                MovimientoAnticipo, null, null, null, null, null, null,
                f.Subtotal, f.ImpuestosTrasladados, cobrado,
                aplicadoSubtotal, aplicadoIva, aplicado, comprometido, saldo,
                estado.ToString(), observacion, anticipo.Id, f.Id);

            filasAnticipo.Add(filaAnticipo);
            filas.Add(filaAnticipo);
            filas.AddRange(aplicaciones
                .OrderBy(a => a.Orden is null) // vinculaciones sin egreso al final
                .ThenBy(a => a.Orden)
                .ThenBy(a => a.CreadoEn)
                .Select(a => a.Fila with { Estado = estado.ToString() }));
        }

        var totales = new Dictionary<string, decimal>
        {
            ["cobradoSubtotal"] = filasAnticipo.Sum(r => r.CobradoSubtotal ?? 0m),
            ["cobradoIva"] = filasAnticipo.Sum(r => r.CobradoIva ?? 0m),
            ["cobrado"] = filasAnticipo.Sum(r => r.Cobrado ?? 0m),
            ["aplicadoSubtotal"] = filasAnticipo.Sum(r => r.AplicadoSubtotal ?? 0m),
            ["aplicadoIva"] = filasAnticipo.Sum(r => r.AplicadoIva ?? 0m),
            ["aplicado"] = filasAnticipo.Sum(r => r.Aplicado ?? 0m),
            ["comprometido"] = filasAnticipo.Sum(r => r.Comprometido ?? 0m),
            ["saldo"] = filasAnticipo.Sum(r => r.Saldo ?? 0m),
        };

        var filtros = new Dictionary<string, string?>
        {
            ["sucursalId"] = query.SucursalId?.ToString(),
            ["clienteId"] = query.ClienteId?.ToString(),
            ["desde"] = query.Desde?.ToString("o"),
            ["hasta"] = query.Hasta?.ToString("o"),
            ["corte"] = corte.ToString("o"),
            ["estado"] = query.Estado?.ToString(),
        };

        return new ReporteResponse<ComparativaAnticipoFila>(
            Titulo: "Comparativa de anticipos vs aplicaciones",
            GeneradoEn: ahora,
            FiltrosAplicados: filtros,
            Columnas: Columnas,
            Filas: filas,
            Totales: totales);
    }

    private static readonly IReadOnlyList<ColumnaDescriptor> Columnas =
    [
        new("sucursal", "Sucursal", "texto"),
        new("cliente", "Cliente", "texto"),
        new("folioAnticipo", "Anticipo", "texto"),
        new("uuidAnticipo", "UUID anticipo", "texto"),
        new("fechaAnticipo", "Fecha anticipo", "fecha"),
        new("movimiento", "Movimiento", "texto"),
        new("facturaFinalFolio", "Factura final", "texto"),
        new("facturaFinalUuid", "UUID factura final", "texto"),
        new("egresoFolio", "Egreso 07", "texto"),
        new("egresoUuid", "UUID egreso", "texto"),
        new("fechaAplicacion", "Fecha aplicación", "fecha"),
        new("estadoEgreso", "Estado egreso", "texto"),
        new("cobradoSubtotal", "Cobrado base", "moneda", "derecha"),
        new("cobradoIva", "Cobrado IVA", "moneda", "derecha"),
        new("cobrado", "Cobrado", "moneda", "derecha"),
        new("aplicadoSubtotal", "Aplicado base", "moneda", "derecha"),
        new("aplicadoIva", "Aplicado IVA", "moneda", "derecha"),
        new("aplicado", "Aplicado", "moneda", "derecha"),
        new("comprometido", "Comprometido", "moneda", "derecha"),
        new("saldo", "Saldo", "moneda", "derecha"),
        new("estado", "Estado", "texto"),
        new("observacion", "Observación", "texto"),
    ];

    private static ComparativaAnticipoFila Aplicacion(
        Guid facturaAnticipoId, Guid anticipoId, string sucursal, string cliente,
        string folioAnticipo, string? uuidAnticipo, DateTimeOffset? fechaAnticipo,
        string movimiento, string? facturaFolio, string? facturaUuid,
        string? egresoFolio, string? egresoUuid, DateTimeOffset? fechaAplicacion, string? estadoEgreso,
        decimal? aplicadoSubtotal, decimal? aplicadoIva, decimal? aplicado, decimal? comprometido,
        string? observacion) =>
        new(sucursal, cliente, folioAnticipo, uuidAnticipo, fechaAnticipo,
            movimiento, facturaFolio, facturaUuid, egresoFolio, egresoUuid, fechaAplicacion, estadoEgreso,
            null, null, null, aplicadoSubtotal, aplicadoIva, aplicado, comprometido, null,
            string.Empty, observacion, anticipoId, facturaAnticipoId);

    private static string DescribirEstado(EstadoTimbrado estado) => estado switch
    {
        EstadoTimbrado.Timbrado => "Timbrado",
        EstadoTimbrado.CancelacionPendiente => "Cancelación pendiente",
        EstadoTimbrado.Cancelado => "Cancelado",
        EstadoTimbrado.TimbradoFallido => "Timbrado fallido",
        EstadoTimbrado.TimbradoEnProceso => "Timbrado en proceso",
        EstadoTimbrado.Descartada => "Descartado",
        _ => "Borrador",
    };
}

public sealed class ComparativaAnticiposValidator : AbstractValidator<ComparativaAnticiposQuery>
{
    public ComparativaAnticiposValidator()
    {
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId.HasValue)
            .WithMessage("La sucursal no es válida.");
        RuleFor(q => q.ClienteId).NotEqual(Guid.Empty).When(q => q.ClienteId.HasValue)
            .WithMessage("El cliente no es válido.");
        RuleFor(q => q.Estado).IsInEnum().When(q => q.Estado.HasValue)
            .WithMessage("El estado no es válido.");
        RuleFor(q => q)
            .Must(q => q.Desde!.Value <= q.Hasta!.Value)
            .When(q => q.Desde.HasValue && q.Hasta.HasValue)
            .WithName("Desde")
            .WithMessage("La fecha inicial no puede ser posterior a la final.");
        RuleFor(q => q)
            .Must(q => q.Corte!.Value >= q.Desde!.Value)
            .When(q => q.Desde.HasValue && q.Corte.HasValue)
            .WithName("Corte")
            .WithMessage("La fecha de corte no puede ser anterior a la fecha inicial.");
    }
}
