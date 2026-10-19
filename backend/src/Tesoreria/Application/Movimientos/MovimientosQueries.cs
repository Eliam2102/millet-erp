using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Movimientos;

// ============================================================================
// TES-PR2: libro de movimientos (§7.2 MovimientosBancariosQuery +
// MovimientoDetalleQuery). Los nombres de beneficiario (Proveedor/Cliente)
// se resuelven vía read-port de DatosMaestros cuando llegue en PR-3
// (ADR-0042); mientras tanto el response lleva solo los IDs.
// ============================================================================

public sealed record MovimientoBancarioResponse(
    Guid Id,
    Guid CuentaBancariaId,
    SentidoMovimiento Sentido,
    decimal Monto,
    string Moneda,
    DateOnly FechaValor,
    string? ReferenciaBancaria,
    Guid? ConceptoId,
    string? ConceptoNombre,
    EstadoAplicacionMovimiento EstadoAplicacion,
    EstadoConciliacionMovimiento EstadoConciliacion,
    BeneficiarioTipo? BeneficiarioTipo,
    Guid? BeneficiarioRef,
    Guid? ContramovimientoDe,
    string? MotivoNoAplicado,
    Guid CreadoPor,
    DateTimeOffset CreadoEn,
    int Version, string? MotivoReversa = null, string? MotivoReclasificacion = null,
    // 86e3pdmee: cuenta del proveedor a la que se pagó (snapshot, número enmascarado).
    string? DestinoBanco = null, string? DestinoNumeroCuenta = null, string? DestinoTitular = null, bool FondosVerificados = false,
    // C-03: razón social y clave del beneficiario, resueltas por los puertos de DatosMaestros (ADR-0042).
    string? BeneficiarioNombre = null, string? BeneficiarioClave = null);

internal static class MovimientoBancarioMapper
{
    /// <summary>Resuelve en lote (sin N+1) nombre y clave de los beneficiarios cliente/proveedor de un conjunto de movimientos.</summary>
    public static async Task<IReadOnlyDictionary<Guid, (string Nombre, string Clave)>> ResolverBeneficiariosAsync(
        IReadOnlyCollection<MovimientoBancario> movimientos, IClienteReadPort clientes, IProveedorBancoReadPort proveedores, CancellationToken ct)
    {
        var resultado = new Dictionary<Guid, (string, string)>();
        var idsCliente = movimientos.Where(m => m.BeneficiarioTipo == BeneficiarioTipo.Cliente && m.BeneficiarioRef is not null)
            .Select(m => m.BeneficiarioRef!.Value).Distinct().ToList();
        if (idsCliente.Count > 0)
            foreach (var (id, c) in await clientes.ObtenerVariosAsync(idsCliente, ct)) resultado[id] = (c.RazonSocial, c.Clave);
        var idsProveedor = movimientos.Where(m => m.BeneficiarioTipo == BeneficiarioTipo.Proveedor && m.BeneficiarioRef is not null)
            .Select(m => m.BeneficiarioRef!.Value).Distinct().ToList();
        if (idsProveedor.Count > 0)
            foreach (var (id, p) in await proveedores.ObtenerVariosAsync(idsProveedor, ct)) resultado[id] = (p.RazonSocial, p.Clave);
        return resultado;
    }

    public static MovimientoBancarioResponse ToResponse(
        MovimientoBancario m, IReadOnlyDictionary<Guid, (string Nombre, string Clave)> beneficiarios,
        string? conceptoNombre = null, bool verCuentaCompleta = false)
    {
        var r = ToResponse(m, conceptoNombre, verCuentaCompleta);
        return m.BeneficiarioRef is Guid id && beneficiarios.TryGetValue(id, out var b)
            ? r with { BeneficiarioNombre = b.Nombre, BeneficiarioClave = b.Clave }
            : r;
    }

    // La cuenta destino es PII: completa solo con `tesoreria.movimientos.ver-cuenta-completa`.
    public static MovimientoBancarioResponse ToResponse(MovimientoBancario m, string? conceptoNombre = null, bool verCuentaCompleta = false) =>
        new(m.Id, m.CuentaBancariaId, m.Sentido, m.Monto, m.Moneda, m.FechaValor,
            m.ReferenciaBancaria, m.ConceptoId, conceptoNombre, m.EstadoAplicacion,
            m.EstadoConciliacion, m.BeneficiarioTipo, m.BeneficiarioRef,
            m.ContramovimientoDe, m.MotivoNoAplicado, m.CreadoPor, m.CreadoEn, m.Version, m.MotivoReversa, m.MotivoReclasificacion,
            m.DestinoBanco, m.DestinoNumeroCuenta is null ? null : verCuentaCompleta ? m.DestinoNumeroCuenta : Clabe.Enmascarar(m.DestinoNumeroCuenta), m.DestinoTitular, m.FondosVerificados);
}

// --------------------------------------------------- Libro (bandeja paginada)

public sealed record MovimientosBancariosQuery(
    Guid? CuentaBancariaId = null,
    SentidoMovimiento? Sentido = null,
    EstadoAplicacionMovimiento? EstadoAplicacion = null,
    EstadoConciliacionMovimiento? EstadoConciliacion = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<MovimientoBancarioResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "movimiento_bancario";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class MovimientosBancariosHandler
    : IRequestHandler<MovimientosBancariosQuery, PagedResponse<MovimientoBancarioResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IClienteReadPort _clientes;
    private readonly IProveedorBancoReadPort _proveedores;
    public MovimientosBancariosHandler(TesoreriaDbContext db, ICurrentUserPermissions permisos, IClienteReadPort clientes, IProveedorBancoReadPort proveedores)
    {
        _db = db; _permisos = permisos; _clientes = clientes; _proveedores = proveedores;
    }

    public async Task<PagedResponse<MovimientoBancarioResponse>> Handle(
        MovimientosBancariosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.MovimientosBancarios
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking();
        if (query.CuentaBancariaId is Guid cuenta) q = q.Where(m => m.CuentaBancariaId == cuenta);
        if (query.Sentido is SentidoMovimiento s) q = q.Where(m => m.Sentido == s);
        if (query.EstadoAplicacion is EstadoAplicacionMovimiento ea) q = q.Where(m => m.EstadoAplicacion == ea);
        if (query.EstadoConciliacion is EstadoConciliacionMovimiento ec) q = q.Where(m => m.EstadoConciliacion == ec);
        if (query.Desde is DateOnly desde) q = q.Where(m => m.FechaValor >= desde);
        if (query.Hasta is DateOnly hasta) q = q.Where(m => m.FechaValor <= hasta);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(m => m.FechaValor).ThenByDescending(m => m.CreadoEn)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        // Nombre del concepto en dos pasos (catálogo chico, misma BD) en vez
        // de left join en la query — legible y sin sorpresas de traducción.
        var conceptoIds = items.Where(m => m.ConceptoId is not null)
            .Select(m => m.ConceptoId!.Value).Distinct().ToList();
        var conceptos = conceptoIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ConceptosMovimiento.AsNoTracking()
                .Where(c => conceptoIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Nombre, cancellationToken);

        var verCuentaCompleta = await _permisos.TieneAsync("tesoreria.movimientos.ver-cuenta-completa", cancellationToken);
        var beneficiarios = await MovimientoBancarioMapper.ResolverBeneficiariosAsync(items, _clientes, _proveedores, cancellationToken);
        var responses = items
            .Select(m => MovimientoBancarioMapper.ToResponse(
                m, beneficiarios, m.ConceptoId is Guid cid && conceptos.TryGetValue(cid, out var n) ? n : null, verCuentaCompleta))
            .ToList();

        return new PagedResponse<MovimientoBancarioResponse>(responses, offset, limit, total);
    }
}

// --------------------------------------------------- Detalle

/// <summary>Aplicación del movimiento a un pasivo — <c>PagoId</c> es el Id de la fila (correlación con CxP).</summary>
public sealed record AplicacionMovimientoDto(
    Guid PagoId,
    Guid FacturaProveedorId,
    Guid ProveedorId,
    decimal ImporteAplicado,
    bool Revertida,
    Guid? CorridaId,
    DateTimeOffset CreadoEn, string? MotivoReversa = null);

/// <summary>
/// Detalle del movimiento (TES-FE-PR2): el response base + sus
/// aplicaciones a pasivos (para revertir por <c>PagoId</c>) y los
/// contramovimientos que lo compensan (RN-10).
/// </summary>
public sealed record MovimientoDetalleResponse(
    MovimientoBancarioResponse Movimiento,
    IReadOnlyList<AplicacionMovimientoDto> Aplicaciones,
    IReadOnlyList<MovimientoBancarioResponse> Contramovimientos);

public sealed record MovimientoDetalleQuery(Guid Id) : IRequest<MovimientoDetalleResponse>;

public sealed class MovimientoDetalleHandler : IRequestHandler<MovimientoDetalleQuery, MovimientoDetalleResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IClienteReadPort _clientes;
    private readonly IProveedorBancoReadPort _proveedores;
    public MovimientoDetalleHandler(TesoreriaDbContext db, ICurrentUserPermissions permisos, IClienteReadPort clientes, IProveedorBancoReadPort proveedores)
    {
        _db = db; _permisos = permisos; _clientes = clientes; _proveedores = proveedores;
    }

    public async Task<MovimientoDetalleResponse> Handle(
        MovimientoDetalleQuery query, CancellationToken cancellationToken)
    {
        var m = await _db.MovimientosBancarios.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento bancario '{query.Id}'.");

        string? conceptoNombre = null;
        if (m.ConceptoId is Guid conceptoId)
        {
            conceptoNombre = await _db.ConceptosMovimiento.AsNoTracking()
                .Where(c => c.Id == conceptoId)
                .Select(c => c.Nombre)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var aplicaciones = await _db.AplicacionesPagoProveedor.AsNoTracking()
            .Where(a => a.MovimientoId == m.Id)
            .OrderBy(a => a.CreadoEn)
            .Select(a => new AplicacionMovimientoDto(
                a.Id, a.FacturaProveedorId, a.ProveedorId,
                a.ImporteAplicado, a.Revertida, a.CorridaId, a.CreadoEn, a.MotivoReversa))
            .ToListAsync(cancellationToken);

        var contramovimientos = await _db.MovimientosBancarios.AsNoTracking()
            .Where(x => x.ContramovimientoDe == m.Id)
            .OrderBy(x => x.CreadoEn)
            .ToListAsync(cancellationToken);

        var verCuentaCompleta = await _permisos.TieneAsync("tesoreria.movimientos.ver-cuenta-completa", cancellationToken);
        var beneficiarios = await MovimientoBancarioMapper.ResolverBeneficiariosAsync([m, .. contramovimientos], _clientes, _proveedores, cancellationToken);
        return new MovimientoDetalleResponse(
            MovimientoBancarioMapper.ToResponse(m, beneficiarios, conceptoNombre, verCuentaCompleta),
            aplicaciones,
            contramovimientos.Select(c => MovimientoBancarioMapper.ToResponse(c, beneficiarios, verCuentaCompleta: verCuentaCompleta)).ToList());
    }
}
