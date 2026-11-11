using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Cobranza;
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

    /// <summary>
    /// Días antes del límite SOLUNION en que aparece la alerta (CA7.11: la
    /// alerta debe salir antes de los 90 días para reportar el impago a
    /// tiempo). Supuesto C7.5 «a validar»: 10 días ⇒ alerta desde 80.
    /// </summary>
    public int SolunionDiasAnticipacion { get; set; } = 10;

    /// <summary>
    /// Tolerancia del bloqueo automático por vencimiento (C7.5, R7/P7.2):
    /// días de gracia después del plazo del cliente. Se bloquea cuando
    /// han pasado más de plazo + tolerancia días desde el timbrado.
    /// </summary>
    public int ToleranciaDiasVencido { get; set; } = 15;
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
                f.FechaTimbrado,
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
        var cambios = false;

        foreach (var grupo in cartera.GroupBy(f => (f.EmpresaId, f.ClienteId, f.Moneda)))
        {
            var (empresaId, clienteId, moneda) = grupo.Key;
            var linea = lineaPor.GetValueOrDefault((clienteId, moneda));
            var diasVencidosMax = grupo.Max(f =>
                hoy.DayNumber - DateOnly.FromDateTime(f.FechaVencimiento.UtcDateTime).DayNumber);
            var saldoTotal = grupo.Sum(f => f.Saldo);

            // 1. SOLUNION 90d — solo cartera asegurada (línea origen SOLUNION).
            // Avisa con anticipación para reportar el impago antes de perder
            // la cobertura (CA7.11).
            var umbralSolunion = _options.SolunionDiasVencido - Math.Max(0, _options.SolunionDiasAnticipacion);
            if (linea is { Origen: OrigenLineaCredito.Solunion }
                && diasVencidosMax >= umbralSolunion)
            {
                var faltan = _options.SolunionDiasVencido - diasVencidosMax;
                var cuando = faltan > 0
                    ? $"faltan {faltan} día(s) para el límite de {_options.SolunionDiasVencido}"
                    : $"límite de siniestro: {_options.SolunionDiasVencido}";
                creadas += await CrearSiNoAbiertaAsync(
                    yaAbiertas, empresaId, clienteId, TipoAlertaCartera.Solunion90d, moneda,
                    $"Cartera asegurada SOLUNION con factura(s) vencida(s) {diasVencidosMax} días ({cuando}).",
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

            // 3. Bloqueo automático por vencimiento (C7.5): la factura más
            // vencida rebasa plazo del cliente + tolerancia. Solo si la línea
            // sigue Activa y lo vencido no quedó cubierto por un desbloqueo
            // manual autorizado.
            if (linea is { Estado: EstadoLineaCredito.Activa })
            {
                var tolerancia = Math.Max(0, _options.ToleranciaDiasVencido);
                var rebasan = BloqueoPorVencimiento.FacturasQueRebasan(
                    grupo.Select(f => f.FechaTimbrado), linea.PlazoDias, tolerancia, hoy);
                if (BloqueoPorVencimiento.DebeBloquear(rebasan, linea.DesbloqueoManualEn))
                {
                    var diasMax = grupo.Max(f => BloqueoPorVencimiento.DiasDesdeTimbrado(f.FechaTimbrado, hoy));
                    var motivo = $"Bloqueo automático: factura con {diasMax} días desde su emisión, más que el plazo de {linea.PlazoDias} + {tolerancia} de tolerancia.";
                    linea.BloquearPorVencimiento(motivo);
                    cambios = true;
                    creadas += await CrearSiNoAbiertaAsync(
                        yaAbiertas, empresaId, clienteId, TipoAlertaCartera.AutoBloqueoVencimiento, moneda,
                        motivo, ahora, cancellationToken);
                    _logger.LogWarning(
                        "[AlertasCartera] Línea de crédito {LineaId} bloqueada por vencimiento. Cliente={ClienteId} Moneda={Moneda}.",
                        linea.Id, clienteId, moneda);
                }
            }
        }

        // 4. Desbloqueo automático (C7.5-a) de las líneas bloqueadas por
        // vencimiento cuyo cliente ya no tiene facturas fuera de plazo +
        // tolerancia (red de seguridad del desbloqueo al aplicar el pago).
        var bloqueadas = await _db.LineasCredito
            .Where(l => l.Estado == EstadoLineaCredito.Bloqueada && l.BloqueoAutomatico)
            .ToListAsync(cancellationToken);
        foreach (var linea in bloqueadas)
        {
            var timbrados = cartera
                .Where(f => f.ClienteId == linea.ClienteId && f.Moneda == linea.Moneda && f.Saldo > 0)
                .Select(f => f.FechaTimbrado);
            if (BloqueoPorVencimiento.FacturasQueRebasan(
                    timbrados, linea.PlazoDias, Math.Max(0, _options.ToleranciaDiasVencido), hoy).Count == 0
                && linea.DesbloquearPorPago())
            {
                cambios = true;
                _logger.LogInformation(
                    "[AlertasCartera] Línea de crédito {LineaId} desbloqueada: sin facturas fuera de plazo + tolerancia.",
                    linea.Id);
            }
        }

        // 5. M-241 (CXC-GESTION): toda factura vencida tiene su ficha de cobranza. Se abre una sola vez (idempotente: no se toca
        // la que ya existe) con el último gestor del cliente como responsable por omisión; sin gestor previo queda «sin asignar».
        // Supuesto a validar con Millet: la regla del responsable por omisión.
        cambios |= await AbrirFichasDeVencidasAsync(ahora, cancellationToken);

        if (creadas > 0 || cambios)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[AlertasCartera] Evaluación completada: {N} alerta(s) nueva(s).", creadas);
        }

        return creadas;
    }

    private async Task<bool> AbrirFichasDeVencidasAsync(DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        var sinFicha = await _db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId != null
                     && (f.Estado == EstadoFacturaCartera.Abierta || f.Estado == EstadoFacturaCartera.Parcial)
                     && f.FechaVencimiento < ahora
                     && !_db.FichasCobranza.Any(x => x.FacturaCarteraId == f.Id))
            .Select(f => new { f.Id, f.EmpresaId, ClienteId = f.ClienteId!.Value })
            .ToListAsync(cancellationToken);
        if (sinFicha.Count == 0) return false;

        var clientes = sinFicha.Select(f => f.ClienteId).Distinct().ToList();
        var gestores = (await _db.SeguimientosCobranza.AsNoTracking()
                .Where(s => clientes.Contains(s.ClienteId))
                .Select(s => new { s.ClienteId, s.UsuarioId, s.Fecha })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.ClienteId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Fecha).First().UsuarioId);

        foreach (var f in sinFicha)
        {
            Guid? responsable = gestores.TryGetValue(f.ClienteId, out var u) ? u : null;
            var (ficha, evento) = FichaCobranza.AbrirAlVencer(f.EmpresaId, f.Id, f.ClienteId, responsable, null, ahora);
            _db.FichasCobranza.Add(ficha);
            _db.FichaCobranzaEventos.Add(evento);
        }
        _logger.LogInformation("[AlertasCartera] {N} ficha(s) de cobranza abierta(s) para facturas vencidas.", sinFicha.Count);
        return true;
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
