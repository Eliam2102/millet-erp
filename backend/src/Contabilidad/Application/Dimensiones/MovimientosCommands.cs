using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Dimensiones;

public sealed record MovimientoRequest(
    Guid CuentaId, Guid TipoDocumentoId, DateOnly FechaContable, Guid SucursalId,
    Guid? Dim1Id, Guid? Dim2Id, Guid? Dim3Id, OrigenMovimiento Origen = OrigenMovimiento.Manual, string? Referencia = null,
    string? Proyecto = null, Guid? ClienteId = null, Guid? ProveedorId = null, Guid? CuentaBancariaId = null)
{
    public MovimientoDimensionado Movimiento => new(CuentaId, TipoDocumentoId, FechaContable, SucursalId, Dim1Id, Dim2Id, Dim3Id, Origen,
        string.IsNullOrWhiteSpace(Proyecto) ? null : Proyecto.Trim(), ClienteId, ProveedorId, CuentaBancariaId);
}

public sealed class MovimientoRequestValidator : AbstractValidator<MovimientoRequest>
{
    public MovimientoRequestValidator()
    {
        RuleFor(m => m.CuentaId).NotEqual(Guid.Empty).WithMessage("Elija la cuenta contable.");
        RuleFor(m => m.TipoDocumentoId).NotEqual(Guid.Empty).WithMessage("Elija el tipo de documento.");
        RuleFor(m => m.SucursalId).NotEqual(Guid.Empty).WithMessage("Elija la sucursal.");
        RuleFor(m => m.FechaContable).NotEqual(default(DateOnly)).WithMessage("Indique la fecha contable.");
        RuleFor(m => m.Referencia).MaximumLength(100);
        RuleFor(m => m.Proyecto).MaximumLength(40);
    }
}

/// <summary>
/// Valida sin guardar (panel «Probar movimiento»). El usuario debe poder operar la sucursal (403 si no). Un periodo que no admite
/// movimientos (F1-CON-03) se muestra como un error más, en el campo <c>fechaContable</c>.
/// </summary>
public sealed record ValidarMovimientoDimensionesQuery(MovimientoRequest Movimiento) : IRequest<ValidacionDimensiones>;

public sealed class ValidarMovimientoDimensionesValidator : AbstractValidator<ValidarMovimientoDimensionesQuery>
{
    public ValidarMovimientoDimensionesValidator() => RuleFor(q => q.Movimiento).SetValidator(new MovimientoRequestValidator());
}

public sealed class ValidarMovimientoDimensionesHandler(
    ValidadorDimensiones validador, AlcanceSucursalContable alcance, VerificadorPeriodoContable periodo)
    : IRequestHandler<ValidarMovimientoDimensionesQuery, ValidacionDimensiones>
{
    public async Task<ValidacionDimensiones> Handle(ValidarMovimientoDimensionesQuery request, CancellationToken cancellationToken)
    {
        var m = request.Movimiento;
        await alcance.VerificarAsync(m.SucursalId, cancellationToken);
        var v = await validador.ValidarAsync(m.Movimiento, cancellationToken);
        return await periodo.RechazoAsync(m.FechaContable, m.Origen, cancellationToken) is { } rechazo
            ? v with { Valido = false, Errores = [.. v.Errores, new ErrorDimension(rechazo.Code, rechazo.Message, "fechaContable")] }
            : v;
    }
}

// ─── Movimientos de prueba (D7) ──────────────────────────────────────────────

public sealed record CentroRefDto(Guid Id, string Clave, string Nombre, bool Activo);

public sealed record MovimientoPruebaResponse(
    Guid Id, Guid SucursalId, string? SucursalNombre, Guid CuentaId, string CuentaCodigo, Guid TipoDocumentoId, string TipoDocumentoClave,
    DateOnly FechaContable, CentroRefDto? Dim1, CentroRefDto? Dim2, CentroRefDto? Dim3, string? Referencia,
    IReadOnlyList<RequerimientoEfectivo> ReglasAplicadas, DateTimeOffset ConfirmadoEn, string? ConfirmadoPor,
    string? Proyecto = null, AuxiliarContable? Cliente = null, AuxiliarContable? Proveedor = null, AuxiliarContable? CuentaBancaria = null);

public sealed record ConfirmacionMovimientoResult(bool Confirmado, ValidacionDimensiones Validacion, MovimientoPruebaResponse? Movimiento);

/// <summary>
/// Valida y, si pasa, guarda el movimiento de PRUEBA con el snapshot de las reglas que lo validaron. Si no pasa, no escribe y
/// devuelve los errores (el endpoint responde 422). No marca la cuenta como usada (evita bloquear cuentas reales, P20).
/// Antes de validar, el periodo de la fecha contable debe admitir movimientos (F1-CON-03): si no, 422 <c>CONTAB_PERIODO_*</c>.
/// </summary>
// PLATFORM-TODO(<Polizas>): la póliza real sustituye a este comando al confirmar sus partidas.
public sealed record ConfirmarMovimientoPruebaCommand(MovimientoRequest Movimiento) : IRequest<ConfirmacionMovimientoResult>;

public sealed class ConfirmarMovimientoPruebaValidator : AbstractValidator<ConfirmarMovimientoPruebaCommand>
{
    public ConfirmarMovimientoPruebaValidator() => RuleFor(q => q.Movimiento).SetValidator(new MovimientoRequestValidator());
}

public sealed class ConfirmarMovimientoPruebaHandler(
    ContabilidadDbContext db, ValidadorDimensiones validador, AlcanceSucursalContable alcance, IClock clock, LecturaMovimientos lectura,
    VerificadorPeriodoContable periodo)
    : IRequestHandler<ConfirmarMovimientoPruebaCommand, ConfirmacionMovimientoResult>
{
    public const string ConsumidorMovimientoPrueba = "MOVIMIENTO_PRUEBA";

    public async Task<ConfirmacionMovimientoResult> Handle(ConfirmarMovimientoPruebaCommand request, CancellationToken cancellationToken)
    {
        var m = request.Movimiento;
        await alcance.VerificarAsync(m.SucursalId, cancellationToken);
        ConfirmacionMovimientoResult? resultado = null;
        MovimientoDimensionPrueba? mov = null;
        // Periodos antes que reglas: la lectura del estado y el guardado comparten la transacción de los cierres.
        // Se adquiere el segundo candado en la misma transacción, sin abrir una transacción anidada.
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaPeriodos.LockPeriodos, async ct =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({PoliticaReglas.LockReglas})", ct);
            await periodo.LanzarSiNoAdmiteAsync(m.FechaContable, m.Origen, ct);
            var v = await validador.ValidarAsync(m.Movimiento, ct);
            if (!v.Valido)
            {
                resultado = new(false, v, null);
                return;
            }

            var cuenta = await db.Cuentas.AsNoTracking().FirstAsync(c => c.Id == m.CuentaId, ct);
            var tipo = await db.TiposDocumento.AsNoTracking().FirstAsync(t => t.Id == m.TipoDocumentoId, ct);
            mov = new MovimientoDimensionPrueba(Guid.CreateVersion7(), m.SucursalId, cuenta.Id, cuenta.Codigo, tipo.Id, tipo.Clave,
                m.FechaContable, v.Centros.Dim1Id, v.Centros.Dim2Id, v.Centros.Dim3Id, m.Movimiento.Proyecto, m.ClienteId, m.ProveedorId,
                m.CuentaBancariaId, string.IsNullOrWhiteSpace(m.Referencia) ? null : m.Referencia.Trim(),
                JsonSerializer.Serialize(v.Requerimientos, LecturaMovimientos.Json), clock.UtcNow);
            db.MovimientosPrueba.Add(mov);
            // Los usos se guardan con el movimiento bajo el mismo candado que las escrituras de reglas.
            foreach (var reglaId in v.Requerimientos.Where(r => r.ReglaId is not null).Select(r => r.ReglaId!.Value).Distinct())
                db.ReglasDimensionUso.Add(new ReglaDimensionUso(Guid.CreateVersion7(), reglaId, ConsumidorMovimientoPrueba, mov.Id.ToString(), m.FechaContable));
            await db.SaveChangesAsync(ct);
            resultado = new(true, v, null);
        }, cancellationToken);
        return resultado!.Confirmado
            ? resultado with { Movimiento = (await lectura.ResponsesAsync([mov!], cancellationToken))[0] }
            : resultado;
    }
}

/// <summary>Proyección de movimientos de prueba: resuelve sucursal y centros para mostrar (incluye centros dados de baja).</summary>
public sealed class LecturaMovimientos(
    ICentroCostoContabilidadPort centros, ISucursalContabilidadPort sucursales, ITerceroContabilidadPort terceros, ICuentaBancariaContabilidadPort bancos)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<MovimientoPruebaResponse>> ResponsesAsync(IReadOnlyList<MovimientoDimensionPrueba> movs, CancellationToken ct)
    {
        var ids = movs.SelectMany(m => new[] { m.Dim1Id, m.Dim2Id, m.Dim3Id }).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var nodos = ids.Count == 0 ? new Dictionary<Guid, CentroCostoNodo>() : await centros.ObtenerAsync(ids, ct);
        var suc = (await sucursales.ListarAsync(ct)).ToDictionary(s => s.Id);
        CentroRefDto? Ref(Guid? id) => id is { } x && nodos.TryGetValue(x, out var n) ? new(n.Id, n.Clave, n.Nombre, n.ActivoEnCadena) : null;
        static List<Guid> Ids(IEnumerable<Guid?> xs) => [.. xs.Where(x => x is not null).Select(x => x!.Value).Distinct()];
        var clientes = await terceros.ObtenerAsync(TipoAuxiliar.Cliente, Ids(movs.Select(m => m.ClienteId)), ct);
        var proveedores = await terceros.ObtenerAsync(TipoAuxiliar.Proveedor, Ids(movs.Select(m => m.ProveedorId)), ct);
        var cuentas = await bancos.ObtenerAsync(Ids(movs.Select(m => m.CuentaBancariaId)), ct);
        static AuxiliarContable? Aux(IReadOnlyDictionary<Guid, AuxiliarContable> d, Guid? id) => id is { } x ? d.GetValueOrDefault(x) : null;
        return [.. movs.Select(m => new MovimientoPruebaResponse(m.Id, m.SucursalId, suc.GetValueOrDefault(m.SucursalId)?.Nombre,
            m.CuentaId, m.CuentaCodigo, m.TipoDocumentoId, m.TipoDocumentoClave, m.FechaContable, Ref(m.Dim1Id), Ref(m.Dim2Id), Ref(m.Dim3Id),
            m.Referencia, JsonSerializer.Deserialize<List<RequerimientoEfectivo>>(m.ReglasAplicadas, Json) ?? [], m.ConfirmadoEn, m.CreatedBy,
            m.Proyecto, Aux(clientes, m.ClienteId), Aux(proveedores, m.ProveedorId), Aux(cuentas, m.CuentaBancariaId)))];
    }
}

/// <summary>Lista solo movimientos de las sucursales que el usuario puede ver (todas con el permiso de bypass).</summary>
public sealed record ListarMovimientosPruebaQuery(Guid? SucursalId, Guid? CuentaId, int Offset, int Limit) : IRequest<PagedResponse<MovimientoPruebaResponse>>;

public sealed class ListarMovimientosPruebaHandler(ContabilidadDbContext db, AlcanceSucursalContable alcance, LecturaMovimientos lectura)
    : IRequestHandler<ListarMovimientosPruebaQuery, PagedResponse<MovimientoPruebaResponse>>
{
    public async Task<PagedResponse<MovimientoPruebaResponse>> Handle(ListarMovimientosPruebaQuery request, CancellationToken cancellationToken)
    {
        var q = db.MovimientosPrueba.AsNoTracking();
        if (request.SucursalId is { } s)
        {
            await alcance.VerificarAsync(s, cancellationToken);
            q = q.Where(m => m.SucursalId == s);
        }
        else if (!await alcance.TodasAsync(cancellationToken))
        {
            var visibles = (await alcance.DisponiblesAsync(cancellationToken)).Select(x => x.Id).ToList();
            q = q.Where(m => visibles.Contains(m.SucursalId));
        }
        if (request.CuentaId is { } c) q = q.Where(m => m.CuentaId == c);
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(m => m.ConfirmadoEn).Skip(request.Offset).Take(request.Limit).ToListAsync(cancellationToken);
        return new PagedResponse<MovimientoPruebaResponse>(await lectura.ResponsesAsync(items, cancellationToken), total, request.Offset, request.Limit);
    }
}

public sealed record ObtenerMovimientoPruebaQuery(Guid Id) : IRequest<MovimientoPruebaResponse>;

public sealed class ObtenerMovimientoPruebaHandler(ContabilidadDbContext db, AlcanceSucursalContable alcance, LecturaMovimientos lectura)
    : IRequestHandler<ObtenerMovimientoPruebaQuery, MovimientoPruebaResponse>
{
    public async Task<MovimientoPruebaResponse> Handle(ObtenerMovimientoPruebaQuery request, CancellationToken cancellationToken)
    {
        var m = await db.MovimientosPrueba.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_MOVIMIENTO_NO_ENCONTRADO", $"No existe el movimiento '{request.Id}'.");
        await alcance.VerificarAsync(m.SucursalId, cancellationToken);
        return (await lectura.ResponsesAsync([m], cancellationToken))[0];
    }
}
