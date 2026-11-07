using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Reportes;

/// <summary>Origen de la diferencia cambiaria realizada: pago a proveedor (#5) o cobro de cliente (#12).</summary>
public enum OrigenDiferenciaCambiaria { Proveedores = 0, Clientes = 1 }

/// <summary>Columna del reporte (ADR-0036). <c>Decimales</c> fija los decimales de las columnas numéricas (TC con 4).</summary>
public sealed record ColumnaReporteDiferencias(string Clave, string Etiqueta, string Tipo, string? Alineacion = null, int? Decimales = null);

/// <summary>
/// Una diferencia cambiaria realizada: la póliza de un pago o cobro en moneda extranjera. <c>Neto</c> = utilidad − pérdida. Las
/// anulaciones (K10.3) llevan los importes en negativo y netean con su original.
/// </summary>
public sealed record DiferenciaCambiariaFila(
    Guid PolizaId, string? PolizaFolio, DateOnly Fecha, OrigenDiferenciaCambiaria Origen, string OrigenEtiqueta, string? DocumentoFolio,
    Guid? TerceroId, string? TerceroClave, string? TerceroNombre, Guid SucursalId, string? SucursalNombre, string? Moneda,
    decimal? ImporteOriginal, decimal? TipoCambioDocumento, decimal? TipoCambioPago, decimal Perdida, decimal Utilidad, decimal Neto,
    bool EsAnulacion);

/// <summary>Totales del periodo; <c>Perdida</c>, <c>Utilidad</c> y <c>Neto</c> son el pie de la tabla.</summary>
public sealed record TotalesDiferenciasCambiarias(
    decimal Perdida, decimal Utilidad, decimal Neto, decimal PerdidaProveedores, decimal UtilidadProveedores, decimal PerdidaClientes,
    decimal UtilidadClientes, int Movimientos);

public sealed record DiferenciasCambiariasResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaReporteDiferencias> Columnas, IReadOnlyList<DiferenciaCambiariaFila> Filas, TotalesDiferenciasCambiarias Totales,
    DateOnly Desde, DateOnly Hasta, IReadOnlyList<string> Avisos);

/// <summary>
/// TES-09 (86e3jvvj4) · reporte de diferencias cambiarias realizadas del periodo: una fila por pago a proveedor o cobro de cliente en
/// moneda extranjera que generó pérdida o utilidad cambiaria (CON-11). Sale solo de las pólizas de Contabilidad: las partidas de las
/// cuentas de los roles <c>PERDIDA/UTILIDAD_CAMBIARIA_REALIZADA_PROVEEDORES/_CLIENTES</c>; en la misma póliza, el TC de la factura es el
/// de la partida del proveedor o cliente y el TC del pago el de las partidas en moneda extranjera sin tercero con otro TC (bancos).
/// Cuentan las pólizas autorizadas y las anuladas con su anulación (K10.3); los borradores no. Contabilidad es corporativa: la
/// sucursal es filtro opcional.
/// </summary>
public sealed record DiferenciasCambiariasQuery(DateOnly Desde, DateOnly Hasta, Guid? SucursalId = null, OrigenDiferenciaCambiaria? Origen = null)
    : IRequest<DiferenciasCambiariasResponse>;

public sealed class DiferenciasCambiariasValidator : AbstractValidator<DiferenciasCambiariasQuery>
{
    public DiferenciasCambiariasValidator()
    {
        RuleFor(q => q.Desde.Year).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).OverridePropertyName("desde");
        RuleFor(q => q.Hasta).GreaterThanOrEqualTo(q => q.Desde).OverridePropertyName("hasta")
            .WithMessage("La fecha final no puede ser anterior a la inicial.");
        RuleFor(q => q.Hasta).Must((q, h) => h.DayNumber - q.Desde.DayNumber <= 366).OverridePropertyName("hasta")
            .WithMessage("El periodo no puede pasar de un año.");
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
        RuleFor(q => q.Origen).IsInEnum().When(q => q.Origen is not null);
    }
}

public sealed class DiferenciasCambiariasHandler(
    ContabilidadDbContext db, ITerceroContabilidadPort terceros, ISucursalContabilidadPort sucursales, IClock clock)
    : IRequestHandler<DiferenciasCambiariasQuery, DiferenciasCambiariasResponse>
{
    /// <summary>Roles de CON-11 (#500) y a qué origen y lado pertenecen.</summary>
    public static readonly IReadOnlyDictionary<string, (OrigenDiferenciaCambiaria Origen, bool EsPerdida)> Roles =
        new Dictionary<string, (OrigenDiferenciaCambiaria, bool)>
        {
            ["PERDIDA_CAMBIARIA_REALIZADA_PROVEEDORES"] = (OrigenDiferenciaCambiaria.Proveedores, true),
            ["UTILIDAD_CAMBIARIA_REALIZADA_PROVEEDORES"] = (OrigenDiferenciaCambiaria.Proveedores, false),
            ["PERDIDA_CAMBIARIA_REALIZADA_CLIENTES"] = (OrigenDiferenciaCambiaria.Clientes, true),
            ["UTILIDAD_CAMBIARIA_REALIZADA_CLIENTES"] = (OrigenDiferenciaCambiaria.Clientes, false),
        };

    private static readonly ColumnaReporteDiferencias[] Columnas =
    [
        new("fecha", "Fecha", "fecha"),
        new("polizaFolio", "Póliza", "texto"),
        new("origenEtiqueta", "Origen", "texto"),
        new("documentoFolio", "Documento", "texto"),
        new("terceroNombre", "Proveedor o cliente", "texto"),
        new("sucursalNombre", "Sucursal", "texto"),
        new("moneda", "Moneda", "texto"),
        new("importeOriginal", "Importe en moneda", "numero", "derecha", 2),
        new("tipoCambioDocumento", "TC factura", "numero", "derecha", 4),
        new("tipoCambioPago", "TC pago", "numero", "derecha", 4),
        new("perdida", "Pérdida", "moneda", "derecha"),
        new("utilidad", "Utilidad", "moneda", "derecha"),
        new("neto", "Neto", "moneda", "derecha"),
    ];

    public async Task<DiferenciasCambiariasResponse> Handle(DiferenciasCambiariasQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var codigos = Roles.Keys.ToList();
        var roles = await db.RolesCuenta.AsNoTracking()
            .Where(r => codigos.Contains(r.Codigo) && r.DeletedAt == null)
            .Select(r => new { r.Codigo, r.CuentaId }).ToListAsync(ct);
        var cuentas = roles.Where(r => r.CuentaId is not null)
            .GroupBy(r => r.CuentaId!.Value)
            .ToDictionary(g => g.Key, g => Roles[g.First().Codigo]);
        var avisos = codigos.Where(c => !roles.Any(r => r.Codigo == c && r.CuentaId is not null))
            .Select(c => $"El rol {c} no tiene cuenta asignada: sus diferencias no salen en el reporte. Asígnala en Motor contable › Roles de cuenta.")
            .ToList();

        var filas = new List<DiferenciaCambiariaFila>();
        if (cuentas.Count > 0)
        {
            var ids = cuentas.Keys.ToList();
            var polizaIds = await (
                from p in db.Polizas.AsNoTracking()
                join d in db.PolizasPartidas.AsNoTracking() on p.Id equals d.PolizaId
                where p.Fecha >= request.Desde && p.Fecha <= request.Hasta && p.Estado != EstadoPoliza.Borrador && ids.Contains(d.CuentaId)
                    && (request.SucursalId == null || d.SucursalId == request.SucursalId)
                select p.Id).Distinct().ToListAsync(ct);

            var polizas = await db.Polizas.AsNoTracking().Where(p => polizaIds.Contains(p.Id))
                .Select(p => new { p.Id, p.TipoClave, p.Numero, p.Fecha, p.OrigenFolio, p.PolizaAnuladaId }).ToListAsync(ct);
            var partidas = await db.PolizasPartidas.AsNoTracking().Where(d => polizaIds.Contains(d.PolizaId))
                .Select(d => new PartidaDiferencia(d.PolizaId, d.Linea, d.CuentaId, d.Cargo, d.Abono, d.Moneda, d.TipoCambio, d.ImporteOriginal,
                    d.SucursalId, d.ClienteId, d.ProveedorId))
                .ToListAsync(ct);

            var armadas = Armar(
                polizas.Select(p => new PolizaDiferencia(p.Id, p.Numero is { } n ? Poliza.FormatearFolio(p.TipoClave, n) : null, p.Fecha,
                    p.OrigenFolio, p.PolizaAnuladaId is not null)),
                partidas, cuentas);
            if (request.Origen is { } o) armadas = [.. armadas.Where(f => f.Origen == o)];

            var proveedores = await terceros.ObtenerAsync(TipoAuxiliar.Proveedor,
                [.. armadas.Where(f => f.Origen == OrigenDiferenciaCambiaria.Proveedores && f.TerceroId is not null).Select(f => f.TerceroId!.Value).Distinct()], ct);
            var clientes = await terceros.ObtenerAsync(TipoAuxiliar.Cliente,
                [.. armadas.Where(f => f.Origen == OrigenDiferenciaCambiaria.Clientes && f.TerceroId is not null).Select(f => f.TerceroId!.Value).Distinct()], ct);
            var suc = (await sucursales.ListarAsync(ct)).ToDictionary(s => s.Id);
            filas = [.. armadas.Select(f =>
            {
                var t = f.TerceroId is { } id
                    ? (f.Origen == OrigenDiferenciaCambiaria.Proveedores ? proveedores : clientes).GetValueOrDefault(id) : null;
                return f with { TerceroClave = t?.Clave, TerceroNombre = t?.Nombre, SucursalNombre = suc.GetValueOrDefault(f.SucursalId)?.Nombre };
            })];
        }

        var filtros = new Dictionary<string, string?>
        {
            ["Desde"] = request.Desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Hasta"] = request.Hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Sucursal"] = request.SucursalId is { } fs
                ? (await sucursales.ListarAsync(ct)).FirstOrDefault(s => s.Id == fs)?.Nombre ?? fs.ToString() : "Todas",
            ["Origen"] = request.Origen is { } fo ? Etiqueta(fo) : "Pagos y cobros",
        };
        return new DiferenciasCambiariasResponse("Diferencias cambiarias realizadas", clock.UtcNow, filtros, Columnas, filas, Totales(filas),
            request.Desde, request.Hasta, avisos);
    }

    public static string Etiqueta(OrigenDiferenciaCambiaria o) => o == OrigenDiferenciaCambiaria.Proveedores ? "Pago a proveedor" : "Cobro de cliente";

    internal sealed record PolizaDiferencia(Guid Id, string? Folio, DateOnly Fecha, string? DocumentoFolio, bool EsAnulacion);

    internal sealed record PartidaDiferencia(Guid PolizaId, int Linea, Guid CuentaId, decimal Cargo, decimal Abono, string Moneda,
        decimal TipoCambio, decimal? ImporteOriginal, Guid SucursalId, Guid? ClienteId, Guid? ProveedorId);

    /// <summary>
    /// Arma una fila por póliza con partidas en las cuentas cambiarias. Pérdida = cargos − abonos de las cuentas de pérdida; utilidad =
    /// abonos − cargos de las de utilidad. TC de la factura: la primera partida en moneda extranjera del proveedor o cliente; TC del pago:
    /// la primera partida en moneda extranjera sin tercero con otro TC (bancos); si todas van al mismo TC, ese.
    /// </summary>
    internal static List<DiferenciaCambiariaFila> Armar(IEnumerable<PolizaDiferencia> polizas, IReadOnlyList<PartidaDiferencia> partidas,
        IReadOnlyDictionary<Guid, (OrigenDiferenciaCambiaria Origen, bool EsPerdida)> cuentas)
    {
        var porPoliza = partidas.GroupBy(d => d.PolizaId).ToDictionary(g => g.Key, g => g.OrderBy(d => d.Linea).ToList());
        var filas = new List<DiferenciaCambiariaFila>();
        foreach (var p in polizas)
        {
            if (!porPoliza.TryGetValue(p.Id, out var lineas)) continue;
            var cambiarias = lineas.Where(d => cuentas.ContainsKey(d.CuentaId)).ToList();
            if (cambiarias.Count == 0) continue;
            var origen = cuentas[cambiarias[0].CuentaId].Origen;
            var perdida = cambiarias.Where(d => cuentas[d.CuentaId].EsPerdida).Sum(d => d.Cargo - d.Abono);
            var utilidad = cambiarias.Where(d => !cuentas[d.CuentaId].EsPerdida).Sum(d => d.Abono - d.Cargo);

            var extranjeras = lineas.Where(d => d.Moneda != Poliza.MonedaNacional && !cuentas.ContainsKey(d.CuentaId)).ToList();
            var documento = extranjeras.Where(d => origen == OrigenDiferenciaCambiaria.Proveedores ? d.ProveedorId is not null : d.ClienteId is not null).ToList();
            var tcDocumento = documento.Count > 0 ? documento[0].TipoCambio : (decimal?)null;
            var sinTercero = extranjeras.Where(d => d.ProveedorId is null && d.ClienteId is null).ToList();
            var tcPago = sinTercero.FirstOrDefault(d => d.TipoCambio != tcDocumento)?.TipoCambio ?? sinTercero.FirstOrDefault()?.TipoCambio;
            decimal? importe = documento.Count > 0 ? documento.Sum(d => Math.Abs(d.ImporteOriginal ?? 0m)) * (p.EsAnulacion ? -1 : 1) : null;
            var tercero = documento.Select(d => origen == OrigenDiferenciaCambiaria.Proveedores ? d.ProveedorId : d.ClienteId).FirstOrDefault()
                ?? lineas.Select(d => origen == OrigenDiferenciaCambiaria.Proveedores ? d.ProveedorId : d.ClienteId).FirstOrDefault(x => x is not null);

            filas.Add(new DiferenciaCambiariaFila(p.Id, p.Folio, p.Fecha, origen, Etiqueta(origen), p.DocumentoFolio, tercero, null, null,
                cambiarias[0].SucursalId, null, documento.FirstOrDefault()?.Moneda ?? extranjeras.FirstOrDefault()?.Moneda, importe, tcDocumento,
                tcPago, perdida, utilidad, utilidad - perdida, p.EsAnulacion));
        }
        return [.. filas.OrderBy(f => f.Fecha).ThenBy(f => f.PolizaFolio, StringComparer.Ordinal)];
    }

    internal static TotalesDiferenciasCambiarias Totales(IReadOnlyList<DiferenciaCambiariaFila> filas)
    {
        decimal Suma(OrigenDiferenciaCambiaria o, Func<DiferenciaCambiariaFila, decimal> campo) => filas.Where(f => f.Origen == o).Sum(campo);
        var perdida = filas.Sum(f => f.Perdida);
        var utilidad = filas.Sum(f => f.Utilidad);
        return new TotalesDiferenciasCambiarias(perdida, utilidad, utilidad - perdida,
            Suma(OrigenDiferenciaCambiaria.Proveedores, f => f.Perdida), Suma(OrigenDiferenciaCambiaria.Proveedores, f => f.Utilidad),
            Suma(OrigenDiferenciaCambiaria.Clientes, f => f.Perdida), Suma(OrigenDiferenciaCambiaria.Clientes, f => f.Utilidad), filas.Count);
    }
}
