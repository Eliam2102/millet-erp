using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.ConciliacionAuxiliares;

public sealed record CuentaControlRef(Guid Id, string Codigo, string Nombre);

/// <summary>
/// Un control del resumen de CON-09. Saldos en MXN y en el sentido natural del control (clientes y bancos deudor; proveedores
/// acreedor). <c>Diferencia</c> = contable − auxiliar. <c>RolFaltante</c>: rol del motor que configurar cuando no hay cuenta.
/// </summary>
public sealed record ControlConciliacionResponse(
    ControlConciliacion Control, string Etiqueta, EstadoConciliacion Estado, string EstadoEtiqueta,
    decimal? SaldoContable, decimal? SaldoAuxiliar, decimal? Diferencia, IReadOnlyList<CuentaControlRef> Cuentas,
    string? RolFaltante, int RechazosPendientes, IReadOnlyList<string> Notas, int DiferenciasPorDocumento, string? EnlaceDetalleExterno);

public sealed record ConciliacionAuxiliaresResponse(
    DateOnly Corte, Guid? SucursalId, DateTimeOffset GeneradoEn, bool TodoCuadra, IReadOnlyList<ControlConciliacionResponse> Controles);

/// <summary>Columna del detalle (ADR-0036). <c>Clave</c> = propiedad camelCase de la fila.</summary>
public sealed record ColumnaConciliacionAuxiliar(string Clave, string Etiqueta, string Tipo, string? Alineacion = null);

/// <summary>Una diferencia del detalle de un control (por proveedor, documento sin póliza, partida sin auxiliar o cuenta bancaria).</summary>
public sealed record DiferenciaAuxiliarResponse(
    TipoDiferenciaAuxiliar Tipo, string TipoEtiqueta, Guid? TerceroId, string? TerceroClave, string? TerceroNombre,
    Guid? DocumentoId, string? Folio, string? Uuid, string? Moneda, decimal? SaldoAuxiliar, decimal? SaldoContable, decimal Diferencia,
    string Causa);

public sealed record ConciliacionAuxiliarDetalleResponse(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaConciliacionAuxiliar> Columnas, IReadOnlyList<DiferenciaAuxiliarResponse> Filas, ControlConciliacionResponse Totales);

/// <summary>
/// F1-CON-09 (86e3jvvm7): resumen de la conciliación de auxiliares al corte. Compara el saldo de las cuentas de control con el
/// total de su módulo: clientes (CxC), proveedores (CxP) y bancos (Tesorería). Contabilidad es corporativa: como la balanza y la
/// cuenta puente, la sucursal es un filtro opcional y no se limita por las sucursales del usuario.
/// </summary>
public sealed record ConciliacionAuxiliaresQuery(DateOnly Corte, Guid? SucursalId = null) : IRequest<ConciliacionAuxiliaresResponse>;

/// <summary>Detalle por documento de un control (proveedores o bancos). El de clientes es <i>CxC › Conciliación con pólizas</i>.</summary>
public sealed record ConciliacionAuxiliarDetalleQuery(ControlConciliacion Control, DateOnly Corte, Guid? SucursalId = null)
    : IRequest<ConciliacionAuxiliarDetalleResponse>;

public sealed class ConciliacionAuxiliaresValidator : AbstractValidator<ConciliacionAuxiliaresQuery>
{
    public ConciliacionAuxiliaresValidator()
    {
        RuleFor(q => q.Corte.Year).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).OverridePropertyName("corte");
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
    }
}

public sealed class ConciliacionAuxiliarDetalleValidator : AbstractValidator<ConciliacionAuxiliarDetalleQuery>
{
    public ConciliacionAuxiliarDetalleValidator()
    {
        RuleFor(q => q.Corte.Year).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).OverridePropertyName("corte");
        RuleFor(q => q.SucursalId).NotEqual(Guid.Empty).When(q => q.SucursalId is not null);
        RuleFor(q => q.Control).Must(c => c is ControlConciliacion.Proveedores or ControlConciliacion.Bancos)
            .WithMessage("El detalle de clientes está en Cuentas por cobrar › Conciliación con pólizas.");
    }
}

/// <summary>Cálculo de CON-09, compartido por el reporte y por el puerto de resumen que consumirá el cierre (CON-12).</summary>
public sealed class CalculadoraConciliacionAuxiliares(
    ContabilidadDbContext db, IAuxiliarProveedoresReadPort proveedores, ISaldoCarteraAlCorteReadPort cartera,
    ISaldosBancariosAlCorteReadPort bancos, ITerceroContabilidadPort terceros)
{
    public const string RolClientes = "CLIENTES", RolProveedores = "PROVEEDORES", RolBancos = "BANCOS";
    private const string EventoFacturaProveedor = "cuentas_por_pagar.factura.registrada.v1";
    private const string EnlaceClientes = "/cxc/conciliacion-polizas";

    public sealed record ResultadoControl(ControlConciliacionResponse Resumen, IReadOnlyList<DiferenciaAuxiliarResponse> Diferencias);

    public async Task<IReadOnlyList<ResultadoControl>> CalcularAsync(DateOnly corte, Guid? sucursalId, CancellationToken ct) =>
    [
        await ClientesAsync(corte, sucursalId, ct),
        await ProveedoresAsync(corte, sucursalId, ct),
        await BancosAsync(corte, sucursalId, ct),
    ];

    public async Task<ResultadoControl> CalcularAsync(ControlConciliacion control, DateOnly corte, Guid? sucursalId, CancellationToken ct) =>
        control switch
        {
            ControlConciliacion.Clientes => await ClientesAsync(corte, sucursalId, ct),
            ControlConciliacion.Proveedores => await ProveedoresAsync(corte, sucursalId, ct),
            _ => await BancosAsync(corte, sucursalId, ct),
        };

    // ── Cuentas de control ────────────────────────────────────────────

    /// <summary>
    /// Cuentas del control (supuesto a validar): primero las marcadas con <see cref="CuentaControl"/> en el catálogo; después las de
    /// los roles del motor (el rol y sus variantes <c>ROL_*</c>, como resuelve el motor) y, para bancos, la cuenta de cada cuenta bancaria.
    /// </summary>
    private async Task<List<CuentaControlRef>> CuentasAsync(CuentaControl? marca, string rol, IEnumerable<string> codigosExtra, CancellationToken ct)
    {
        var prefijo = rol + "_";
        var deRoles = await db.RolesCuenta.AsNoTracking()
            .Where(r => (r.Codigo == rol || r.Codigo.StartsWith(prefijo)) && r.DeletedAt == null && r.CuentaId != null)
            .Select(r => r.CuentaId!.Value).Distinct().ToListAsync(ct);
        var codigos = codigosExtra.Select(FormatoCatalogo.Codigo).OfType<string>().Distinct().ToList();
        return await db.Cuentas.AsNoTracking()
            .Where(c => (marca != null && c.CuentaControl == marca) || deRoles.Contains(c.Id) || codigos.Contains(c.Codigo))
            .OrderBy(c => c.Codigo).Select(c => new CuentaControlRef(c.Id, c.Codigo, c.Nombre)).ToListAsync(ct);
    }

    private IQueryable<PolizaPartida> PartidasAlCorte(DateOnly corte, IReadOnlyCollection<Guid> cuentas, Guid? sucursalId) =>
        from p in db.Polizas.AsNoTracking()
        join d in db.PolizasPartidas.AsNoTracking() on p.Id equals d.PolizaId
        where p.Fecha <= corte && p.Estado != EstadoPoliza.Borrador && cuentas.Contains(d.CuentaId)
            && (sucursalId == null || d.SucursalId == sucursalId)
        select d;

    private Task<int> RechazosRolSinCuentaAsync(string rol, CancellationToken ct) =>
        db.RechazosContables.AsNoTracking()
            .CountAsync(r => r.Estado == EstadoRechazoContable.Pendiente && r.Causa == CausaRechazoContable.RolSinCuenta
                && r.Detalle != null && r.Detalle.Contains(rol), ct);

    private static ControlConciliacionResponse Resumen(ControlConciliacion control, List<CuentaControlRef> cuentas, decimal? contable,
        decimal? auxiliar, string rol, int rechazos, List<string> notas, int diferencias, string? enlace = null)
    {
        var estado = ComparadorAuxiliares.Estado(cuentas.Count > 0, contable, auxiliar);
        if (estado == EstadoConciliacion.SinCuenta)
            notas.Insert(0, $"Asigna una cuenta al rol {rol} en Contabilidad › Motor › Roles, o marca la cuenta de control en el catálogo.");
        return new(control, ComparadorAuxiliares.Etiqueta(control), estado, ComparadorAuxiliares.Etiqueta(estado),
            estado == EstadoConciliacion.SinCuenta ? null : contable, auxiliar,
            estado is EstadoConciliacion.Cuadra or EstadoConciliacion.Diferencia ? contable - auxiliar : null,
            cuentas, estado == EstadoConciliacion.SinCuenta ? rol : null, rechazos, notas, diferencias, enlace);
    }

    // ── Clientes: solo totales (el detalle es de CxC › Conciliación con pólizas) ──

    private async Task<ResultadoControl> ClientesAsync(DateOnly corte, Guid? sucursalId, CancellationToken ct)
    {
        var cuentas = await CuentasAsync(CuentaControl.Clientes, RolClientes, [], ct);
        var ids = cuentas.Select(c => c.Id).ToList();
        decimal? contable = ids.Count == 0 ? null : await PartidasAlCorte(corte, ids, sucursalId).SumAsync(d => d.Cargo - d.Abono, ct);
        var notas = new List<string>();
        decimal? auxiliar = null;
        if (sucursalId is not null)
            notas.Add("La cartera de clientes no se separa por sucursal: con filtro de sucursal no se compara.");
        else
        {
            var saldos = await cartera.LeerAsync(corte, ct);
            auxiliar = saldos.Where(s => s.Moneda == Poliza.MonedaNacional).Sum(s => s.Saldo);
            foreach (var s in saldos.Where(s => s.Moneda != Poliza.MonedaNacional && s.Saldo != 0))
                notas.Add($"Cartera en {s.Moneda} sin convertir: {s.Saldo:N2} {s.Moneda} en {s.Documentos} factura(s). " +
                    "La póliza la registra en MXN; parte de la diferencia puede venir de ahí.");
        }
        notas.Add("El detalle de cobros contra pólizas está en Cuentas por cobrar › Conciliación con pólizas.");
        var rechazos = await RechazosRolSinCuentaAsync(RolClientes, ct);
        return new(Resumen(ControlConciliacion.Clientes, cuentas, contable, auxiliar, RolClientes, rechazos, notas, 0, EnlaceClientes), []);
    }

    // ── Proveedores: totales + diferencias por proveedor y documentos sin póliza (CON-09-a) ──

    private async Task<ResultadoControl> ProveedoresAsync(DateOnly corte, Guid? sucursalId, CancellationToken ct)
    {
        var cuentas = await CuentasAsync(CuentaControl.Proveedores, RolProveedores, [], ct);
        var ids = cuentas.Select(c => c.Id).ToList();
        var aux = await proveedores.LeerAsync(corte, sucursalId, ct);
        var notas = new List<string>();
        if (aux.HistorialPorConfirmar > 0)
            notas.Add($"{aux.HistorialPorConfirmar} factura(s) de proveedor con aplicaciones anteriores sin fecha: su saldo al corte es el actual.");

        var porProveedorAux = aux.Documentos.GroupBy(d => d.ProveedorId).ToDictionary(g => g.Key, g => g.Sum(d => d.SaldoMxn));
        var auxiliar = aux.Documentos.Sum(d => d.SaldoMxn);
        var filas = new List<DiferenciaAuxiliarResponse>();
        decimal? contable = null;
        if (ids.Count > 0)
        {
            var porTercero = await PartidasAlCorte(corte, ids, sucursalId)
                .GroupBy(d => d.ProveedorId).Select(g => new { g.Key, Neto = g.Sum(d => d.Cargo - d.Abono) }).ToListAsync(ct);
            contable = ComparadorAuxiliares.SaldoAcreedor(porTercero.Sum(x => x.Neto));
            var contablePorProveedor = porTercero.Where(x => x.Key != null)
                .ToDictionary(x => x.Key!.Value, x => ComparadorAuxiliares.SaldoAcreedor(x.Neto));
            var sinAuxiliar = porTercero.Where(x => x.Key == null).Sum(x => ComparadorAuxiliares.SaldoAcreedor(x.Neto));

            var difs = ComparadorAuxiliares.DiferenciasPorTercero(contablePorProveedor, porProveedorAux);
            var nombres = await terceros.ObtenerAsync(TipoAuxiliar.Proveedor, [.. difs.Select(d => d.ProveedorId)
                .Concat(aux.Documentos.Select(d => d.ProveedorId)).Distinct()], ct);
            filas.AddRange(difs.Select(d =>
            {
                var n = nombres.GetValueOrDefault(d.ProveedorId);
                return new DiferenciaAuxiliarResponse(TipoDiferenciaAuxiliar.Proveedor, ComparadorAuxiliares.Etiqueta(TipoDiferenciaAuxiliar.Proveedor),
                    d.ProveedorId, n?.Clave, n?.Nombre, null, null, null, Poliza.MonedaNacional, d.Auxiliar, d.Contable, d.Contable - d.Auxiliar,
                    "El saldo del auxiliar y el de las pólizas no coinciden (en moneda extranjera puede ser el tipo de cambio).");
            }));
            filas.AddRange(await SinPolizaAsync(aux.Documentos, corte, nombres, ct));
            if (Math.Abs(sinAuxiliar) > ComparadorAuxiliares.Tolerancia)
                filas.Add(new(TipoDiferenciaAuxiliar.PartidaSinAuxiliar, ComparadorAuxiliares.Etiqueta(TipoDiferenciaAuxiliar.PartidaSinAuxiliar),
                    null, null, null, null, null, null, Poliza.MonedaNacional, 0m, sinAuxiliar, sinAuxiliar,
                    "Partidas de la cuenta de control sin proveedor: captura manual o movimiento sin auxiliar."));
        }
        var rechazos = await RechazosRolSinCuentaAsync(RolProveedores, ct);
        var resumen = Resumen(ControlConciliacion.Proveedores, cuentas, contable, auxiliar, RolProveedores, rechazos, notas, filas.Count);
        return new(resumen, resumen.Estado == EstadoConciliacion.SinCuenta ? [] : filas);
    }

    /// <summary>CON-09-a: facturas de proveedor con saldo al corte cuya póliza no existe al corte, con la causa del motor.</summary>
    private async Task<IEnumerable<DiferenciaAuxiliarResponse>> SinPolizaAsync(IReadOnlyList<DocumentoProveedorAlCorte> documentos, DateOnly corte,
        IReadOnlyDictionary<Guid, AuxiliarContable> nombres, CancellationToken ct)
    {
        if (documentos.Count == 0) return [];
        var docIds = documentos.Select(d => d.FacturaId).ToList();
        var polizas = await db.Polizas.AsNoTracking()
            .Where(p => p.OrigenModulo == "cuentas_por_pagar" && p.OrigenTipo == "factura_proveedor" && p.Estado != EstadoPoliza.Borrador
                && p.OrigenDocumentoId != null && docIds.Contains(p.OrigenDocumentoId.Value))
            .GroupBy(p => p.OrigenDocumentoId!.Value).Select(g => new { g.Key, Primera = g.Min(p => p.Fecha) }).ToDictionaryAsync(x => x.Key, x => x.Primera, ct);
        var sin = documentos.Where(d => !polizas.TryGetValue(d.FacturaId, out var f) || f > corte).ToList();
        if (sin.Count == 0) return [];
        var rechazos = await db.RechazosContables.AsNoTracking()
            .Where(r => r.Estado == EstadoRechazoContable.Pendiente && r.TipoEvento == EventoFacturaProveedor)
            .Select(r => new { r.Payload, r.Causa, r.Mensaje }).ToListAsync(ct);
        return sin.Select(d =>
        {
            var id = d.FacturaId.ToString();
            var rechazo = rechazos.FirstOrDefault(r => r.Payload.Contains(id, StringComparison.OrdinalIgnoreCase));
            var n = nombres.GetValueOrDefault(d.ProveedorId);
            var causa = ComparadorAuxiliares.CausaSinPoliza(rechazo is null ? null : $"{rechazo.Causa} · {rechazo.Mensaje}",
                polizas.TryGetValue(d.FacturaId, out var f) ? f : null);
            return new DiferenciaAuxiliarResponse(TipoDiferenciaAuxiliar.DocumentoSinPoliza, ComparadorAuxiliares.Etiqueta(TipoDiferenciaAuxiliar.DocumentoSinPoliza),
                d.ProveedorId, n?.Clave, n?.Nombre, d.FacturaId, d.Folio, d.Uuid, d.Moneda, d.SaldoMxn, 0m, -d.SaldoMxn, causa);
        });
    }

    // ── Bancos: por cuenta bancaria, contable (partidas con esa cuenta bancaria) contra libros de Tesorería ──

    private async Task<ResultadoControl> BancosAsync(DateOnly corte, Guid? sucursalId, CancellationToken ct)
    {
        var libros = await bancos.LeerAsync(corte, ct);
        var cuentas = await CuentasAsync(null, RolBancos, libros.Select(b => b.CuentaContableRef).OfType<string>(), ct);
        var ids = cuentas.Select(c => c.Id).ToList();
        var notas = new List<string>();
        var filas = new List<DiferenciaAuxiliarResponse>();
        decimal? contable = null, auxiliar = null;
        if (sucursalId is not null)
            notas.Add("Los libros de bancos no se separan por sucursal: con filtro de sucursal no se compara.");
        if (ids.Count > 0)
        {
            var porCuentaBancaria = await PartidasAlCorte(corte, ids, sucursalId)
                .GroupBy(d => d.CuentaBancariaId).Select(g => new { g.Key, Neto = g.Sum(d => d.Cargo - d.Abono) }).ToListAsync(ct);
            contable = porCuentaBancaria.Sum(x => x.Neto);
            var neto = porCuentaBancaria.Where(x => x.Key != null).ToDictionary(x => x.Key!.Value, x => x.Neto);
            var mxn = libros.Where(b => b.Moneda == Poliza.MonedaNacional).ToList();
            foreach (var b in libros.Where(b => b.Moneda != Poliza.MonedaNacional))
                notas.Add($"{b.Banco} {Ultimos4(b.NumeroCuenta)} está en {b.Moneda}: sus libros no se convierten; se compara solo lo MXN.");
            foreach (var b in mxn.Where(b => b.SaldoLibros is null))
                notas.Add($"{b.Banco} {Ultimos4(b.NumeroCuenta)}: el corte es anterior a su saldo inicial; no tiene libros a esa fecha.");
            if (sucursalId is null)
            {
                auxiliar = mxn.Sum(b => b.SaldoLibros ?? 0m);
                var contableMxn = neto.Where(x => mxn.Any(b => b.CuentaBancariaId == x.Key)).Sum(x => x.Value)
                    + porCuentaBancaria.Where(x => x.Key == null).Sum(x => x.Neto);
                contable = contableMxn;
                foreach (var b in mxn.Where(b => b.SaldoLibros is not null))
                {
                    var c = neto.GetValueOrDefault(b.CuentaBancariaId);
                    if (Math.Abs(c - b.SaldoLibros!.Value) <= ComparadorAuxiliares.Tolerancia) continue;
                    filas.Add(new(TipoDiferenciaAuxiliar.CuentaBancaria, ComparadorAuxiliares.Etiqueta(TipoDiferenciaAuxiliar.CuentaBancaria),
                        b.CuentaBancariaId, Ultimos4(b.NumeroCuenta), b.Banco, null, null, null, b.Moneda, b.SaldoLibros, c, c - b.SaldoLibros.Value,
                        c == 0m ? "Movimientos de Tesorería sin póliza en la cuenta de bancos."
                            : "El saldo en libros y el de las pólizas de la cuenta bancaria no coinciden."));
                }
                var sinCuenta = porCuentaBancaria.Where(x => x.Key == null).Sum(x => x.Neto);
                if (Math.Abs(sinCuenta) > ComparadorAuxiliares.Tolerancia)
                    filas.Add(new(TipoDiferenciaAuxiliar.PartidaSinAuxiliar, ComparadorAuxiliares.Etiqueta(TipoDiferenciaAuxiliar.PartidaSinAuxiliar),
                        null, null, null, null, null, null, Poliza.MonedaNacional, 0m, sinCuenta, sinCuenta,
                        "Partidas de cuentas de bancos sin cuenta bancaria: captura manual o movimiento sin auxiliar."));
            }
        }
        var rechazos = await RechazosRolSinCuentaAsync(RolBancos, ct);
        var resumen = Resumen(ControlConciliacion.Bancos, cuentas, contable, auxiliar, RolBancos, rechazos, notas, filas.Count);
        return new(resumen, resumen.Estado == EstadoConciliacion.SinCuenta ? [] : filas);
    }

    private static string Ultimos4(string numero) => numero.Length <= 4 ? numero : "…" + numero[^4..];
}

public sealed class ConciliacionAuxiliaresHandler(CalculadoraConciliacionAuxiliares calculadora, IClock clock)
    : IRequestHandler<ConciliacionAuxiliaresQuery, ConciliacionAuxiliaresResponse>
{
    public async Task<ConciliacionAuxiliaresResponse> Handle(ConciliacionAuxiliaresQuery request, CancellationToken cancellationToken)
    {
        var controles = (await calculadora.CalcularAsync(request.Corte, request.SucursalId, cancellationToken)).Select(r => r.Resumen).ToList();
        return new(request.Corte, request.SucursalId, clock.UtcNow, controles.All(c => c.Estado == EstadoConciliacion.Cuadra), controles);
    }
}

public sealed class ConciliacionAuxiliarDetalleHandler(CalculadoraConciliacionAuxiliares calculadora, IClock clock)
    : IRequestHandler<ConciliacionAuxiliarDetalleQuery, ConciliacionAuxiliarDetalleResponse>
{
    private static readonly ColumnaConciliacionAuxiliar[] Columnas =
    [
        new("tipoEtiqueta", "Diferencia", "texto"),
        new("terceroNombre", "Proveedor / banco", "texto"),
        new("folio", "Documento", "texto"),
        new("moneda", "Moneda", "texto"),
        new("saldoAuxiliar", "Saldo auxiliar (MXN)", "moneda", "derecha"),
        new("saldoContable", "Saldo contable (MXN)", "moneda", "derecha"),
        new("diferencia", "Diferencia", "moneda", "derecha"),
        new("causa", "Causa probable", "texto"),
    ];

    public async Task<ConciliacionAuxiliarDetalleResponse> Handle(ConciliacionAuxiliarDetalleQuery request, CancellationToken cancellationToken)
    {
        var r = await calculadora.CalcularAsync(request.Control, request.Corte, request.SucursalId, cancellationToken);
        var filtros = new Dictionary<string, string?>
        {
            ["corte"] = request.Corte.ToString("yyyy-MM-dd"),
            ["sucursalId"] = request.SucursalId?.ToString(),
            ["control"] = ComparadorAuxiliares.Etiqueta(request.Control),
        };
        return new($"Conciliación de auxiliares · {ComparadorAuxiliares.Etiqueta(request.Control)}", clock.UtcNow, filtros, Columnas,
            r.Diferencias, r.Resumen);
    }
}

/// <summary>Adaptador del puerto público de resumen para el cierre (CON-12).</summary>
public sealed class ConciliacionAuxiliaresResumenAdapter(CalculadoraConciliacionAuxiliares calculadora) : IConciliacionAuxiliaresResumenReadPort
{
    public async Task<ResumenConciliacionAuxiliares> ObtenerAsync(DateOnly corte, CancellationToken ct)
    {
        var controles = (await calculadora.CalcularAsync(corte, null, ct))
            .Select(r => new ControlConciliacionResumen(r.Resumen.Etiqueta, r.Resumen.EstadoEtiqueta, r.Resumen.Estado == EstadoConciliacion.Cuadra,
                r.Resumen.Diferencia)).ToList();
        return new(corte, controles.All(c => c.Cuadra), controles);
    }
}
