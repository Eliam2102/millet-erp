using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Revaluacion;

/// <summary>Bancos o clientes: las cuentas de control que revalúa Contabilidad (proveedores los revalúa CxP, CXP-12).</summary>
public enum ControlRevaluacion { Bancos = 0, Clientes = 1 }

/// <summary>Un saldo en moneda extranjera revaluado: cuenta, sucursal, moneda y, en clientes, el cliente (auxiliar).</summary>
public sealed record RevaluacionCierreDetalle(ControlRevaluacion Control, Guid CuentaId, string CuentaCodigo, Guid SucursalId, Guid? ClienteId,
    Guid? CuentaBancariaId, string Moneda, decimal SaldoMoneda, decimal SaldoLibros, decimal TipoCambioCierre, decimal SaldoCierre, decimal Diferencia)
{
    /// <summary>Positivo = utilidad cambiaria no realizada (el activo en pesos sube); negativo = pérdida.</summary>
    public string Resultado => Diferencia > 0 ? "Utilidad" : Diferencia < 0 ? "Pérdida" : "Sin diferencia";
}

public sealed record RevaluacionCierreContableResponse(int Anio, int Mes, DateOnly FechaCierre, DateOnly FechaReversa, bool Ejecutada,
    IReadOnlyList<RevaluacionPolizaResponse> Polizas, IReadOnlyList<RevaluacionCierreDetalle> Detalles, decimal TotalUtilidad, decimal TotalPerdida,
    IReadOnlyList<string> Avisos, IReadOnlyList<RevaluacionPolizaPrevia>? Previas = null);

/// <summary>M-317: póliza que se registraría para una sucursal (vista previa; no existe en la base).</summary>
public sealed record RevaluacionPolizaPrevia(Guid SucursalId, string Concepto, IReadOnlyList<RevaluacionPartidaPrevia> Partidas, decimal TotalCargo, decimal TotalAbono);

public sealed record RevaluacionPartidaPrevia(string CuentaCodigo, string Concepto, decimal Cargo, decimal Abono);

public sealed record RevaluacionPolizaResponse(Guid Id, string? Folio, DateOnly Fecha, Guid? SucursalId, bool EsReversa);

/// <summary>
/// CON-11 (86e3kdypd) · revaluación de cierre (no realizada) de bancos y clientes en moneda extranjera (Cuestionario 08, C-10; R14):
/// saldo en su moneda × TC del último día del mes (K10.6) contra el saldo en pesos en libros; la diferencia va a utilidad o pérdida
/// cambiaria no realizada del control (roles con sufijo _BANCOS / _CLIENTES; el base es el de proveedores, CXP-12) y se revierte sola el día 1 del mes siguiente. Contabilidad la calcula de sus
/// propias partidas (moneda, TC e importe original); no lee esquemas de Tesorería ni de CxC.
/// </summary>
public sealed class CalculadoraRevaluacionCierre(ContabilidadDbContext db, ITipoCambioContabilidadReadPort tiposCambio)
{
    public const string Modulo = "contabilidad", TipoRevaluacion = "revaluacion_cierre", TipoReversa = "revaluacion_cierre_reversa";
    public const string RolBancos = "BANCOS", RolClientes = "CLIENTES";
    public const string RolPerdida = "PERDIDA_CAMBIARIA_NO_REALIZADA", RolUtilidad = "UTILIDAD_CAMBIARIA_NO_REALIZADA";

    /// <summary>
    /// Rol de pérdida o utilidad no realizada de cada control: el catálogo de Contabilidad tiene una subcuenta por origen (clientes,
    /// proveedores, bancos), así que el rol lleva el sufijo del control (<c>PERDIDA_CAMBIARIA_NO_REALIZADA_BANCOS</c>, <c>…_CLIENTES</c>).
    /// El rol base sin sufijo es el de proveedores (CXP-12).
    /// </summary>
    public static string RolResultado(string rolBase, ControlRevaluacion control) =>
        $"{rolBase}_{(control == ControlRevaluacion.Bancos ? "BANCOS" : "CLIENTES")}";

    public static DateOnly Cierre(int anio, int mes) => new(anio, mes, DateTime.DaysInMonth(anio, mes));

    /// <summary>Documento origen de la revaluación del mes: el mismo id para todas las sucursales (estable por empresa y mes).</summary>
    public static Guid DocumentoId(Guid empresaId, int anio, int mes) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{TipoRevaluacion}:{empresaId}:{anio:0000}-{mes:00}")).AsSpan(0, 16));

    public async Task<IReadOnlyList<RevaluacionCierreDetalle>> CalcularAsync(int anio, int mes, CancellationToken ct)
    {
        var cierre = Cierre(anio, mes);
        var detalles = new List<RevaluacionCierreDetalle>();
        foreach (var (control, rol) in new[] { (ControlRevaluacion.Bancos, RolBancos), (ControlRevaluacion.Clientes, RolClientes) })
        {
            var cuentas = await CuentasDelRolAsync(rol, ct);
            if (cuentas.Count == 0) continue;
            var ids = cuentas.Keys.ToList();
            var partidas = await (from p in db.Polizas.AsNoTracking()
                                  join d in db.PolizasPartidas.AsNoTracking() on p.Id equals d.PolizaId
                                  where p.Fecha <= cierre && p.Estado != EstadoPoliza.Borrador && ids.Contains(d.CuentaId)
                                      && d.Moneda != Poliza.MonedaNacional
                                  select new MovimientoMoneda(d.CuentaId, d.SucursalId, control == ControlRevaluacion.Clientes ? d.ClienteId : null,
                                      control == ControlRevaluacion.Bancos ? d.CuentaBancariaId : null, d.Moneda, d.Cargo, d.Abono, d.ImporteOriginal))
                .ToListAsync(ct);
            foreach (var g in partidas.GroupBy(x => (x.CuentaId, x.SucursalId, x.ClienteId, x.CuentaBancariaId, x.Moneda)))
            {
                var (moneda, libros) = Saldos(g);
                if (moneda == 0m && libros == 0m) continue;
                var tc = await tiposCambio.ValorEnMxnAsync(g.Key.Moneda, cierre, ct) ?? throw new BusinessRuleException("REVALUACION_SIN_TC_CIERRE",
                    $"Falta el tipo de cambio de {g.Key.Moneda} del {cierre:dd/MM/yyyy} en el catálogo (K10.6). Cárgalo y vuelve a correr la revaluación.");
                var alCierre = Math.Round(moneda * tc, 2, MidpointRounding.AwayFromZero);
                detalles.Add(new(control, g.Key.CuentaId, cuentas[g.Key.CuentaId], g.Key.SucursalId, g.Key.ClienteId, g.Key.CuentaBancariaId,
                    g.Key.Moneda, moneda, libros, tc, alCierre, alCierre - libros));
            }
        }
        return detalles;
    }

    /// <summary>
    /// Saldo en la moneda y en pesos de un grupo de partidas. El importe original toma el signo del lado (cargo +, abono −) y el de la
    /// partida (una anulación K10.3 lleva importes negativos).
    /// </summary>
    internal static (decimal Moneda, decimal Libros) Saldos(IEnumerable<MovimientoMoneda> movimientos)
    {
        decimal moneda = 0m, libros = 0m;
        foreach (var m in movimientos)
        {
            var importe = m.Cargo != 0m ? m.Cargo : -m.Abono;
            var original = Math.Abs(m.ImporteOriginal ?? 0m) * Math.Sign(importe);
            moneda += original;
            libros += m.Cargo - m.Abono;
        }
        return (moneda, libros);
    }

    /// <summary>Cuentas de los roles del motor (el rol y sus variantes <c>ROL_*</c>, como en la conciliación de auxiliares CON-09).</summary>
    private async Task<Dictionary<Guid, string>> CuentasDelRolAsync(string rol, CancellationToken ct)
    {
        var prefijo = rol + "_";
        var ids = await db.RolesCuenta.AsNoTracking()
            .Where(r => (r.Codigo == rol || r.Codigo.StartsWith(prefijo)) && r.DeletedAt == null && r.CuentaId != null)
            .Select(r => r.CuentaId!.Value).Distinct().ToListAsync(ct);
        return await db.Cuentas.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Codigo, ct);
    }

    internal sealed record MovimientoMoneda(Guid CuentaId, Guid SucursalId, Guid? ClienteId, Guid? CuentaBancariaId, string Moneda,
        decimal Cargo, decimal Abono, decimal? ImporteOriginal);

    /// <summary>
    /// Partidas de la póliza de una sucursal: el ajuste a cada cuenta (con su auxiliar) y, por control, el neto a utilidad o pérdida no
    /// realizada en la cuenta de ese control. Activo: diferencia positiva = cargo a la cuenta y abono a utilidad; negativa = abono a la
    /// cuenta y cargo a pérdida.
    /// </summary>
    internal static List<PartidaRequest> Partidas(IReadOnlyList<RevaluacionCierreDetalle> detalles, Guid sucursal,
        IReadOnlyDictionary<ControlRevaluacion, (Guid Perdida, Guid Utilidad)> cuentasResultado)
    {
        var partidas = new List<PartidaRequest>();
        foreach (var d in detalles.Where(d => d.Diferencia != 0m))
        {
            var concepto = d.Control == ControlRevaluacion.Bancos ? $"Revaluación de bancos {d.Moneda} (TC {d.TipoCambioCierre:0.####})"
                : $"Revaluación de clientes {d.Moneda} (TC {d.TipoCambioCierre:0.####})";
            partidas.Add(new PartidaRequest(d.CuentaId, concepto, d.Diferencia > 0 ? d.Diferencia : 0m, d.Diferencia < 0 ? -d.Diferencia : 0m,
                sucursal, ClienteId: d.ClienteId, CuentaBancariaId: d.CuentaBancariaId, Moneda: Poliza.MonedaNacional, TipoCambio: 1m,
                Origen: d.ClienteId is null ? OrigenMovimiento.Automatica : OrigenMovimiento.AuxiliarCxC));
        }
        foreach (var porControl in detalles.GroupBy(d => d.Control).OrderBy(g => g.Key))
        {
            var (cuentaPerdida, cuentaUtilidad) = cuentasResultado[porControl.Key];
            var origen = porControl.Key == ControlRevaluacion.Bancos ? "bancos" : "clientes";
            var utilidad = porControl.Where(d => d.Diferencia > 0).Sum(d => d.Diferencia);
            var perdida = -porControl.Where(d => d.Diferencia < 0).Sum(d => d.Diferencia);
            if (perdida > 0) partidas.Add(new PartidaRequest(cuentaPerdida, $"Pérdida cambiaria no realizada {origen}", perdida, 0m, sucursal,
                Moneda: Poliza.MonedaNacional, TipoCambio: 1m, Origen: OrigenMovimiento.Automatica));
            if (utilidad > 0) partidas.Add(new PartidaRequest(cuentaUtilidad, $"Utilidad cambiaria no realizada {origen}", 0m, utilidad, sucursal,
                Moneda: Poliza.MonedaNacional, TipoCambio: 1m, Origen: OrigenMovimiento.Automatica));
        }
        return partidas;
    }

    /// <summary>Lados contrarios de cada partida (la reversa del día 1).</summary>
    internal static PartidaRequest Contraria(PolizaPartida p) => new(p.CuentaId, $"Reversa: {p.Concepto}".Trim(), p.Abono, p.Cargo, p.SucursalId,
        p.Dim1Id, p.Dim2Id, p.Dim3Id, p.Proyecto, p.ClienteId, p.ProveedorId, p.CuentaBancariaId, p.Moneda, p.TipoCambio, p.ImporteOriginal,
        p.ClienteId is null && p.ProveedorId is null ? OrigenMovimiento.Automatica : p.ClienteId is null ? OrigenMovimiento.AuxiliarCxP : OrigenMovimiento.AuxiliarCxC);
}

// ---------------------------------------------------------------- Consulta

public sealed record ConsultarRevaluacionCierreQuery(int Anio, int Mes) : IRequest<RevaluacionCierreContableResponse>;

public sealed class ConsultarRevaluacionCierreValidator : AbstractValidator<ConsultarRevaluacionCierreQuery>
{
    public ConsultarRevaluacionCierreValidator()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2000, 2100).WithMessage("Año inválido.");
        RuleFor(c => c.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
    }
}

/// <summary>Vista previa (sin ejecutar) o, si ya se corrió, las pólizas del mes con el cálculo vigente.</summary>
public sealed class ConsultarRevaluacionCierreHandler(ContabilidadDbContext db, CalculadoraRevaluacionCierre calculadora, ICurrentEmpresaContext empresa)
    : IRequestHandler<ConsultarRevaluacionCierreQuery, RevaluacionCierreContableResponse>
{
    public async Task<RevaluacionCierreContableResponse> Handle(ConsultarRevaluacionCierreQuery q, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(ConsultarRevaluacionCierreQuery));
        var polizas = await RevaluacionCierreComun.PolizasDelMesAsync(db, empresaId, q.Anio, q.Mes, ct);
        var avisos = new List<string>();
        IReadOnlyList<RevaluacionCierreDetalle> detalles;
        var previas = new List<RevaluacionPolizaPrevia>();
        try
        {
            detalles = polizas.Count > 0 ? [] : await calculadora.CalcularAsync(q.Anio, q.Mes, ct);
            // M-317: la vista previa arma las pólizas por sucursal con la misma lógica que ejecutar, sin registrar nada.
            if (detalles.Count > 0) previas.AddRange(await RevaluacionCierreComun.PreviasAsync(db, detalles, CalculadoraRevaluacionCierre.Cierre(q.Anio, q.Mes), q.Mes, q.Anio, ct));
        }
        catch (BusinessRuleException e) { detalles = []; avisos.Add(e.Message); }
        return RevaluacionCierreComun.Respuesta(q.Anio, q.Mes, polizas, detalles, avisos, previas);
    }
}

// ---------------------------------------------------------------- Ejecutar

public sealed record EjecutarRevaluacionCierreContableCommand(int Anio, int Mes) : IRequest<RevaluacionCierreContableResponse>;

public sealed class EjecutarRevaluacionCierreContableValidator : AbstractValidator<EjecutarRevaluacionCierreContableCommand>
{
    public EjecutarRevaluacionCierreContableValidator()
    {
        RuleFor(c => c.Anio).InclusiveBetween(2000, 2100).WithMessage("Año inválido.");
        RuleFor(c => c.Mes).InclusiveBetween(1, 12).WithMessage("Mes inválido.");
    }
}

/// <summary>
/// Cierre del mes: antes revierte lo que quedara pendiente de meses anteriores (para que el saldo en libros no arrastre otra revaluación),
/// calcula y registra una póliza automática por sucursal con fecha del último día del mes. Una sola vez por mes y empresa.
/// </summary>
public sealed class EjecutarRevaluacionCierreContableHandler(ContabilidadDbContext db, CalculadoraRevaluacionCierre calculadora,
    IPolizaContablePort puerto, ICurrentEmpresaContext empresa, IClock clock, IMediator mediator)
    : IRequestHandler<EjecutarRevaluacionCierreContableCommand, RevaluacionCierreContableResponse>
{
    public async Task<RevaluacionCierreContableResponse> Handle(EjecutarRevaluacionCierreContableCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(EjecutarRevaluacionCierreContableCommand));
        var cierre = CalculadoraRevaluacionCierre.Cierre(c.Anio, c.Mes);
        if (cierre > FechaContable.HoyLocal(clock.UtcNow))
            throw new BusinessRuleException("REVALUACION_MES_NO_CERRADO", "La revaluación se corre al cierre: el último día del mes aún no llega.");
        if ((await RevaluacionCierreComun.PolizasDelMesAsync(db, empresaId, c.Anio, c.Mes, ct)).Count > 0)
            throw new ConflictException("REVALUACION_YA_EJECUTADA", $"El mes {c.Mes:00}/{c.Anio} ya se revaluó.");
        await mediator.Send(new PublicarReversasRevaluacionContableCommand(cierre), ct);

        var detalles = await calculadora.CalcularAsync(c.Anio, c.Mes, ct);
        var docId = CalculadoraRevaluacionCierre.DocumentoId(empresaId, c.Anio, c.Mes);
        // M-317: tras anular la revaluación del mes se puede correr otra vez; la llave lleva cuántas pólizas de este mes ya se anularon.
        var anuladas = await db.Polizas.CountAsync(p => p.OrigenModulo == CalculadoraRevaluacionCierre.Modulo
            && p.OrigenTipo == CalculadoraRevaluacionCierre.TipoRevaluacion && p.OrigenDocumentoId == docId && p.AnuladaPorPolizaId != null, ct);
        foreach (var (sucursal, partidas) in await RevaluacionCierreComun.ArmarAsync(db, detalles, cierre, ct))
        {
            await puerto.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica("AS", cierre, $"Revaluación de bancos y clientes en moneda extranjera {c.Mes:00}/{c.Anio}",
                new DocumentoOrigenPoliza(CalculadoraRevaluacionCierre.Modulo, CalculadoraRevaluacionCierre.TipoRevaluacion, docId, $"REV-{c.Anio}{c.Mes:00}", null),
                $"{CalculadoraRevaluacionCierre.Modulo}.{CalculadoraRevaluacionCierre.TipoRevaluacion}:{empresaId}:{c.Anio}-{c.Mes:00}:{sucursal}" + (anuladas > 0 ? $":r{anuladas}" : ""),
                partidas, OrigenMovimiento.Automatica, empresaId), ct);
        }
        var polizas = await RevaluacionCierreComun.PolizasDelMesAsync(db, empresaId, c.Anio, c.Mes, ct);
        return RevaluacionCierreComun.Respuesta(c.Anio, c.Mes, polizas, detalles, []);
    }
}

// ---------------------------------------------------------------- Reversa del día 1

/// <summary>
/// Reversa automática: cada póliza de revaluación cuyo día 1 siguiente ya llegó (o hasta <paramref name="Hasta"/>) se revierte con una
/// póliza propia, lados contrarios y fecha del día 1. Idempotente (llave por póliza). La corre el proceso diario y la ejecución del cierre.
/// </summary>
public sealed record PublicarReversasRevaluacionContableCommand(DateOnly? Hasta = null) : IRequest<int>;

public sealed class PublicarReversasRevaluacionContableHandler(ContabilidadDbContext db, IPolizaContablePort puerto, IClock clock)
    : IRequestHandler<PublicarReversasRevaluacionContableCommand, int>
{
    public async Task<int> Handle(PublicarReversasRevaluacionContableCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limite = c.Hasta ?? FechaContable.HoyLocal(clock.UtcNow);
        var revertidas = db.Polizas.IgnoreQueryFilters().Where(r => r.OrigenModulo == CalculadoraRevaluacionCierre.Modulo
            && r.OrigenTipo == CalculadoraRevaluacionCierre.TipoReversa).Select(r => r.OrigenDocumentoId);
        var pendientes = await db.Polizas.IgnoreQueryFilters().AsNoTracking().Include(p => p.Partidas)
            .Where(p => p.OrigenModulo == CalculadoraRevaluacionCierre.Modulo && p.OrigenTipo == CalculadoraRevaluacionCierre.TipoRevaluacion
                && p.Estado != EstadoPoliza.Borrador && p.PolizaAnuladaId == null && p.AnuladaPorPolizaId == null
                && p.Fecha < limite && !revertidas.Contains((Guid?)p.Id))
            .OrderBy(p => p.Fecha).ToListAsync(ct);
        foreach (var p in pendientes)
        {
            var fecha = p.Fecha.AddDays(1);
            await puerto.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica(p.TipoClave, fecha, $"Reversa: {p.Concepto}",
                new DocumentoOrigenPoliza(CalculadoraRevaluacionCierre.Modulo, CalculadoraRevaluacionCierre.TipoReversa, p.Id, p.OrigenFolio, null),
                $"{CalculadoraRevaluacionCierre.Modulo}.{CalculadoraRevaluacionCierre.TipoReversa}:{p.Id}",
                [.. p.Partidas.OrderBy(x => x.Linea).Select(CalculadoraRevaluacionCierre.Contraria)], OrigenMovimiento.Automatica, p.EmpresaId), ct);
        }
        return pendientes.Count;
    }
}

internal static class RevaluacionCierreComun
{
    public static async Task<List<RevaluacionPolizaResponse>> PolizasDelMesAsync(ContabilidadDbContext db, Guid empresaId, int anio, int mes,
        CancellationToken ct)
    {
        var docId = CalculadoraRevaluacionCierre.DocumentoId(empresaId, anio, mes);
        var originales = await db.Polizas.AsNoTracking()
            .Where(p => p.OrigenModulo == CalculadoraRevaluacionCierre.Modulo && p.OrigenTipo == CalculadoraRevaluacionCierre.TipoRevaluacion
                && p.OrigenDocumentoId == docId && p.AnuladaPorPolizaId == null && p.PolizaAnuladaId == null)
            .Select(p => new { p.Id, p.Folio, p.Fecha }).ToListAsync(ct);
        var ids = originales.Select(o => (Guid?)o.Id).ToList();
        var reversas = await db.Polizas.AsNoTracking()
            .Where(p => p.OrigenModulo == CalculadoraRevaluacionCierre.Modulo && p.OrigenTipo == CalculadoraRevaluacionCierre.TipoReversa
                && ids.Contains(p.OrigenDocumentoId))
            .Select(p => new { p.Id, p.Folio, p.Fecha }).ToListAsync(ct);
        var sucursales = await db.PolizasPartidas.AsNoTracking().Where(x => ids.Contains(x.PolizaId) && x.Linea == 1)
            .ToDictionaryAsync(x => x.PolizaId, x => x.SucursalId, ct);
        return [.. originales.OrderBy(o => o.Folio).Select(o => new RevaluacionPolizaResponse(o.Id, o.Folio, o.Fecha, sucursales.GetValueOrDefault(o.Id), false)),
            .. reversas.OrderBy(o => o.Folio).Select(o => new RevaluacionPolizaResponse(o.Id, o.Folio, o.Fecha, null, true))];
    }

    public static RevaluacionCierreContableResponse Respuesta(int anio, int mes, IReadOnlyList<RevaluacionPolizaResponse> polizas,
        IReadOnlyList<RevaluacionCierreDetalle> detalles, IReadOnlyList<string> avisos, IReadOnlyList<RevaluacionPolizaPrevia>? previas = null)
    {
        var cierre = CalculadoraRevaluacionCierre.Cierre(anio, mes);
        return new(anio, mes, cierre, cierre.AddDays(1), polizas.Any(p => !p.EsReversa), polizas, detalles,
            detalles.Where(d => d.Diferencia > 0).Sum(d => d.Diferencia), -detalles.Where(d => d.Diferencia < 0).Sum(d => d.Diferencia), avisos, previas);
    }

    /// <summary>Partidas de la póliza de cada sucursal (la misma lógica para la vista previa y para ejecutar).</summary>
    public static async Task<List<(Guid Sucursal, List<PartidaRequest> Partidas)>> ArmarAsync(ContabilidadDbContext db,
        IReadOnlyList<RevaluacionCierreDetalle> detalles, DateOnly cierre, CancellationToken ct)
    {
        var roles = await db.RolesCuenta.AsNoTracking()
            .Where(r => r.Codigo.StartsWith(CalculadoraRevaluacionCierre.RolPerdida) || r.Codigo.StartsWith(CalculadoraRevaluacionCierre.RolUtilidad))
            .ToListAsync(ct);
        var resultado = new List<(Guid, List<PartidaRequest>)>();
        foreach (var porSucursal in detalles.Where(d => d.Diferencia != 0m).GroupBy(d => d.SucursalId).OrderBy(g => g.Key))
        {
            var sucursal = porSucursal.Key;
            var cuentas = porSucursal.GroupBy(d => d.Control).ToDictionary(g => g.Key, g => (
                CuentaDelRol(roles, CalculadoraRevaluacionCierre.RolResultado(CalculadoraRevaluacionCierre.RolPerdida, g.Key), sucursal, cierre,
                    g.Any(d => d.Diferencia < 0)),
                CuentaDelRol(roles, CalculadoraRevaluacionCierre.RolResultado(CalculadoraRevaluacionCierre.RolUtilidad, g.Key), sucursal, cierre,
                    g.Any(d => d.Diferencia > 0))));
            resultado.Add((sucursal, CalculadoraRevaluacionCierre.Partidas([.. porSucursal], sucursal, cuentas)));
        }
        return resultado;
    }

    /// <summary>M-317: pólizas por sucursal que se registrarían, con código de cuenta; no escribe nada.</summary>
    public static async Task<List<RevaluacionPolizaPrevia>> PreviasAsync(ContabilidadDbContext db, IReadOnlyList<RevaluacionCierreDetalle> detalles,
        DateOnly cierre, int mes, int anio, CancellationToken ct)
    {
        var armadas = await ArmarAsync(db, detalles, cierre, ct);
        var ids = armadas.SelectMany(a => a.Partidas).Select(p => p.CuentaId).Distinct().ToList();
        var codigos = await db.Cuentas.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Codigo, ct);
        return [.. armadas.Select(a => new RevaluacionPolizaPrevia(a.Sucursal, $"Revaluación de bancos y clientes en moneda extranjera {mes:00}/{anio}",
            [.. a.Partidas.Select(p => new RevaluacionPartidaPrevia(codigos.GetValueOrDefault(p.CuentaId, "?"), p.Concepto ?? string.Empty, p.Cargo, p.Abono))],
            a.Partidas.Sum(p => p.Cargo), a.Partidas.Sum(p => p.Abono)))];
    }

    private static Guid CuentaDelRol(List<RolCuenta> roles, string codigo, Guid sucursal, DateOnly fecha, bool necesaria) =>
        GenerarPolizaDesdeEventoHandler.Rol(roles, codigo, sucursal, Poliza.MonedaNacional, fecha)?.CuentaId
        ?? (necesaria
            ? throw new BusinessRuleException("REVALUACION_ROL_SIN_CUENTA",
                $"El rol {codigo} no tiene cuenta asignada: Contabilidad debe mapearlo en Motor › Roles de cuenta antes de revaluar.")
            : Guid.Empty);
}
