using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.Reposiciones;

// ============================================================================
// GI-PR1 (doc 12 §D2/Q1/Q4): reposición agregada de caja chica.
// El saldo por reponer de una (sucursal, destino) = Σ MontoTotal de las
// comprobaciones de caja chica Aplicadas sin reposición. Al Aplicar se
// emite automáticamente si el saldo alcanza el mínimo configurado; el
// corte manual vacía el saldo sin alcanzar el mínimo. La emisión publica
// pasivo.autorizado-para-pago.v1 con TipoBeneficiario CajaSucursal|Empleado
// y OrigenTipo ReposicionCajaChica (idempotencia por OrigenId).
// ============================================================================

/// <summary>
/// Emisor compartido entre <c>AplicarComprobacionGastosHandler</c>
/// (respeta el mínimo) y el corte manual (no lo respeta). NO llama
/// <c>SaveChanges</c> — el caller persiste en su propia transacción para
/// que el outbox drene los eventos atómicamente (ADR-0009).
/// </summary>
public sealed class ReposicionCajaChicaEmisor
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IIntegrationEventPublisher _events;

    public ReposicionCajaChicaEmisor(
        CuentasPorPagarDbContext db, IIntegrationEventPublisher events)
    {
        _db = db; _events = events;
    }

    /// <summary>
    /// Emite la reposición de <c>(sucursal, destino)</c> si hay saldo y —
    /// cuando <paramref name="respetarMinimo"/> — este alcanza el mínimo
    /// configurado. Devuelve null si no se emitió (sin saldo o bajo el
    /// mínimo). Las comprobaciones cubiertas quedan ligadas vía
    /// <c>ReposicionId</c>.
    /// </summary>
    public async Task<ReposicionCajaChica?> EmitirSiCorrespondeAsync(
        Guid empresaId,
        Guid sucursalId,
        DestinoReposicionCaja destino,
        bool respetarMinimo,
        Guid? usuarioId,
        DateTimeOffset ahora,
        CancellationToken cancellationToken,
        Guid? incluirComprobacionId = null)
    {
        // incluirComprobacionId: la comprobación que se está aplicando en
        // esta misma TX aún no está persistida como Aplicada — el WHERE
        // corre sobre valores de BD, así que se incluye por Id y la
        // identity resolution de EF regresa la instancia tracked (ya
        // Aplicada en memoria).
        var pendientes = await _db.ComprobacionesGastos
            .Where(c => c.Tipo == TipoComprobacionGastos.ReembolsoCajaChica
                && c.SucursalId == sucursalId
                && c.DestinoReposicion == destino
                && c.ReposicionId == null
                && (c.Estado == EstadoComprobacionGastos.Aplicada || c.Id == incluirComprobacionId))
            .ToListAsync(cancellationToken);

        pendientes = pendientes
            .Where(c => c.Estado == EstadoComprobacionGastos.Aplicada && c.ReposicionId == null)
            .ToList();

        if (pendientes.Count == 0) return null;

        // Multi-moneda: se agrupa por moneda; cada grupo con saldo
        // suficiente emite su propia reposición (los pagos no se mezclan).
        ReposicionCajaChica? ultima = null;
        foreach (var grupo in pendientes.GroupBy(c => c.Moneda))
        {
            var saldo = grupo.Sum(c => c.MontoTotal);
            if (respetarMinimo)
            {
                var minimo = await _db.ConfiguracionesReposicionCaja
                    .AsNoTracking()
                    .Where(x => x.SucursalId == sucursalId)
                    .Select(x => (decimal?)x.MontoMinimo)
                    .FirstOrDefaultAsync(cancellationToken) ?? 0m;
                if (saldo < minimo) continue;
            }

            var beneficiarioId = destino == DestinoReposicionCaja.CuentaSucursal
                ? sucursalId
                : grupo.OrderByDescending(c => c.FechaAplicacion).First().ResponsableId;

            var reposicion = ReposicionCajaChica.Emitir(
                empresaId: empresaId,
                sucursalId: sucursalId,
                destino: destino,
                beneficiarioId: beneficiarioId,
                moneda: grupo.Key,
                montoTotal: saldo,
                numeroComprobaciones: grupo.Count(),
                esCorteManual: !respetarMinimo,
                emitidaPor: usuarioId,
                ahora: ahora);

            _db.ReposicionesCajaChica.Add(reposicion);
            foreach (var c in grupo)
            {
                c.AsignarReposicion(reposicion.Id);
            }

            await _events.PublishAsync(new PasivoAutorizadoParaPagoIntegrationEvent(
                EmpresaId: empresaId,
                OcurridoEn: ahora,
                FacturaProveedorId: Guid.Empty,
                ProveedorId: Guid.Empty,
                OrdenCompraId: null,
                MontoTotal: saldo,
                SaldoPendiente: saldo,
                Moneda: grupo.Key,
                TipoCambio: null,
                FechaVencimiento: DateOnly.FromDateTime(ahora.UtcDateTime),
                UuidCfdi: null,
                FolioProveedor: null,
                MetodoPago: null,
                TipoBeneficiario: destino == DestinoReposicionCaja.CuentaSucursal
                    ? PasivoAutorizadoParaPagoIntegrationEvent.BeneficiarioCajaSucursal
                    : PasivoAutorizadoParaPagoIntegrationEvent.BeneficiarioEmpleado,
                BeneficiarioId: beneficiarioId,
                OrigenTipo: PasivoAutorizadoParaPagoIntegrationEvent.OrigenReposicionCajaChica,
                OrigenId: reposicion.Id), cancellationToken);

            ultima = reposicion;
        }

        return ultima;
    }
}

// ------------------------------------------------------------- Corte manual

public sealed record EmitirReposicionManualCommand(
    Guid SucursalId,
    DestinoReposicionCaja Destino) : IRequest<EmitirReposicionManualResponse>;

public sealed record EmitirReposicionManualResponse(
    Guid ReposicionId,
    decimal MontoTotal,
    int NumeroComprobaciones,
    string Moneda);

public sealed class EmitirReposicionManualValidator : AbstractValidator<EmitirReposicionManualCommand>
{
    public EmitirReposicionManualValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.Destino).IsInEnum();
    }
}

public sealed class EmitirReposicionManualHandler
    : IRequestHandler<EmitirReposicionManualCommand, EmitirReposicionManualResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ReposicionCajaChicaEmisor _emisor;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public EmitirReposicionManualHandler(
        CuentasPorPagarDbContext db,
        ReposicionCajaChicaEmisor emisor,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _emisor = emisor; _currentEmpresa = currentEmpresa;
        _currentUser = currentUser; _clock = clock;
    }

    public async Task<EmitirReposicionManualResponse> Handle(
        EmitirReposicionManualCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var reposicion = await _emisor.EmitirSiCorrespondeAsync(
            empresaId, command.SucursalId, command.Destino,
            respetarMinimo: false,
            usuarioId: _currentUser.UserId,
            ahora: _clock.UtcNow,
            cancellationToken)
            ?? throw new BusinessRuleException(
                "REPO_SALDO_CERO",
                "No hay comprobaciones aplicadas pendientes de reposición para esa sucursal y destino.");

        await _db.SaveChangesAsync(cancellationToken);

        return new EmitirReposicionManualResponse(
            ReposicionId: reposicion.Id,
            MontoTotal: reposicion.MontoTotal,
            NumeroComprobaciones: reposicion.NumeroComprobaciones,
            Moneda: reposicion.Moneda);
    }
}

// ------------------------------------------------------------ Configuración

public sealed record ConfigurarReposicionCajaCommand(
    Guid SucursalId,
    decimal MontoMinimo) : IRequest<ConfiguracionReposicionResponse>;

public sealed record ConfiguracionReposicionResponse(
    Guid SucursalId,
    decimal MontoMinimo);

public sealed class ConfigurarReposicionCajaValidator : AbstractValidator<ConfigurarReposicionCajaCommand>
{
    public ConfigurarReposicionCajaValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.MontoMinimo).GreaterThanOrEqualTo(0);
    }
}

public sealed class ConfigurarReposicionCajaHandler
    : IRequestHandler<ConfigurarReposicionCajaCommand, ConfiguracionReposicionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ConfigurarReposicionCajaHandler(
        CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _currentEmpresa = currentEmpresa;
    }

    public async Task<ConfiguracionReposicionResponse> Handle(
        ConfigurarReposicionCajaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var config = await _db.ConfiguracionesReposicionCaja
            .FirstOrDefaultAsync(c => c.SucursalId == command.SucursalId, cancellationToken);

        if (config is null)
        {
            config = ConfiguracionReposicionCaja.Crear(empresaId, command.SucursalId, command.MontoMinimo);
            _db.ConfiguracionesReposicionCaja.Add(config);
        }
        else
        {
            config.ActualizarMinimo(command.MontoMinimo);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ConfiguracionReposicionResponse(config.SucursalId, config.MontoMinimo);
    }
}

// ------------------------------------------------------------------ Queries

public sealed record ListarReposicionesQuery(
    Guid? SucursalId, int Offset, int Limit) : IRequest<PagedResponse<ReposicionListItemResponse>>;

public sealed record ReposicionListItemResponse(
    Guid Id,
    Guid SucursalId,
    DestinoReposicionCaja Destino,
    Guid BeneficiarioId,
    string Moneda,
    decimal MontoTotal,
    int NumeroComprobaciones,
    bool EsCorteManual,
    DateTimeOffset FechaEmision);

public sealed class ListarReposicionesHandler
    : IRequestHandler<ListarReposicionesQuery, PagedResponse<ReposicionListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarReposicionesHandler(CuentasPorPagarDbContext db) => _db = db;

    public async Task<PagedResponse<ReposicionListItemResponse>> Handle(
        ListarReposicionesQuery query, CancellationToken cancellationToken)
    {
        var q = _db.ReposicionesCajaChica.AsNoTracking();
        if (query.SucursalId is Guid sucursalId)
            q = q.Where(r => r.SucursalId == sucursalId);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(r => r.FechaEmision)
            .Skip(query.Offset).Take(query.Limit)
            .Select(r => new ReposicionListItemResponse(
                r.Id, r.SucursalId, r.Destino, r.BeneficiarioId, r.Moneda,
                r.MontoTotal, r.NumeroComprobaciones, r.EsCorteManual, r.FechaEmision))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ReposicionListItemResponse>(items, query.Offset, query.Limit, total);
    }
}

public sealed record ListarSaldosPendientesQuery() : IRequest<IReadOnlyList<SaldoPendienteResponse>>;

public sealed record SaldoPendienteResponse(
    Guid SucursalId,
    DestinoReposicionCaja Destino,
    string Moneda,
    decimal Saldo,
    int NumeroComprobaciones,
    decimal MontoMinimo);

public sealed class ListarSaldosPendientesHandler
    : IRequestHandler<ListarSaldosPendientesQuery, IReadOnlyList<SaldoPendienteResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarSaldosPendientesHandler(CuentasPorPagarDbContext db) => _db = db;

    public async Task<IReadOnlyList<SaldoPendienteResponse>> Handle(
        ListarSaldosPendientesQuery query, CancellationToken cancellationToken)
    {
        var saldos = await _db.ComprobacionesGastos
            .AsNoTracking()
            .Where(c => c.Tipo == TipoComprobacionGastos.ReembolsoCajaChica
                && c.Estado == EstadoComprobacionGastos.Aplicada
                && c.ReposicionId == null
                && c.DestinoReposicion != null)
            .GroupBy(c => new { c.SucursalId, c.DestinoReposicion, c.Moneda })
            .Select(g => new
            {
                g.Key.SucursalId,
                Destino = g.Key.DestinoReposicion!.Value,
                g.Key.Moneda,
                Saldo = g.Sum(c => c.MontoTotal),
                Numero = g.Count(),
            })
            .ToListAsync(cancellationToken);

        var minimos = await _db.ConfiguracionesReposicionCaja
            .AsNoTracking()
            .ToDictionaryAsync(c => c.SucursalId, c => c.MontoMinimo, cancellationToken);

        return saldos
            .Select(s => new SaldoPendienteResponse(
                s.SucursalId, s.Destino, s.Moneda, s.Saldo, s.Numero,
                minimos.GetValueOrDefault(s.SucursalId, 0m)))
            .OrderByDescending(s => s.Saldo)
            .ToList();
    }
}
