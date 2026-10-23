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
// P8.5 (86e3kdw2k): sin mínimo. La reposición nace al aprobar el pasivo de
// CADA comprobación de caja chica (una reposición por comprobación). El
// corte manual solo vacía el saldo de comprobaciones Aplicadas heredadas
// sin reposición (Σ MontoTotal por sucursal, destino). La emisión publica
// pasivo.autorizado-para-pago.v1 con TipoBeneficiario CajaSucursal|Empleado
// y OrigenTipo ReposicionCajaChica (idempotencia por OrigenId).
// ============================================================================

/// <summary>
/// Emisor compartido entre la aprobación de pasivo / <c>Aplicar</c> legado
/// (una sola comprobación) y el corte manual. NO llama
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
    /// Emite la reposición de <c>(sucursal, destino)</c> si hay saldo. Con
    /// <paramref name="soloComprobacionId"/> cubre únicamente esa comprobación;
    /// sin él, todas las Aplicadas sin reposición. Devuelve null si no hay
    /// nada que reponer. Las comprobaciones cubiertas quedan ligadas vía
    /// <c>ReposicionId</c>.
    /// </summary>
    public async Task<ReposicionCajaChica?> EmitirSiCorrespondeAsync(
        Guid empresaId,
        Guid sucursalId,
        DestinoReposicionCaja destino,
        bool esCorteManual,
        Guid? usuarioId,
        DateTimeOffset ahora,
        CancellationToken cancellationToken,
        Guid? soloComprobacionId = null)
    {
        // soloComprobacionId: la comprobación que se está aprobando en esta
        // misma TX aún no está persistida como Aplicada — el WHERE corre
        // sobre valores de BD, así que se filtra por Id y la identity
        // resolution de EF regresa la instancia tracked (ya Aplicada en
        // memoria).
        var pendientes = await _db.ComprobacionesGastos
            .Where(c => c.Tipo == TipoComprobacionGastos.ReembolsoCajaChica
                && c.SucursalId == sucursalId
                && c.DestinoReposicion == destino
                && c.ReposicionId == null
                && (soloComprobacionId != null
                    ? c.Id == soloComprobacionId
                    : c.Estado == EstadoComprobacionGastos.Aplicada))
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
                esCorteManual: esCorteManual,
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
            esCorteManual: true,
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

// ------------------------------------------------------------------ Queries

public sealed record ListarReposicionesQuery(
    Guid? SucursalId, int Offset, int Limit) : IRequest<PagedResponse<ReposicionListItemResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "reposicion_caja";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

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
        var q = _db.ReposicionesCajaChica
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking();
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

public sealed record ListarSaldosPendientesQuery() : IRequest<IReadOnlyList<SaldoPendienteResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "comprobacion";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed record SaldoPendienteResponse(
    Guid SucursalId,
    DestinoReposicionCaja Destino,
    string Moneda,
    decimal Saldo,
    int NumeroComprobaciones);

public sealed class ListarSaldosPendientesHandler
    : IRequestHandler<ListarSaldosPendientesQuery, IReadOnlyList<SaldoPendienteResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarSaldosPendientesHandler(CuentasPorPagarDbContext db) => _db = db;

    public async Task<IReadOnlyList<SaldoPendienteResponse>> Handle(
        ListarSaldosPendientesQuery query, CancellationToken cancellationToken)
    {
        var saldos = await _db.ComprobacionesGastos
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id))
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

        return saldos
            .Select(s => new SaldoPendienteResponse(
                s.SucursalId, s.Destino, s.Moneda, s.Saldo, s.Numero))
            .OrderByDescending(s => s.Saldo)
            .ToList();
    }
}
