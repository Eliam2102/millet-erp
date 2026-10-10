using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Cuentas;

// ============================================================================
// TES-PR2: catálogo de cuentas propias con saldo (§7.2 SaldosPorCuentaQuery;
// endpoint GET cuentas del §11 — GET only en v1, CRUD diferido a
// Administración [TES-7]).
//
// Saldo = saldo inicial a fecha de corte + ingresos − egresos registrados.
// Los movimientos deben ser posteriores al corte.
//
// PII (ADR-0006/ADR-0018): número de cuenta y CLABE salen ENMASCARADOS por
// default; el valor completo solo con el permiso canónico
// `tesoreria.movimientos.ver-cuenta-completa` (scoping fino vía
// ICurrentUserPermissions, mismo patrón que la Capa A de Cajas).
// ============================================================================

public sealed record CuentaSaldoResponse(
    Guid Id,
    string Banco,
    string NumeroCuenta,
    string? Clabe,
    string Moneda,
    string? CuentaContableRef,
    string? PerfilExtracto,
    bool Activa,
    decimal Saldo,
    int Version, string? Sucursal = null, string? Finalidad = null, string? Titular = null,
    string? Firmantes = null, decimal? SaldoInicial = null, DateOnly? FechaCorteSaldoInicial = null);

public sealed record SaldosPorCuentaQuery(bool SoloActivas = true)
    : IRequest<IReadOnlyList<CuentaSaldoResponse>>;

public sealed class SaldosPorCuentaHandler
    : IRequestHandler<SaldosPorCuentaQuery, IReadOnlyList<CuentaSaldoResponse>>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentUserPermissions _permissions;

    public SaldosPorCuentaHandler(TesoreriaDbContext db, ICurrentUserPermissions permissions)
    {
        _db = db; _permissions = permissions;
    }

    public async Task<IReadOnlyList<CuentaSaldoResponse>> Handle(
        SaldosPorCuentaQuery query, CancellationToken cancellationToken)
    {
        var cuentasQuery = _db.CuentasBancarias.AsNoTracking();
        if (query.SoloActivas) cuentasQuery = cuentasQuery.Where(c => c.Activa);

        var cuentas = await cuentasQuery
            .OrderBy(c => c.Banco).ThenBy(c => c.Moneda)
            .ToListAsync(cancellationToken);

        // Saldo por cuenta en una sola pasada agregada en SQL.
        var saldos = await _db.MovimientosBancarios.AsNoTracking()
            .GroupBy(m => m.CuentaBancariaId)
            .Select(g => new
            {
                CuentaId = g.Key,
                Saldo = g.Sum(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto),
            })
            .ToDictionaryAsync(x => x.CuentaId, x => x.Saldo, cancellationToken);

        var verCompleta = await _permissions.TieneAsync(
            PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta, cancellationToken);

        return cuentas.Select(c => CuentaBancariaMapper.ToResponse(c, saldos.GetValueOrDefault(c.Id), verCompleta)).ToList();
    }
}
