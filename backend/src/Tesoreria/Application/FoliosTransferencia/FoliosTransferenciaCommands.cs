using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.FoliosTransferencia;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.FoliosTransferencia;

public sealed record FolioTransferenciaResponse(
    Guid Id, string Folio, EstadoFolioTransferencia Estado, Guid MovimientoId, DateOnly FechaValor,
    string? ReferenciaBancaria, decimal ImporteIngreso, Guid ClienteId, string? ClienteClave,
    string? ClienteRazonSocial, string? Pedido, decimal Importe, Guid EmitidoPor, DateTimeOffset EmitidoEn,
    Guid? CobroMostradorId, DateTimeOffset? UsadoEn, string? MotivoCancelacion, DateTimeOffset? CanceladoEn,
    int Version);

/// <summary>Ingresos emite el folio desde un ingreso de cliente ya registrado (F6.6, CA6.8).</summary>
public sealed record EmitirFolioTransferenciaCommand(Guid MovimientoId, Guid ClienteId, decimal Importe, string? Pedido)
    : IRequest<FolioTransferenciaResponse>;

public sealed class EmitirFolioTransferenciaValidator : AbstractValidator<EmitirFolioTransferenciaCommand>
{
    public EmitirFolioTransferenciaValidator()
    {
        RuleFor(x => x.MovimientoId).NotEmpty();
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.Importe).GreaterThan(0);
        RuleFor(x => x.Pedido).MaximumLength(60);
    }
}

/// <summary>Ingresos cancela un folio que no se usó, con motivo.</summary>
public sealed record CancelarFolioTransferenciaCommand(Guid Id, string Motivo) : IRequest<FolioTransferenciaResponse>;

public sealed class CancelarFolioTransferenciaValidator : AbstractValidator<CancelarFolioTransferenciaCommand>
{
    public CancelarFolioTransferenciaValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed record FoliosTransferenciaQuery(
    EstadoFolioTransferencia? Estado = null, Guid? ClienteId = null, Guid? MovimientoId = null,
    string? Folio = null, int Offset = 0, int Limit = 50) : IRequest<PagedResponse<FolioTransferenciaResponse>>;

internal static class FolioTransferenciaLectura
{
    /// <summary>Serializa emisiones para que la suma de folios no rebase el ingreso en concurrencia.</summary>
    public const long LockKey = 0x5445535F465452;

    /// <summary>
    /// Lo que queda del ingreso para nuevos folios: disponible por aplicar (C7.4) menos
    /// devoluciones comprometidas y folios no cancelados.
    /// </summary>
    public static async Task<decimal> DisponibleAsync(TesoreriaDbContext db, MovimientoBancario m, CancellationToken ct)
    {
        var aplicado = await DepositoDisponible.AplicadoPorMovimientoAsync(db, [m], ct);
        var devoluciones = await db.DevolucionesCliente.AsNoTracking()
            .Where(d => d.MovimientoOrigenId == m.Id
                && (d.Estado == EstadoDevolucionCliente.Solicitada || d.Estado == EstadoDevolucionCliente.Autorizada
                    || d.Estado == EstadoDevolucionCliente.Pagada))
            .SumAsync(d => (decimal?)d.Importe, ct) ?? 0m;
        var folios = await db.FoliosTransferencia.AsNoTracking()
            .Where(f => f.MovimientoId == m.Id && f.Estado != EstadoFolioTransferencia.Cancelado)
            .SumAsync(f => (decimal?)f.Importe, ct) ?? 0m;
        return DepositoDisponible.Disponible(m, aplicado.GetValueOrDefault(m.Id)) - devoluciones - folios;
    }

    /// <summary>Reserva el siguiente folio «TR-000001»; atómico en Postgres, por conteo en memoria.</summary>
    public static async Task<string> SiguienteFolioAsync(TesoreriaDbContext db, Guid empresaId, CancellationToken ct)
    {
        int numero;
        if (db.Database.IsRelational())
        {
            numero = (await db.Database.SqlQuery<int>($@"
                INSERT INTO tesoreria.folio_secuencias_transferencia (empresa_id, siguiente)
                VALUES ({empresaId}, 2)
                ON CONFLICT (empresa_id) DO UPDATE
                  SET siguiente = tesoreria.folio_secuencias_transferencia.siguiente + 1
                RETURNING (siguiente - 1)::int AS ""Value""").ToListAsync(ct)).Single();
        }
        else
        {
            numero = await db.FoliosTransferencia.IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresaId, ct) + 1;
        }
        return $"TR-{numero:000000}";
    }

    public static async Task<IReadOnlyList<FolioTransferenciaResponse>> ToResponsesAsync(
        TesoreriaDbContext db, IClienteReadPort clientes, IReadOnlyList<FolioTransferencia> folios, CancellationToken ct)
    {
        if (folios.Count == 0) return [];
        var movimientoIds = folios.Select(f => f.MovimientoId).Distinct().ToArray();
        var movimientos = await db.MovimientosBancarios.AsNoTracking()
            .Where(m => movimientoIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);
        var nombres = await clientes.ObtenerVariosAsync(folios.Select(f => f.ClienteId).Distinct().ToList(), ct);
        return folios.Select(f =>
        {
            var m = movimientos[f.MovimientoId];
            nombres.TryGetValue(f.ClienteId, out var c);
            return new FolioTransferenciaResponse(
                f.Id, f.Folio, f.Estado, f.MovimientoId, m.FechaValor, m.ReferenciaBancaria, m.Monto, f.ClienteId,
                c?.Clave, c?.RazonSocial, f.Pedido, f.Importe, f.EmitidoPor, f.EmitidoEn, f.CobroMostradorId,
                f.UsadoEn, f.MotivoCancelacion, f.CanceladoEn, f.Version);
        }).ToList();
    }
}

public sealed class EmitirFolioTransferenciaHandler : IRequestHandler<EmitirFolioTransferenciaCommand, FolioTransferenciaResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public EmitirFolioTransferenciaHandler(
        TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
        IClienteReadPort clientes, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _clientes = clientes; _clock = clock;
    }

    public async Task<FolioTransferenciaResponse> Handle(EmitirFolioTransferenciaCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await EmitirAsync(command, cancellationToken);
        FolioTransferenciaResponse? resultado = null;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, FolioTransferenciaLectura.LockKey,
            async ct => resultado = await EmitirAsync(command, ct), cancellationToken);
        return resultado!;
    }

    private async Task<FolioTransferenciaResponse> EmitirAsync(EmitirFolioTransferenciaCommand command, CancellationToken ct)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que emite el folio.");

        var ingreso = await _db.MovimientosBancarios.AsNoTracking().FirstOrDefaultAsync(m => m.Id == command.MovimientoId, ct)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", $"No se encontró el movimiento '{command.MovimientoId}'.");
        if (await _db.MovimientosBancarios.AnyAsync(c => c.ContramovimientoDe == ingreso.Id, ct))
            throw new BusinessRuleException("FOLIO_TR_INGRESO_REVERTIDO", "El ingreso fue revertido; no se emite folio.");

        var cliente = await _clientes.ObtenerVariosAsync([command.ClienteId], ct);
        if (!cliente.ContainsKey(command.ClienteId))
            throw new BusinessRuleException("FOLIO_TR_CLIENTE_NO_ENCONTRADO", "El cliente seleccionado no existe.");

        var disponible = await FolioTransferenciaLectura.DisponibleAsync(_db, ingreso, ct);
        var folio = await FolioTransferenciaLectura.SiguienteFolioAsync(_db, empresaId, ct);
        var nuevo = FolioTransferencia.Emitir(
            empresaId, folio, ingreso, command.ClienteId, command.Pedido, command.Importe, disponible, usuarioId, _clock.UtcNow);

        _db.FoliosTransferencia.Add(nuevo);
        await _db.SaveChangesAsync(ct);
        return (await FolioTransferenciaLectura.ToResponsesAsync(_db, _clientes, [nuevo], ct))[0];
    }
}

public sealed class CancelarFolioTransferenciaHandler : IRequestHandler<CancelarFolioTransferenciaCommand, FolioTransferenciaResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public CancelarFolioTransferenciaHandler(
        TesoreriaDbContext db, ICurrentUserContext currentUser, IClienteReadPort clientes, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clientes = clientes; _clock = clock;
    }

    public async Task<FolioTransferenciaResponse> Handle(CancelarFolioTransferenciaCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que cancela el folio.");
        var folio = await _db.FoliosTransferencia.FirstOrDefaultAsync(f => f.Id == command.Id, ct)
            ?? throw new EntityNotFoundException("FOLIO_TR_NO_ENCONTRADO", $"No se encontró el folio '{command.Id}'.");
        folio.Cancelar(command.Motivo, usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return (await FolioTransferenciaLectura.ToResponsesAsync(_db, _clientes, [folio], ct))[0];
    }
}

public sealed class FoliosTransferenciaHandler : IRequestHandler<FoliosTransferenciaQuery, PagedResponse<FolioTransferenciaResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly IClienteReadPort _clientes;

    public FoliosTransferenciaHandler(TesoreriaDbContext db, IClienteReadPort clientes)
    {
        _db = db; _clientes = clientes;
    }

    public async Task<PagedResponse<FolioTransferenciaResponse>> Handle(FoliosTransferenciaQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);
        var q = _db.FoliosTransferencia.AsNoTracking();
        if (query.Estado is EstadoFolioTransferencia estado) q = q.Where(f => f.Estado == estado);
        if (query.ClienteId is Guid cliente) q = q.Where(f => f.ClienteId == cliente);
        if (query.MovimientoId is Guid movimiento) q = q.Where(f => f.MovimientoId == movimiento);
        if (!string.IsNullOrWhiteSpace(query.Folio))
        {
            var texto = query.Folio.Trim().ToUpperInvariant();
            q = q.Where(f => f.Folio.Contains(texto));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(f => f.Estado == EstadoFolioTransferencia.Vigente ? 0 : 1)
            .ThenByDescending(f => f.EmitidoEn)
            .Skip(offset).Take(limit)
            .ToListAsync(ct);
        return new PagedResponse<FolioTransferenciaResponse>(
            await FolioTransferenciaLectura.ToResponsesAsync(_db, _clientes, items, ct), offset, limit, total);
    }
}

/// <summary>Conteos por estado para las pestañas de la bandeja (F6.6), con el filtro de folio.</summary>
public sealed record ConteosFoliosTransferenciaPorEstadoQuery(string? Folio = null)
    : IRequest<IReadOnlyList<ConteoPorEstadoItem>>;

public sealed class ConteosFoliosTransferenciaPorEstadoHandler(TesoreriaDbContext db)
    : IRequestHandler<ConteosFoliosTransferenciaPorEstadoQuery, IReadOnlyList<ConteoPorEstadoItem>>
{
    public async Task<IReadOnlyList<ConteoPorEstadoItem>> Handle(
        ConteosFoliosTransferenciaPorEstadoQuery query, CancellationToken cancellationToken)
    {
        var q = db.FoliosTransferencia.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Folio))
        {
            var texto = query.Folio.Trim().ToUpperInvariant();
            q = q.Where(f => f.Folio.Contains(texto));
        }
        return await q.GroupBy(f => f.Estado)
            .Select(g => new ConteoPorEstadoItem((int)g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}
