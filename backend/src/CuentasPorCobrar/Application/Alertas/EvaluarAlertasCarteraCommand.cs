using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.Alertas;

/// <summary>Umbrales del worker de alertas (CXC-PR8) — configurables por ambiente.</summary>
public sealed class AlertasCarteraOptions
{
    public const string SectionName = "CuentasPorCobrar:Alertas";

    public bool Disabled { get; set; }
    public int IntervalHours { get; set; } = 24;

    /// <summary>Días vencidos para alertar cartera asegurada SOLUNION (límite de siniestro).</summary>
    public int SolunionDiasVencido { get; set; } = 90;

    /// <summary>Días vencidos que disparan el auto-bloqueo de la línea.</summary>
    public int AutoBloqueoDiasVencido { get; set; } = 90;
}

/// <summary>
/// Evaluación de alertas de cartera (CXC-PR8, levantamiento §3.1): la
/// dispara <c>AlertaCarteraWorker</c> (diaria) pero vive como command
/// para poder ejercitarla en tests y re-correrla manualmente si hiciera
/// falta. Idempotente por día operativo: no duplica una alerta del mismo
/// (cliente, tipo, moneda) mientras haya una sin atender.
/// </summary>
public sealed record EvaluarAlertasCarteraCommand : IRequest<int>;

public sealed class EvaluarAlertasCarteraHandler : IRequestHandler<EvaluarAlertasCarteraCommand, int>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly AlertasCarteraOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<EvaluarAlertasCarteraHandler> _logger;

    public EvaluarAlertasCarteraHandler(
        CuentasPorCobrarDbContext db,
        IIntegrationEventPublisher eventos,
        IOptions<AlertasCarteraOptions> options,
        IClock clock,
        ILogger<EvaluarAlertasCarteraHandler> logger)
    {
        _db = db; _eventos = eventos; _options = options.Value; _clock = clock; _logger = logger;
    }

    public async Task<int> Handle(EvaluarAlertasCarteraCommand request, CancellationToken cancellationToken)
    {
        var ahora = _clock.UtcNow;
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);

        // Cartera viva agrupada por (empresa, cliente, moneda).
        var cartera = await _db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId != null
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial))
            .Select(f => new
            {
                f.EmpresaId,
                ClienteId = f.ClienteId!.Value,
                f.Moneda,
                f.FechaVencimiento,
                Saldo = f.Total - f.MontoPagado - f.MontoNc,
            })
            .ToListAsync(cancellationToken);

        var lineas = await _db.LineasCredito
            .Where(l => l.Estado == EstadoLineaCredito.Activa)
            .ToListAsync(cancellationToken);
        var lineaPor = lineas.ToDictionary(l => (l.ClienteId, l.Moneda));

        var pendientes = await _db.AlertasCartera.AsNoTracking()
            .Where(a => !a.Atendida)
            .Select(a => new { a.ClienteId, a.Tipo, a.Moneda })
            .ToListAsync(cancellationToken);
        var yaAbiertas = pendientes.Select(a => (a.ClienteId, a.Tipo, a.Moneda)).ToHashSet();

        var creadas = 0;

        foreach (var grupo in cartera.GroupBy(f => (f.EmpresaId, f.ClienteId, f.Moneda)))
        {
            var (empresaId, clienteId, moneda) = grupo.Key;
            var linea = lineaPor.GetValueOrDefault((clienteId, moneda));
            var diasVencidosMax = grupo.Max(f =>
                hoy.DayNumber - DateOnly.FromDateTime(f.FechaVencimiento.UtcDateTime).DayNumber);
            var saldoTotal = grupo.Sum(f => f.Saldo);

            // 1. SOLUNION 90d — solo cartera asegurada (línea origen SOLUNION).
            if (linea is { Origen: OrigenLineaCredito.Solunion }
                && diasVencidosMax >= _options.SolunionDiasVencido)
            {
                creadas += await CrearSiNoAbiertaAsync(
                    yaAbiertas, empresaId, clienteId, TipoAlertaCartera.Solunion90d, moneda,
                    $"Cartera asegurada SOLUNION con factura(s) vencida(s) {diasVencidosMax} días (límite de siniestro: {_options.SolunionDiasVencido}).",
                    ahora, cancellationToken);
            }

            // 2. Exceso de crédito — saldo excedido §2.2.
            if (linea is not null && saldoTotal > linea.Limite)
            {
                creadas += await CrearSiNoAbiertaAsync(
                    yaAbiertas, empresaId, clienteId, TipoAlertaCartera.ExcesoCredito, moneda,
                    $"Saldo {saldoTotal} {moneda} excede el límite {linea.Limite} (excedido: {saldoTotal - linea.Limite}).",
                    ahora, cancellationToken);
            }

            // 3. Auto-bloqueo por vencimientos — bloquea la línea con motivo
            // automático (levantamiento §3.1). Solo si sigue Activa.
            if (linea is { Estado: EstadoLineaCredito.Activa }
                && diasVencidosMax >= _options.AutoBloqueoDiasVencido)
            {
                var motivo = $"Auto-bloqueo: factura(s) vencida(s) {diasVencidosMax} días (umbral {_options.AutoBloqueoDiasVencido}).";
                linea.Bloquear(motivo);
                creadas += await CrearSiNoAbiertaAsync(
                    yaAbiertas, empresaId, clienteId, TipoAlertaCartera.AutoBloqueoVencimiento, moneda,
                    motivo, ahora, cancellationToken);
                _logger.LogWarning(
                    "[AlertasCartera] Línea de crédito {LineaId} auto-bloqueada. Cliente={ClienteId} Moneda={Moneda}.",
                    linea.Id, clienteId, moneda);
            }
        }

        if (creadas > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[AlertasCartera] Evaluación completada: {N} alerta(s) nueva(s).", creadas);
        }

        return creadas;
    }

    private async Task<int> CrearSiNoAbiertaAsync(
        HashSet<(Guid, TipoAlertaCartera, string)> yaAbiertas,
        Guid empresaId, Guid clienteId, TipoAlertaCartera tipo, string moneda,
        string detalle, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        if (!yaAbiertas.Add((clienteId, tipo, moneda))) return 0;

        var alerta = new AlertaCartera(empresaId, clienteId, tipo, moneda, detalle, ahora);
        _db.AlertasCartera.Add(alerta);

        // PLATFORM-TODO(<Notificaciones>): cuando exista el motor transversal
        // de notificaciones, este evento alimentará correo/push; hoy la alerta
        // solo es visible en la bandeja /cxc/alertas.
        await _eventos.PublishAsync(new AlertaCarteraGeneradaEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            AlertaId: alerta.Id,
            ClienteId: clienteId,
            Tipo: tipo.ToString(),
            Moneda: moneda,
            Detalle: detalle), cancellationToken);

        return 1;
    }
}
