using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.ConciliacionPolizas;

// ============================================================================
// CXC-06 (86e3qddef): conciliación de los cobros aplicados en cartera contra
// sus pólizas. Cada cobro se cruza con su documento contable:
//   • REP de una propuesta → póliza del pago de cliente (#12, por movimiento bancario);
//   • cobro de mostrador   → póliza del cobro (#11a, parte no efectivo) + liquidación
//                            del efectivo en el cierre de caja (#11b).
// Lo que no cuadra se lista con su causa probable. Distinto de C7.7 (cartera
// contra Facturación).
// ============================================================================

public enum EstadoConciliacionCobro { Cuadra = 1, Diferencia = 2, EnTransito = 3 }

public sealed record ConciliacionCobroFila(
    string Documento, Guid DocumentoId, Guid? ClienteId, string? Cliente, string Moneda, DateOnly Fecha,
    decimal ImporteCartera, decimal ImportePolizas, decimal Diferencia, IReadOnlyList<string> Polizas,
    EstadoConciliacionCobro Estado, string? Causa);

public sealed record ConciliacionCobrosResponse(
    DateOnly Desde, DateOnly Hasta, int Cobros, int Cuadran, int ConDiferencia, int EnTransito,
    IReadOnlyList<ConciliacionCobroFila> Filas);

public sealed record ConciliacionCobrosPolizasQuery(DateOnly Desde, DateOnly Hasta, Guid? ClienteId = null)
    : IRequest<ConciliacionCobrosResponse>;

public sealed class ConciliacionCobrosPolizasValidator : AbstractValidator<ConciliacionCobrosPolizasQuery>
{
    public ConciliacionCobrosPolizasValidator()
    {
        RuleFor(q => q.Hasta).GreaterThanOrEqualTo(q => q.Desde).WithMessage("La fecha final no puede ser anterior a la inicial.");
        RuleFor(q => q).Must(q => q.Hasta.DayNumber - q.Desde.DayNumber <= 92)
            .WithName("Hasta").WithMessage("El rango máximo es de tres meses.");
    }
}

public sealed class ConciliacionCobrosPolizasHandler(CuentasPorCobrarDbContext db, ICobrosContabilizadosReadPort contabilidad)
    : IRequestHandler<ConciliacionCobrosPolizasQuery, ConciliacionCobrosResponse>
{
    public const string DocRep = "REP";
    public const string DocCobroMostrador = "Cobro de mostrador";
    public const string DocPagoSinRep = "Pago confirmado";

    public async Task<ConciliacionCobrosResponse> Handle(ConciliacionCobrosPolizasQuery request, CancellationToken cancellationToken)
    {
        var q = request;
        var ct = cancellationToken;
        var desde = new DateTimeOffset(q.Desde.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var hasta = new DateTimeOffset(q.Hasta.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);

        // 1. Cartera: pagos aplicados (no revertidos) por comprobante origen (REP o cobro de mostrador).
        var movimientos = await (from m in db.MovimientosCartera.AsNoTracking()
                                 join f in db.FacturasCartera.AsNoTracking() on m.FacturaCarteraId equals f.Id
                                 where m.Tipo == TipoMovimientoCartera.Pago && !m.Revertido
                                       && m.FechaMovimiento >= desde && m.FechaMovimiento < hasta
                                       && (q.ClienteId == null || f.ClienteId == q.ClienteId)
                                 select new { m.OrigenComprobanteId, m.Importe, m.FechaMovimiento, f.ClienteId, f.ReceptorNombre, f.Moneda })
            .ToListAsync(ct);
        var cartera = movimientos
            .Where(m => FechaLocal(m.FechaMovimiento) >= q.Desde && FechaLocal(m.FechaMovimiento) <= q.Hasta)
            .GroupBy(m => m.OrigenComprobanteId)
            .Select(g => new
            {
                Origen = g.Key, Importe = g.Sum(x => x.Importe), Fecha = FechaLocal(g.Min(x => x.FechaMovimiento)),
                g.First().ClienteId, Cliente = g.First().ReceptorNombre, g.First().Moneda,
            })
            .ToList();

        // 2. ¿Qué cobros son REP de una propuesta? (el REP guarda su propuesta; la póliza #12 va por movimiento bancario).
        var origenes = cartera.Select(c => c.Origen).ToList();
        var propuestasRep = await db.PropuestasAplicacionPago.AsNoTracking()
            .Where(p => p.ReciboPagoId != null && origenes.Contains(p.ReciboPagoId.Value))
            .Select(p => new { Rep = p.ReciboPagoId!.Value, p.MovimientoBancarioId })
            .ToListAsync(ct);
        var movimientoDelRep = propuestasRep.Where(p => p.MovimientoBancarioId != null)
            .ToDictionary(p => p.Rep, p => p.MovimientoBancarioId!.Value);

        var porMovimiento = cartera.Where(c => movimientoDelRep.ContainsKey(c.Origen))
            .GroupBy(c => movimientoDelRep[c.Origen]).ToList();
        var cobrosMostrador = cartera.Where(c => !movimientoDelRep.ContainsKey(c.Origen)).ToList();

        // 3. Contabilidad: pólizas del rango y las de los documentos de cartera (aunque caigan fuera del rango).
        var polizasRango = await contabilidad.ListarAsync(q.Desde, q.Hasta, ct);
        var polizasPago = (await contabilidad.ListarPorDocumentosAsync(
                DocumentoCobroContable.PagoCliente, porMovimiento.Select(g => g.Key).ToList(), ct))
            .Concat(polizasRango.Where(p => p.TipoDocumento == DocumentoCobroContable.PagoCliente))
            .DistinctBy(p => (p.PolizaId, p.ClienteId, p.Moneda))
            .Where(p => q.ClienteId == null || p.ClienteId == q.ClienteId)
            .ToList();
        var polizasCobro = (await contabilidad.ListarPorDocumentosAsync(
                DocumentoCobroContable.CobroMostrador, cobrosMostrador.Select(c => c.Origen).ToList(), ct))
            .Concat(polizasRango.Where(p => p.TipoDocumento == DocumentoCobroContable.CobroMostrador))
            .DistinctBy(p => (p.PolizaId, p.ClienteId, p.Moneda))
            .Where(p => q.ClienteId == null || p.ClienteId == q.ClienteId)
            .ToList();
        var efectivo = (await contabilidad.EfectivoAsync(cobrosMostrador.Select(c => c.Origen).ToList(), ct))
            .GroupBy(e => e.CobroId).ToDictionary(g => g.Key, g => g.ToList());

        var filas = new List<ConciliacionCobroFila>();

        // REP ↔ pago de cliente.
        foreach (var g in porMovimiento)
        {
            var c = g.First();
            var polizas = polizasPago.Where(p => p.DocumentoId == g.Key).ToList();
            filas.Add(Fila(DocRep, g.Key, c.ClienteId, c.Cliente, c.Moneda, g.Min(x => x.Fecha), g.Sum(x => x.Importe),
                polizas.Sum(p => p.AbonoClientes), polizas, pendienteEfectivo: 0m,
                sinPoliza: "El pago confirmado no tiene póliza: revisar la bandeja de rechazos del motor contable.",
                duplicada: false));
        }

        // Cobro de mostrador ↔ póliza del cobro + liquidación del efectivo.
        foreach (var c in cobrosMostrador)
        {
            var polizas = polizasCobro.Where(p => p.DocumentoId == c.Origen).ToList();
            var efectivoCobro = efectivo.GetValueOrDefault(c.Origen) ?? [];
            var liquidado = efectivoCobro.Where(e => e.Liquidado).Sum(e => e.Importe);
            var pendiente = efectivoCobro.Where(e => !e.Liquidado).Sum(e => e.Importe);
            filas.Add(Fila(DocCobroMostrador, c.Origen, c.ClienteId, c.Cliente, c.Moneda, c.Fecha, c.Importe,
                polizas.Sum(p => p.AbonoClientes) + liquidado, polizas, pendiente,
                sinPoliza: "El cobro no tiene póliza: revisar la bandeja de rechazos del motor contable.",
                duplicada: polizas.Select(p => p.PolizaId).Distinct().Count() > 1));
        }

        // Pólizas sin cobro en cartera.
        var conCartera = porMovimiento.Select(g => g.Key).ToHashSet();
        foreach (var g in polizasPago.Where(p => !conCartera.Contains(p.DocumentoId) && p.Fecha >= q.Desde && p.Fecha <= q.Hasta)
                     .GroupBy(p => (p.DocumentoId, p.ClienteId, p.Moneda)))
        {
            var importe = g.Sum(p => p.AbonoClientes);
            filas.Add(new ConciliacionCobroFila(DocPagoSinRep, g.Key.DocumentoId, g.Key.ClienteId, null, g.Key.Moneda, g.Min(p => p.Fecha),
                0m, importe, -importe, g.Select(p => p.Folio).Distinct().ToList(), EstadoConciliacionCobro.EnTransito,
                "Tesorería confirmó el pago y ya tiene póliza; la cartera se afecta al timbrar el REP."));
        }
        var cobrosCartera = cobrosMostrador.Select(c => c.Origen).ToHashSet();
        foreach (var g in polizasCobro.Where(p => !cobrosCartera.Contains(p.DocumentoId) && p.Fecha >= q.Desde && p.Fecha <= q.Hasta)
                     .GroupBy(p => (p.DocumentoId, p.ClienteId, p.Moneda)))
        {
            var importe = g.Sum(p => p.AbonoClientes);
            filas.Add(new ConciliacionCobroFila(DocCobroMostrador, g.Key.DocumentoId, g.Key.ClienteId, null, g.Key.Moneda,
                g.Min(p => p.Fecha), 0m, importe, -importe, g.Select(p => p.Folio).Distinct().ToList(), EstadoConciliacionCobro.Diferencia,
                "La póliza no tiene cobro en cartera: el evento del cobro no se aplicó en CxC."));
        }

        // Nombre del cliente de las filas que solo vienen de Contabilidad.
        var sinNombre = filas.Where(f => f.Cliente is null && f.ClienteId != null).Select(f => f.ClienteId!.Value).Distinct().ToList();
        if (sinNombre.Count > 0)
        {
            var nombres = await db.FacturasCartera.AsNoTracking().Where(f => f.ClienteId != null && sinNombre.Contains(f.ClienteId.Value))
                .GroupBy(f => f.ClienteId!.Value).Select(g => new { g.Key, Nombre = g.Max(x => x.ReceptorNombre) })
                .ToDictionaryAsync(x => x.Key, x => x.Nombre, ct);
            filas = filas.Select(f => f.Cliente is null && f.ClienteId is Guid id && nombres.TryGetValue(id, out var n) ? f with { Cliente = n } : f)
                .ToList();
        }

        var ordenadas = filas.OrderBy(f => f.Estado == EstadoConciliacionCobro.Cuadra).ThenBy(f => f.Fecha).ThenBy(f => f.Cliente).ToList();
        return new ConciliacionCobrosResponse(q.Desde, q.Hasta, ordenadas.Count,
            ordenadas.Count(f => f.Estado == EstadoConciliacionCobro.Cuadra),
            ordenadas.Count(f => f.Estado == EstadoConciliacionCobro.Diferencia),
            ordenadas.Count(f => f.Estado == EstadoConciliacionCobro.EnTransito), ordenadas);
    }

    private static ConciliacionCobroFila Fila(string documento, Guid documentoId, Guid? clienteId, string? cliente, string moneda,
        DateOnly fecha, decimal importeCartera, decimal importePolizas, List<PolizaCobroLectura> polizas, decimal pendienteEfectivo,
        string sinPoliza, bool duplicada)
    {
        var diferencia = importeCartera - importePolizas;
        var folios = polizas.Select(p => p.Folio).Distinct().ToList();
        if (duplicada)
            return new(documento, documentoId, clienteId, cliente, moneda, fecha, importeCartera, importePolizas, diferencia, folios,
                EstadoConciliacionCobro.Diferencia, "El cobro tiene más de una póliza vigente: anular la duplicada.");
        if (diferencia == 0m)
            return new(documento, documentoId, clienteId, cliente, moneda, fecha, importeCartera, importePolizas, 0m, folios,
                EstadoConciliacionCobro.Cuadra, null);
        if (pendienteEfectivo > 0m && diferencia == pendienteEfectivo)
            return new(documento, documentoId, clienteId, cliente, moneda, fecha, importeCartera, importePolizas, diferencia, folios,
                EstadoConciliacionCobro.EnTransito, "Efectivo pendiente de liquidar: se contabiliza al cerrar la sesión de caja.");
        var causa = polizas.Count == 0 && importePolizas == 0m
            ? sinPoliza
            : diferencia > 0m
                ? "La póliza abona a clientes menos de lo aplicado en cartera (ajuste sin contabilizar o cobro parcial en la póliza)."
                : "La póliza abona a clientes más de lo aplicado en cartera (aplicación incompleta o revertida en CxC).";
        return new(documento, documentoId, clienteId, cliente, moneda, fecha, importeCartera, importePolizas, diferencia, folios,
            EstadoConciliacionCobro.Diferencia, causa);
    }

    /// <summary>Fecha contable local (ADR-0040), la misma que usa el motor para las pólizas.</summary>
    private static DateOnly FechaLocal(DateTimeOffset instante) => Millet.SharedKernel.Application.FechaContable.HoyLocal(instante);
}
