using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Bancos;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Devoluciones;

// ============================================================================
// T9.6: lecturas de devoluciones de dinero a clientes. Nombres de cliente por
// IClienteReadPort (ADR-0042, nunca JOIN); la CLABE destino sale enmascarada
// salvo permiso `tesoreria.movimientos.ver-cuenta-completa` (PII).
// ============================================================================

public sealed record DevolucionClienteResponse(
    Guid Id, OrigenDevolucionCliente Origen, EstadoDevolucionCliente Estado, Guid ClienteId,
    string? ClienteClave, string? ClienteRazonSocial, decimal Importe, decimal ImporteMaximoOrigen,
    decimal DisponibleOrigen, string Moneda, string Motivo, Guid? MovimientoOrigenId, Guid? NotaCreditoId,
    string? NotaCreditoFolio, string? NotaCreditoUuid, string? DestinoBanco, string? DestinoClabe,
    string? DestinoTitular, Guid? SolicitadaPor, DateTimeOffset SolicitadaEn, Guid? AutorizadaPor,
    DateTimeOffset? AutorizadaEn, string? MotivoRechazo, string? MotivoCancelacion, Guid? MovimientoPagoId,
    DateTimeOffset? PagadaEn, Guid? SucursalId, int Version, string Folio = "", Guid? EditadaPor = null);

internal static class DevolucionLectura
{
    /// <summary>Serializa solicitudes, ediciones y pagos de devoluciones para que el tope T9.6-a no se rebase en concurrencia.</summary>
    public const long LockKey = 0x5445535F444556;

    /// <summary>Suma lo comprometido (solicitada, autorizada o pagada) por las OTRAS devoluciones del mismo origen.</summary>
    public static async Task<decimal> ComprometidoAsync(TesoreriaDbContext db, DevolucionCliente d, CancellationToken ct)
    {
        var q = db.DevolucionesCliente.AsNoTracking().Where(x => x.Id != d.Id && x.Estado != EstadoDevolucionCliente.Rechazada && x.Estado != EstadoDevolucionCliente.Cancelada);
        q = d.Origen == OrigenDevolucionCliente.DepositoErroneo
            ? q.Where(x => x.MovimientoOrigenId == d.MovimientoOrigenId)
            : q.Where(x => x.NotaCreditoId == d.NotaCreditoId);
        return await q.SumAsync(x => (decimal?)x.Importe, ct) ?? 0m;
    }

    public static DevolucionClienteResponse ToResponse(DevolucionCliente d, ClienteRefDto? cliente, decimal disponible, bool verCuentaCompleta) => new(
        d.Id, d.Origen, d.Estado, d.ClienteId, cliente?.Clave, cliente?.RazonSocial, d.Importe, d.ImporteMaximoOrigen,
        disponible, d.Moneda, d.Motivo, d.MovimientoOrigenId, d.NotaCreditoId, d.NotaCreditoFolio, d.NotaCreditoUuid,
        d.DestinoBanco, d.DestinoClabe is null ? null : verCuentaCompleta ? d.DestinoClabe : Clabe.Enmascarar(d.DestinoClabe),
        d.DestinoTitular, d.SolicitadaPor, d.SolicitadaEn, d.AutorizadaPor, d.AutorizadaEn, d.MotivoRechazo,
        d.MotivoCancelacion, d.MovimientoPagoId, d.PagadaEn, d.SucursalId, d.Version, d.Folio, d.EditadaPor);

    /// <summary>
    /// Reserva el siguiente folio «DEV-T-0001» de la empresa. Atómico en Postgres; el proveedor
    /// en memoria (pruebas unitarias) lo deriva del conteo.
    /// </summary>
    public static async Task<string> SiguienteFolioAsync(TesoreriaDbContext db, Guid empresaId, CancellationToken ct)
    {
        int numero;
        if (db.Database.IsRelational())
        {
            numero = (await db.Database.SqlQuery<int>($@"
                INSERT INTO tesoreria.folio_secuencias_devolucion_cliente (empresa_id, siguiente)
                VALUES ({empresaId}, 2)
                ON CONFLICT (empresa_id) DO UPDATE
                  SET siguiente = tesoreria.folio_secuencias_devolucion_cliente.siguiente + 1
                RETURNING (siguiente - 1)::int AS ""Value""").ToListAsync(ct)).Single();
        }
        else
        {
            numero = await db.DevolucionesCliente.IgnoreQueryFilters().CountAsync(x => x.EmpresaId == empresaId, ct) + 1;
        }
        return $"DEV-T-{numero:0000}";
    }
}

public sealed record DevolucionesQuery(
    EstadoDevolucionCliente? Estado = null,
    OrigenDevolucionCliente? Origen = null,
    Guid? ClienteId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<DevolucionClienteResponse>>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "devolucion_cliente";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class DevolucionesHandler : IRequestHandler<DevolucionesQuery, PagedResponse<DevolucionClienteResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly IClienteReadPort _clientes;
    private readonly ICurrentUserPermissions _permisos;

    public DevolucionesHandler(TesoreriaDbContext db, IClienteReadPort clientes, ICurrentUserPermissions permisos)
    {
        _db = db; _clientes = clientes; _permisos = permisos;
    }

    public async Task<PagedResponse<DevolucionClienteResponse>> Handle(DevolucionesQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.DevolucionesCliente
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking();
        if (query.Estado is EstadoDevolucionCliente estado) q = q.Where(d => d.Estado == estado);
        if (query.Origen is OrigenDevolucionCliente origen) q = q.Where(d => d.Origen == origen);
        if (query.ClienteId is Guid cliente) q = q.Where(d => d.ClienteId == cliente);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(d => d.Estado == EstadoDevolucionCliente.Solicitada || d.Estado == EstadoDevolucionCliente.Autorizada ? 0 : 1)
            .ThenByDescending(d => d.SolicitadaEn)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var clientes = await _clientes.ObtenerVariosAsync(items.Select(d => d.ClienteId).Distinct().ToList(), cancellationToken);
        var verCuentaCompleta = await _permisos.TieneAsync("tesoreria.movimientos.ver-cuenta-completa", cancellationToken);

        var responses = new List<DevolucionClienteResponse>(items.Count);
        foreach (var d in items)
        {
            clientes.TryGetValue(d.ClienteId, out var c);
            var disponible = d.ImporteMaximoOrigen - await DevolucionLectura.ComprometidoAsync(_db, d, cancellationToken);
            responses.Add(DevolucionLectura.ToResponse(d, c, disponible, verCuentaCompleta));
        }
        return new PagedResponse<DevolucionClienteResponse>(responses, offset, limit, total);
    }
}

public sealed record DevolucionDetalleQuery(Guid Id) : IRequest<DevolucionClienteResponse>;

public sealed class DevolucionDetalleHandler : IRequestHandler<DevolucionDetalleQuery, DevolucionClienteResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly IClienteReadPort _clientes;
    private readonly ICurrentUserPermissions _permisos;

    public DevolucionDetalleHandler(TesoreriaDbContext db, IClienteReadPort clientes, ICurrentUserPermissions permisos)
    {
        _db = db; _clientes = clientes; _permisos = permisos;
    }

    public async Task<DevolucionClienteResponse> Handle(DevolucionDetalleQuery query, CancellationToken cancellationToken)
    {
        var d = await _db.DevolucionesCliente.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("DEV_NO_ENCONTRADA", $"No se encontró la devolución '{query.Id}'.");
        var clientes = await _clientes.ObtenerVariosAsync([d.ClienteId], cancellationToken);
        clientes.TryGetValue(d.ClienteId, out var c);
        var disponible = d.ImporteMaximoOrigen - await DevolucionLectura.ComprometidoAsync(_db, d, cancellationToken);
        var verCuentaCompleta = await _permisos.TieneAsync("tesoreria.movimientos.ver-cuenta-completa", cancellationToken);
        return DevolucionLectura.ToResponse(d, c, disponible, verCuentaCompleta);
    }
}

public sealed record BuscarClientesTesoreriaQuery(string Texto) : IRequest<IReadOnlyList<ClienteRefDto>>;

public sealed class BuscarClientesTesoreriaHandler : IRequestHandler<BuscarClientesTesoreriaQuery, IReadOnlyList<ClienteRefDto>>
{
    private readonly IClienteReadPort _clientes;

    public BuscarClientesTesoreriaHandler(IClienteReadPort clientes) => _clientes = clientes;

    public async Task<IReadOnlyList<ClienteRefDto>> Handle(BuscarClientesTesoreriaQuery query, CancellationToken cancellationToken)
    {
        var texto = query.Texto?.Trim();
        if (texto is null || texto.Length < 2) return [];
        return await _clientes.BuscarAsync(texto, 20, cancellationToken);
    }
}

public sealed record ConteosDevolucionesPorEstadoQuery(OrigenDevolucionCliente? Origen = null, Guid? ClienteId = null)
    : IRequest<IReadOnlyList<ConteoPorEstadoItem>>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "devolucion_cliente";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ConteosDevolucionesPorEstadoHandler(TesoreriaDbContext db)
    : IRequestHandler<ConteosDevolucionesPorEstadoQuery, IReadOnlyList<ConteoPorEstadoItem>>
{
    public async Task<IReadOnlyList<ConteoPorEstadoItem>> Handle(ConteosDevolucionesPorEstadoQuery query, CancellationToken cancellationToken)
    {
        var q = db.DevolucionesCliente.AsNoTracking()
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id));
        if (query.Origen is OrigenDevolucionCliente origen) q = q.Where(d => d.Origen == origen);
        if (query.ClienteId is Guid cliente) q = q.Where(d => d.ClienteId == cliente);
        return await q.GroupBy(x => x.Estado)
            .Select(g => new ConteoPorEstadoItem((int)g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}
