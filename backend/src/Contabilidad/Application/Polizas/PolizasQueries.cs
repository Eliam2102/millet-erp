using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Polizas;

public sealed record DocumentoOrigenResponse(string Modulo, string Tipo, Guid Id, string? Folio, string? Uuid);

/// <summary>K10.4: póliza ligada a esta (su reversa o una reclasificación), para verla desde la original.</summary>
public sealed record PolizaLigadaResponse(Guid Id, string? Folio, RelacionPoliza Relacion, EstadoPoliza Estado, DateOnly Fecha, string Concepto);

public sealed record PartidaResponse(
    Guid Id, int Linea, Guid CuentaId, string CuentaCodigo, string? CuentaNombre, string? Concepto, decimal Cargo, decimal Abono,
    string Moneda, decimal TipoCambio, decimal? ImporteOriginal, Guid SucursalId, string? SucursalNombre,
    CentroRefDto? Dim1, CentroRefDto? Dim2, CentroRefDto? Dim3, string? Proyecto,
    AuxiliarContable? Cliente, AuxiliarContable? Proveedor, AuxiliarContable? CuentaBancaria);

/// <summary>
/// Póliza completa. <c>Folio</c> es null en borrador. En la póliza de anulación, <c>PolizaAnulada*</c> apunta a la original; en la
/// original anulada, <c>AnuladaPor*</c> apunta a su anulación (K10.3).
/// </summary>
public sealed record PolizaResponse(
    Guid Id, string? Folio, Guid TipoOperacionId, string TipoClave, string? TipoNombre, TipoPolizaSat TipoSat, int? Numero,
    DateOnly Fecha, int PeriodoAnio, int PeriodoNumero, string Concepto, OrigenPoliza Origen, EstadoPoliza Estado,
    DocumentoOrigenResponse? DocumentoOrigen, Guid? PolizaAnuladaId, string? PolizaAnuladaFolio, Guid? AnuladaPorPolizaId,
    string? AnuladaPorFolio, string? MotivoAnulacion, string? AnuladaPor, DateTimeOffset? AnuladaEn,
    Guid? CapturadaPorId, string CapturadaPor, DateTimeOffset CapturadaEn, Guid? AutorizadaPorId, string? AutorizadaPor,
    DateTimeOffset? AutorizadaEn, decimal TotalCargos, decimal TotalAbonos, decimal Diferencia, bool Cuadra,
    IReadOnlyList<PartidaResponse> Partidas, int Version,
    Guid? PolizaRelacionadaId = null, string? PolizaRelacionadaFolio = null, RelacionPoliza? Relacion = null,
    Guid? PlantillaRecurrenteId = null, string? PlantillaRecurrenteNombre = null, IReadOnlyList<PolizaLigadaResponse>? Ligadas = null);

public sealed record PolizaResumenResponse(
    Guid Id, string? Folio, Guid TipoOperacionId, string TipoClave, int? Numero, DateOnly Fecha, int PeriodoAnio, int PeriodoNumero,
    string Concepto, OrigenPoliza Origen, EstadoPoliza Estado, bool EsAnulacion, decimal TotalCargos, decimal TotalAbonos,
    string CapturadaPor, string? AutorizadaPor, int Version, RelacionPoliza? Relacion = null, bool EsRecurrente = false);

/// <summary>Proyección de pólizas: resuelve tipo, cuentas, sucursales, centros y auxiliares para mostrar (incluye dados de baja).</summary>
public sealed class LecturaPolizas(
    ContabilidadDbContext db, ICentroCostoContabilidadPort centros, ISucursalContabilidadPort sucursales,
    ITerceroContabilidadPort terceros, ICuentaBancariaContabilidadPort bancos)
{
    public async Task<PolizaResponse> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Polizas.AsNoTracking().Include(x => x.Partidas).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("CONTAB_POLIZA_NO_ENCONTRADA", $"No existe la póliza '{id}'.");
        return await ResponseAsync(p, ct);
    }

    public async Task<PolizaResponse> ResponseAsync(Poliza p, CancellationToken ct)
    {
        var partidas = p.Partidas.OrderBy(x => x.Linea).ToList();
        var tipoNombre = await db.TiposDocumento.AsNoTracking().Where(t => t.Id == p.TipoOperacionId).Select(t => t.Nombre).FirstOrDefaultAsync(ct);
        var ligadas = new[] { p.PolizaAnuladaId, p.AnuladaPorPolizaId, p.PolizaRelacionadaId }.Where(x => x is not null).Select(x => x!.Value).ToList();
        var derivadas = await db.Polizas.AsNoTracking().Where(x => x.PolizaRelacionadaId == p.Id && x.Relacion != null)
            .OrderBy(x => x.Fecha).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.TipoClave, x.Numero, x.Relacion, x.Estado, x.Fecha, x.Concepto }).ToListAsync(ct);
        var plantillaNombre = p.PlantillaRecurrenteId is { } pid
            ? await db.PlantillasRecurrentes.AsNoTracking().Where(x => x.Id == pid).Select(x => x.Nombre).FirstOrDefaultAsync(ct)
            : null;
        var folios = ligadas.Count == 0 ? new Dictionary<Guid, string?>()
            : (await db.Polizas.AsNoTracking().Where(x => ligadas.Contains(x.Id)).Select(x => new { x.Id, x.TipoClave, x.Numero }).ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.Numero is { } n ? Poliza.FormatearFolio(x.TipoClave, n) : null);
        var cuentaIds = partidas.Select(x => x.CuentaId).Distinct().ToList();
        var nombres = await db.Cuentas.AsNoTracking().Where(c => cuentaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var dimIds = partidas.SelectMany(x => new[] { x.Dim1Id, x.Dim2Id, x.Dim3Id }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var nodos = dimIds.Count == 0 ? new Dictionary<Guid, CentroCostoNodo>() : await centros.ObtenerAsync(dimIds, ct);
        var suc = (await sucursales.ListarAsync(ct)).ToDictionary(s => s.Id);
        static List<Guid> Ids(IEnumerable<Guid?> xs) => [.. xs.Where(x => x is not null).Select(x => x!.Value).Distinct()];
        var clientes = await terceros.ObtenerAsync(TipoAuxiliar.Cliente, Ids(partidas.Select(x => x.ClienteId)), ct);
        var proveedores = await terceros.ObtenerAsync(TipoAuxiliar.Proveedor, Ids(partidas.Select(x => x.ProveedorId)), ct);
        var cuentasBanco = await bancos.ObtenerAsync(Ids(partidas.Select(x => x.CuentaBancariaId)), ct);
        CentroRefDto? Ref(Guid? cid) => cid is { } x && nodos.TryGetValue(x, out var n) ? new(n.Id, n.Clave, n.Nombre, n.ActivoEnCadena) : null;
        static AuxiliarContable? Aux(IReadOnlyDictionary<Guid, AuxiliarContable> d, Guid? aid) => aid is { } x ? d.GetValueOrDefault(x) : null;
        string? Folio(Guid? pid) => pid is { } x ? folios.GetValueOrDefault(x) : null;

        return new PolizaResponse(p.Id, p.Folio, p.TipoOperacionId, p.TipoClave, tipoNombre, p.TipoSat, p.Numero, p.Fecha, p.PeriodoAnio,
            p.PeriodoNumero, p.Concepto, p.Origen, p.Estado,
            p.OrigenDocumentoId is { } doc ? new(p.OrigenModulo!, p.OrigenTipo!, doc, p.OrigenFolio, p.OrigenUuid) : null,
            p.PolizaAnuladaId, Folio(p.PolizaAnuladaId), p.AnuladaPorPolizaId, Folio(p.AnuladaPorPolizaId), p.MotivoAnulacion, p.AnuladaPor,
            p.AnuladaEn, p.CapturadaPorId, p.CapturadaPor, p.CreatedAt, p.AutorizadaPorId, p.AutorizadaPor, p.AutorizadaEn,
            p.TotalCargos, p.TotalAbonos, p.TotalCargos - p.TotalAbonos, p.Cuadra,
            [.. partidas.Select(x => new PartidaResponse(x.Id, x.Linea, x.CuentaId, x.CuentaCodigo, nombres.GetValueOrDefault(x.CuentaId),
                x.Concepto, x.Cargo, x.Abono, x.Moneda, x.TipoCambio, x.ImporteOriginal, x.SucursalId, suc.GetValueOrDefault(x.SucursalId)?.Nombre,
                Ref(x.Dim1Id), Ref(x.Dim2Id), Ref(x.Dim3Id), x.Proyecto, Aux(clientes, x.ClienteId), Aux(proveedores, x.ProveedorId),
                Aux(cuentasBanco, x.CuentaBancariaId)))],
            p.Version, p.PolizaRelacionadaId, Folio(p.PolizaRelacionadaId), p.Relacion, p.PlantillaRecurrenteId, plantillaNombre,
            [.. derivadas.Select(x => new PolizaLigadaResponse(x.Id, x.Numero is { } n ? Poliza.FormatearFolio(x.TipoClave, n) : null,
                x.Relacion!.Value, x.Estado, x.Fecha, x.Concepto))]);
    }
}

public sealed record ObtenerPolizaQuery(Guid Id) : IRequest<PolizaResponse>;

public sealed class ObtenerPolizaHandler(LecturaPolizas lectura) : IRequestHandler<ObtenerPolizaQuery, PolizaResponse>
{
    public Task<PolizaResponse> Handle(ObtenerPolizaQuery request, CancellationToken cancellationToken) => lectura.ObtenerAsync(request.Id, cancellationToken);
}

/// <summary>
/// Bandeja de pólizas, más reciente primero. <c>Q</c> busca por folio («TT-000123», también sin ceros) o por texto en el concepto.
/// La póliza es de la empresa (Contabilidad es corporativa): el alcance por sucursal se aplica al capturar y operar, no al consultar.
/// </summary>
public sealed record ListarPolizasQuery(
    int? Anio, int? Numero, Guid? TipoOperacionId, EstadoPoliza? Estado, OrigenPoliza? Origen, string? Q, int Offset = 0, int Limit = 50)
    : IRequest<PagedResponse<PolizaResumenResponse>>;

public sealed class ListarPolizasValidator : AbstractValidator<ListarPolizasQuery>
{
    public ListarPolizasValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, 200);
        RuleFor(q => q.Offset).GreaterThanOrEqualTo(0);
        RuleFor(q => q.Numero).InclusiveBetween(1, PeriodoContable.NumeroAjuste).When(q => q.Numero is not null);
        RuleFor(q => q.Q).MaximumLength(100);
    }
}

public sealed partial class ListarPolizasHandler(ContabilidadDbContext db) : IRequestHandler<ListarPolizasQuery, PagedResponse<PolizaResumenResponse>>
{
    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9-]{0,19})-(\d{1,6})\s*$")]
    private static partial Regex FolioRegex();

    public async Task<PagedResponse<PolizaResumenResponse>> Handle(ListarPolizasQuery request, CancellationToken cancellationToken)
    {
        var q = db.Polizas.AsNoTracking();
        if (request.Anio is { } a) q = q.Where(p => p.PeriodoAnio == a);
        if (request.Numero is { } n) q = q.Where(p => p.PeriodoNumero == n);
        if (request.TipoOperacionId is { } t) q = q.Where(p => p.TipoOperacionId == t);
        if (request.Estado is { } e) q = q.Where(p => p.Estado == e);
        if (request.Origen is { } o) q = q.Where(p => p.Origen == o);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var texto = request.Q.Trim();
            if (FolioRegex().Match(texto) is { Success: true } m)
            {
                var clave = m.Groups[1].Value.ToUpperInvariant();
                var numero = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                q = q.Where(p => p.TipoClave == clave && p.Numero == numero);
            }
            else
            {
                var patron = $"%{texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
                q = q.Where(p => EF.Functions.ILike(p.Concepto, patron));
            }
        }
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Id)
            .Skip(request.Offset).Take(request.Limit)
            .Select(p => new { p.Id, p.TipoOperacionId, p.TipoClave, p.Numero, p.Fecha, p.PeriodoAnio, p.PeriodoNumero, p.Concepto, p.Origen,
                p.Estado, p.PolizaAnuladaId, p.TotalCargos, p.TotalAbonos, p.CapturadaPor, p.AutorizadaPor, p.Version, p.Relacion, p.PlantillaRecurrenteId })
            .ToListAsync(cancellationToken);
        return new PagedResponse<PolizaResumenResponse>([.. items.Select(p => new PolizaResumenResponse(p.Id,
            p.Numero is { } num ? Poliza.FormatearFolio(p.TipoClave, num) : null, p.TipoOperacionId, p.TipoClave, p.Numero, p.Fecha,
            p.PeriodoAnio, p.PeriodoNumero, p.Concepto, p.Origen, p.Estado, p.PolizaAnuladaId is not null, p.TotalCargos, p.TotalAbonos,
            p.CapturadaPor, p.AutorizadaPor, p.Version, p.Relacion, p.PlantillaRecurrenteId is not null))], total, request.Offset, request.Limit);
    }
}

public sealed record SaldoCuentaPeriodoResponse(Guid CuentaId, string CuentaCodigo, string? CuentaNombre, decimal Cargos, decimal Abonos, decimal Saldo);

/// <summary>
/// Movimientos del periodo por cuenta, con las pólizas que cuentan para la balanza: autorizadas y anuladas, más sus anulaciones
/// (que son autorizadas). Como la anulación lleva las mismas partidas en negativo, una póliza anulada y su anulación suman cero:
/// la balanza queda como si la póliza no existiera (K10.3-a). Los borradores no cuentan. Es la consulta mínima de K10.3; la balanza
/// completa con saldos iniciales y arrastre es C1.7.
/// </summary>
public sealed record SaldosPeriodoQuery(int Anio, int Numero, Guid? CuentaId) : IRequest<IReadOnlyList<SaldoCuentaPeriodoResponse>>;

public sealed class SaldosPeriodoValidator : AbstractValidator<SaldosPeriodoQuery>
{
    public SaldosPeriodoValidator()
    {
        RuleFor(q => q.Numero).InclusiveBetween(1, PeriodoContable.NumeroAjuste);
        RuleFor(q => q.Anio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo);
    }
}

public sealed class SaldosPeriodoHandler(ContabilidadDbContext db) : IRequestHandler<SaldosPeriodoQuery, IReadOnlyList<SaldoCuentaPeriodoResponse>>
{
    public async Task<IReadOnlyList<SaldoCuentaPeriodoResponse>> Handle(SaldosPeriodoQuery request, CancellationToken cancellationToken)
    {
        var q = from x in db.PolizasPartidas.AsNoTracking()
                join p in db.Polizas.AsNoTracking() on x.PolizaId equals p.Id
                where p.PeriodoAnio == request.Anio && p.PeriodoNumero == request.Numero && p.Estado != EstadoPoliza.Borrador
                select x;
        if (request.CuentaId is { } c) q = q.Where(x => x.CuentaId == c);
        var filas = await q.GroupBy(x => new { x.CuentaId, x.CuentaCodigo })
            .Select(g => new { g.Key.CuentaId, g.Key.CuentaCodigo, Cargos = g.Sum(x => x.Cargo), Abonos = g.Sum(x => x.Abono) })
            .ToListAsync(cancellationToken);
        var ids = filas.Select(f => f.CuentaId).ToList();
        var nombres = await db.Cuentas.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);
        return [.. filas.OrderBy(f => f.CuentaCodigo, StringComparer.Ordinal)
            .Select(f => new SaldoCuentaPeriodoResponse(f.CuentaId, f.CuentaCodigo, nombres.GetValueOrDefault(f.CuentaId), f.Cargos, f.Abonos, f.Cargos - f.Abonos))];
    }
}
