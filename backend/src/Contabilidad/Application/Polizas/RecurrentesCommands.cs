using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Polizas;

// ─── DTOs ────────────────────────────────────────────────────────────────────

public sealed record PlantillaRecurrenteRequest(
    string Nombre, Guid TipoOperacionId, string Concepto, int AnioInicio, int MesInicio, int NumeroPeriodos, int DiaDelMes,
    bool ReversaAutomatica, bool NaceAutorizada, IReadOnlyList<PartidaRequest> Partidas);

/// <summary>Partida fija de la plantilla con los datos para mostrarla (ids para editar + código y nombre resueltos).</summary>
public sealed record PartidaPlantillaResponse(
    Guid CuentaId, string? CuentaCodigo, string? CuentaNombre, string? Concepto, decimal Cargo, decimal Abono, Guid SucursalId, string? SucursalNombre,
    Guid? Dim1Id, Guid? Dim2Id, Guid? Dim3Id, CentroRefDto? Dim1, CentroRefDto? Dim2, CentroRefDto? Dim3, string? Proyecto,
    Guid? ClienteId, Guid? ProveedorId, Guid? CuentaBancariaId, Ports.AuxiliarContable? Cliente, Ports.AuxiliarContable? Proveedor,
    Ports.AuxiliarContable? CuentaBancaria);

public sealed record AplicacionRecurrenteResponse(
    int Anio, int Mes, DateOnly Fecha, Guid? PolizaId, string? Folio, EstadoPoliza? Estado,
    DateOnly? FechaReversa, Guid? ReversaId, string? ReversaFolio);

public sealed record PlantillaRecurrenteResponse(
    Guid Id, string Nombre, Guid TipoOperacionId, string TipoClave, string Concepto, int AnioInicio, int MesInicio, int NumeroPeriodos,
    int DiaDelMes, bool ReversaAutomatica, bool NaceAutorizada, EstadoPlantillaRecurrente Estado, decimal Importe,
    IReadOnlyList<PartidaPlantillaResponse> Partidas, Guid? CreadaPorId, string CreadaPor, DateTimeOffset CreadaEn, Guid? ActivadaPorId,
    string? ActivadaPor, DateTimeOffset? ActivadaEn, int Generadas, IReadOnlyList<AplicacionRecurrenteResponse> Aplicaciones, int Version);

public sealed record PlantillaRecurrenteResumenResponse(
    Guid Id, string Nombre, string TipoClave, int AnioInicio, int MesInicio, int NumeroPeriodos, bool ReversaAutomatica,
    bool NaceAutorizada, EstadoPlantillaRecurrente Estado, decimal Importe, int Generadas, string CreadaPor, int Version);

public sealed record AplicacionGenerada(Guid PlantillaId, string Plantilla, int Anio, int Mes, Guid PolizaId, string? Folio, EstadoPoliza Estado, bool EsReversa);

public sealed record AplicacionPendiente(Guid PlantillaId, string Plantilla, int Anio, int Mes, bool EsReversa, string Codigo, string Motivo);

public sealed record ResultadoGeneracionRecurrentes(IReadOnlyList<AplicacionGenerada> Generadas, IReadOnlyList<AplicacionPendiente> Pendientes);

// ─── Generador ───────────────────────────────────────────────────────────────

/// <summary>
/// Genera lo que toca de cada plantilla activa hasta <c>hoy</c> (CA10.6, K10.4-b): la póliza de cada periodo cuyo día ya llegó y, si
/// la plantilla tiene reversa automática, la reversa en negativo el día 1 del periodo siguiente. Idempotente: la llave
/// (plantilla + periodo, o «reversa:» + póliza) es única, así que correrlo dos veces o en paralelo no duplica. Lo que no se puede
/// generar (periodo no abierto, cuenta inválida) queda como pendiente con su motivo y se reintenta en la siguiente corrida. Corre con
/// la empresa de cada plantilla, también desde el proceso de fondo (sin request).
/// </summary>
public sealed class GeneradorRecurrentes(ContabilidadDbContext db, RegistroPolizas registro, IClock clock)
{
    public const string UsuarioGenerador = "generador-recurrentes";

    public async Task<ResultadoGeneracionRecurrentes> GenerarAsync(DateOnly hoy, Guid? plantillaId, CancellationToken ct)
    {
        var generadas = new List<AplicacionGenerada>();
        var pendientes = new List<AplicacionPendiente>();
        var plantillas = await db.PlantillasRecurrentes.AsNoTracking()
            .Where(p => p.Estado == EstadoPlantillaRecurrente.Activa && (plantillaId == null || p.Id == plantillaId))
            .OrderBy(p => p.CreatedAt).ToListAsync(ct);
        foreach (var p in plantillas)
        {
            var llaves = await db.Polizas.AsNoTracking().Where(x => x.PlantillaRecurrenteId == p.Id && x.LlaveIdempotencia != null)
                .Select(x => x.LlaveIdempotencia!).ToListAsync(ct);
            for (var k = 0; k < p.NumeroPeriodos; k++)
            {
                var (anio, mes) = p.Periodo(k);
                var fecha = p.Fecha(anio, mes);
                if (fecha > hoy) break;
                if (llaves.Contains(PlantillaRecurrente.Llave(p.Id, anio, mes))) continue;
                await IntentarAsync(p, anio, mes, false, generadas, pendientes, () => GenerarPolizaAsync(p, anio, mes, fecha, ct));
            }
            if (p.ReversaAutomatica) await RevertirVencidasAsync(p, hoy, generadas, pendientes, ct);
            await TerminarSiCompletaAsync(p, ct);
        }
        return new(generadas, pendientes);
    }

    private async Task IntentarAsync(PlantillaRecurrente p, int anio, int mes, bool esReversa, List<AplicacionGenerada> generadas,
        List<AplicacionPendiente> pendientes, Func<Task<Poliza?>> accion)
    {
        try
        {
            if (await accion() is { } poliza)
                generadas.Add(new(p.Id, p.Nombre, anio, mes, poliza.Id, poliza.Folio, poliza.Estado, esReversa));
        }
        catch (Exception e) when (DomainExceptionAdapter.Traducir(e) is { } r)
        {
            db.ChangeTracker.Clear();
            pendientes.Add(new(p.Id, p.Nombre, anio, mes, esReversa, r.Codigo, r.Mensaje));
        }
    }

    private async Task<Poliza?> GenerarPolizaAsync(PlantillaRecurrente p, int anio, int mes, DateOnly fecha, CancellationToken ct)
    {
        Poliza? poliza = null;
        await registro.EnTransaccionAsync(async t =>
        {
            var tipo = await db.TiposDocumento.AsNoTracking().FirstAsync(x => x.Id == p.TipoOperacionId, t);
            await registro.VerificarPeriodoAsync(anio, mes, OrigenPoliza.Manual, t, p.EmpresaId);
            var partidas = await registro.ValidarPartidasAsync([.. p.Partidas.Select(Request)], tipo.Id, fecha, OrigenMovimiento.Manual, t);
            poliza = Poliza.CrearManual(Guid.CreateVersion7(), tipo, fecha, anio, mes, $"{p.Concepto} ({PeriodoContable.Nombre(mes)} {anio})", null,
                partidas.Partidas, p.CreadaPorId, p.CreadaPor);
            poliza.EmpresaId = p.EmpresaId;
            foreach (var x in poliza.Partidas) x.EmpresaId = p.EmpresaId;
            poliza.MarcarRecurrente(p.Id, PlantillaRecurrente.Llave(p.Id, anio, mes));
            if (p.NaceAutorizada)
            {
                var numero = await registro.ReservarNumeroAsync(tipo.Id, anio, t, p.EmpresaId);
                poliza.Autorizar(numero, p.ActivadaPorId, p.ActivadaPor ?? UsuarioGenerador, clock.UtcNow);
            }
            db.Polizas.Add(poliza);
            await db.SaveChangesAsync(t);
            if (p.NaceAutorizada) await registro.RegistrarUsosAsync(poliza, partidas.ReglasAplicadas, t);
        }, ct);
        return poliza;
    }

    /// <summary>K10.4-b: reversa en negativo, el día 1 del periodo siguiente, de cada póliza autorizada de la plantilla que no la tenga.</summary>
    private async Task RevertirVencidasAsync(PlantillaRecurrente p, DateOnly hoy, List<AplicacionGenerada> generadas,
        List<AplicacionPendiente> pendientes, CancellationToken ct)
    {
        var revertidas = await db.Polizas.AsNoTracking()
            .Where(x => x.PlantillaRecurrenteId == p.Id && x.Relacion == RelacionPoliza.Reversa).Select(x => x.PolizaRelacionadaId).ToListAsync(ct);
        var originales = await db.Polizas.AsNoTracking()
            .Where(x => x.PlantillaRecurrenteId == p.Id && x.Relacion == null && x.Estado == EstadoPoliza.Autorizada && !revertidas.Contains(x.Id))
            .Select(x => new { x.Id, x.PeriodoAnio, x.PeriodoNumero }).ToListAsync(ct);
        foreach (var o in originales)
        {
            var fecha = PlantillaRecurrente.FechaReversa(o.PeriodoAnio, o.PeriodoNumero);
            if (fecha > hoy) continue;
            await IntentarAsync(p, fecha.Year, fecha.Month, true, generadas, pendientes, () => RevertirAsync(p, o.Id, fecha, ct));
        }
    }

    private async Task<Poliza?> RevertirAsync(PlantillaRecurrente p, Guid originalId, DateOnly fecha, CancellationToken ct)
    {
        Poliza? reversa = null;
        await registro.EnTransaccionAsync(async t =>
        {
            var original = await db.Polizas.Include(x => x.Partidas).FirstAsync(x => x.Id == originalId, t);
            var tipo = await db.TiposDocumento.AsNoTracking().FirstAsync(x => x.Id == original.TipoOperacionId, t);
            await registro.VerificarPeriodoAsync(fecha.Year, fecha.Month, original.Origen, t, p.EmpresaId);
            var numero = await registro.ReservarNumeroAsync(tipo.Id, fecha.Year, t, p.EmpresaId);
            reversa = original.Revertir(Guid.CreateVersion7(), tipo, fecha, fecha.Year, fecha.Month, numero, p.ActivadaPorId,
                p.ActivadaPor ?? UsuarioGenerador, clock.UtcNow);
            db.Polizas.Add(reversa);
            await db.SaveChangesAsync(t);
        }, ct);
        return reversa;
    }

    private async Task TerminarSiCompletaAsync(PlantillaRecurrente p, CancellationToken ct)
    {
        var polizas = await db.Polizas.AsNoTracking().Where(x => x.PlantillaRecurrenteId == p.Id)
            .Select(x => new { x.Id, x.Relacion, x.Estado, x.PolizaRelacionadaId }).ToListAsync(ct);
        var originales = polizas.Where(x => x.Relacion == null).ToList();
        if (originales.Count < p.NumeroPeriodos) return;
        if (p.ReversaAutomatica)
        {
            var revertidas = polizas.Where(x => x.Relacion == RelacionPoliza.Reversa).Select(x => x.PolizaRelacionadaId).ToHashSet();
            if (originales.Any(o => o.Estado == EstadoPoliza.Borrador || o.Estado == EstadoPoliza.Autorizada && !revertidas.Contains(o.Id))) return;
        }
        var plantilla = await db.PlantillasRecurrentes.FirstAsync(x => x.Id == p.Id, ct);
        plantilla.Terminar();
        await db.SaveChangesAsync(ct);
    }

    internal static PartidaRequest Request(PartidaPlantilla x) => new(x.CuentaId, x.Concepto, x.Cargo, x.Abono, x.SucursalId,
        x.Dim1Id, x.Dim2Id, x.Dim3Id, x.Proyecto, x.ClienteId, x.ProveedorId, x.CuentaBancariaId);

    internal static PartidaPlantilla Partida(PartidaRequest x) => new()
    {
        CuentaId = x.CuentaId, Concepto = string.IsNullOrWhiteSpace(x.Concepto) ? null : x.Concepto.Trim(), Cargo = x.Cargo, Abono = x.Abono,
        SucursalId = x.SucursalId, Dim1Id = x.Dim1Id, Dim2Id = x.Dim2Id, Dim3Id = x.Dim3Id,
        Proyecto = string.IsNullOrWhiteSpace(x.Proyecto) ? null : x.Proyecto.Trim(), ClienteId = x.ClienteId, ProveedorId = x.ProveedorId,
        CuentaBancariaId = x.CuentaBancariaId,
    };
}

/// <summary>Convierte los rechazos de negocio, validación y llave duplicada en un pendiente con código y mensaje.</summary>
internal static class DomainExceptionAdapter
{
    public sealed record Rechazo(string Codigo, string Mensaje);

    public static Rechazo? Traducir(Exception e) => e switch
    {
        BusinessRuleException b => new(b.Code, b.Message),
        Millet.SharedKernel.Application.Exceptions.ValidationException v => new(v.Code, string.Join(" ", v.Errors.Select(x => x.Message))),
        DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } } =>
            new("CONTAB_RECURRENTE_YA_GENERADA", "Otra corrida ya generó esta aplicación."),
        _ => null,
    };
}

// ─── Plantillas: comandos ────────────────────────────────────────────────────

public sealed class PlantillaRecurrenteRequestValidator : AbstractValidator<PlantillaRecurrenteRequest>
{
    public PlantillaRecurrenteRequestValidator()
    {
        RuleFor(p => p.Nombre).NotEmpty().MaximumLength(120);
        RuleFor(p => p.TipoOperacionId).NotEqual(Guid.Empty).WithMessage("Elija el tipo de operación.");
        RuleFor(p => p.Concepto).NotEmpty().MaximumLength(Poliza.ConceptoMaximo);
        RuleFor(p => p.MesInicio).InclusiveBetween(1, 12);
        RuleFor(p => p.AnioInicio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo);
        RuleFor(p => p.NumeroPeriodos).InclusiveBetween(1, PlantillaRecurrente.PeriodosMaximo);
        RuleFor(p => p.DiaDelMes).InclusiveBetween(PlantillaRecurrente.UltimoDiaDelMes, 28);
        RuleFor(p => p.Partidas).NotNull().Must(p => p.Count is >= 2 and <= 200).WithMessage("La plantilla lleva de 2 a 200 partidas.");
        RuleForEach(p => p.Partidas).SetValidator(new PartidaRequestValidator());
    }
}

public sealed record CrearPlantillaRecurrenteCommand(PlantillaRecurrenteRequest Plantilla) : IRequest<PlantillaRecurrenteResponse>;

public sealed class CrearPlantillaRecurrenteValidator : AbstractValidator<CrearPlantillaRecurrenteCommand>
{
    public CrearPlantillaRecurrenteValidator() => RuleFor(c => c.Plantilla).SetValidator(new PlantillaRecurrenteRequestValidator());
}

/// <summary>
/// Prepara la plantilla en borrador. Valida las partidas contra el catálogo y las reglas de dimensión con la fecha del primer
/// periodo (las siguientes se vuelven a validar al generar cada póliza).
/// </summary>
public sealed class CrearPlantillaRecurrenteHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, ICurrentUserContext usuario, LecturaRecurrentes lectura)
    : IRequestHandler<CrearPlantillaRecurrenteCommand, PlantillaRecurrenteResponse>
{
    public async Task<PlantillaRecurrenteResponse> Handle(CrearPlantillaRecurrenteCommand request, CancellationToken cancellationToken)
    {
        var r = request.Plantilla;
        foreach (var s in r.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, cancellationToken);
        var tipo = await registro.TipoAsync(r.TipoOperacionId, cancellationToken);
        await PoliticaRecurrentes.ValidarPartidasAsync(registro, r, tipo.Id, cancellationToken);
        var plantilla = new PlantillaRecurrente(Guid.CreateVersion7(), r.Nombre, tipo, r.Concepto, r.AnioInicio, r.MesInicio, r.NumeroPeriodos,
            r.DiaDelMes, r.ReversaAutomatica, r.NaceAutorizada, r.Partidas.Select(GeneradorRecurrentes.Partida), usuario.UserId,
            PoliticaPolizas.Usuario(usuario));
        db.PlantillasRecurrentes.Add(plantilla);
        await db.SaveChangesAsync(cancellationToken);
        return await lectura.ObtenerAsync(plantilla.Id, cancellationToken);
    }
}

public sealed record EditarPlantillaRecurrenteCommand(Guid Id, int VersionEsperada, PlantillaRecurrenteRequest Plantilla) : IRequest<PlantillaRecurrenteResponse>;

public sealed class EditarPlantillaRecurrenteValidator : AbstractValidator<EditarPlantillaRecurrenteCommand>
{
    public EditarPlantillaRecurrenteValidator() => RuleFor(c => c.Plantilla).SetValidator(new PlantillaRecurrenteRequestValidator());
}

public sealed class EditarPlantillaRecurrenteHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, LecturaRecurrentes lectura)
    : IRequestHandler<EditarPlantillaRecurrenteCommand, PlantillaRecurrenteResponse>
{
    public async Task<PlantillaRecurrenteResponse> Handle(EditarPlantillaRecurrenteCommand request, CancellationToken cancellationToken)
    {
        var r = request.Plantilla;
        foreach (var s in r.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, cancellationToken);
        var plantilla = await PoliticaRecurrentes.CargarAsync(db, request.Id, request.VersionEsperada, cancellationToken);
        var tipo = await registro.TipoAsync(r.TipoOperacionId, cancellationToken);
        await PoliticaRecurrentes.ValidarPartidasAsync(registro, r, tipo.Id, cancellationToken);
        plantilla.Editar(r.Nombre, tipo, r.Concepto, r.AnioInicio, r.MesInicio, r.NumeroPeriodos, r.DiaDelMes, r.ReversaAutomatica,
            r.NaceAutorizada, r.Partidas.Select(GeneradorRecurrentes.Partida));
        await db.SaveChangesAsync(cancellationToken);
        return await lectura.ObtenerAsync(plantilla.Id, cancellationToken);
    }
}

public enum AccionPlantillaRecurrente { Activar, Suspender, Reanudar }

/// <summary>Activar (otra persona con permiso, R23), suspender o reanudar. Al activar se genera de inmediato lo que ya toca.</summary>
public sealed record CambiarEstadoPlantillaRecurrenteCommand(Guid Id, int VersionEsperada, AccionPlantillaRecurrente Accion)
    : IRequest<PlantillaRecurrenteResponse>;

public sealed class CambiarEstadoPlantillaRecurrenteHandler(
    ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock, GeneradorRecurrentes generador, LecturaRecurrentes lectura)
    : IRequestHandler<CambiarEstadoPlantillaRecurrenteCommand, PlantillaRecurrenteResponse>
{
    public async Task<PlantillaRecurrenteResponse> Handle(CambiarEstadoPlantillaRecurrenteCommand request, CancellationToken cancellationToken)
    {
        var plantilla = await PoliticaRecurrentes.CargarAsync(db, request.Id, request.VersionEsperada, cancellationToken);
        switch (request.Accion)
        {
            case AccionPlantillaRecurrente.Activar: plantilla.Activar(usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow); break;
            case AccionPlantillaRecurrente.Suspender: plantilla.Suspender(); break;
            case AccionPlantillaRecurrente.Reanudar: plantilla.Reanudar(); break;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (request.Accion is AccionPlantillaRecurrente.Activar or AccionPlantillaRecurrente.Reanudar)
            await generador.GenerarAsync(FechaContable.HoyLocal(clock.UtcNow), plantilla.Id, cancellationToken);
        return await lectura.ObtenerAsync(plantilla.Id, cancellationToken);
    }
}

/// <summary>«Generar pendientes»: corre el generador ahora (lo mismo que hace el proceso de fondo cada hora).</summary>
public sealed record GenerarRecurrentesCommand(Guid? PlantillaId) : IRequest<ResultadoGeneracionRecurrentes>;

public sealed class GenerarRecurrentesHandler(GeneradorRecurrentes generador, IClock clock) : IRequestHandler<GenerarRecurrentesCommand, ResultadoGeneracionRecurrentes>
{
    public Task<ResultadoGeneracionRecurrentes> Handle(GenerarRecurrentesCommand request, CancellationToken cancellationToken) =>
        generador.GenerarAsync(FechaContable.HoyLocal(clock.UtcNow), request.PlantillaId, cancellationToken);
}

// ─── Plantillas: consultas ───────────────────────────────────────────────────

public sealed record ListarPlantillasRecurrentesQuery(EstadoPlantillaRecurrente? Estado) : IRequest<IReadOnlyList<PlantillaRecurrenteResumenResponse>>;

public sealed class ListarPlantillasRecurrentesHandler(ContabilidadDbContext db)
    : IRequestHandler<ListarPlantillasRecurrentesQuery, IReadOnlyList<PlantillaRecurrenteResumenResponse>>
{
    public async Task<IReadOnlyList<PlantillaRecurrenteResumenResponse>> Handle(ListarPlantillasRecurrentesQuery request, CancellationToken cancellationToken)
    {
        var q = db.PlantillasRecurrentes.AsNoTracking();
        if (request.Estado is { } e) q = q.Where(p => p.Estado == e);
        var plantillas = await q.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        var ids = plantillas.Select(p => p.Id).ToList();
        var generadas = await db.Polizas.AsNoTracking().Where(x => x.PlantillaRecurrenteId != null && ids.Contains(x.PlantillaRecurrenteId.Value) && x.Relacion == null)
            .GroupBy(x => x.PlantillaRecurrenteId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, cancellationToken);
        var tipos = await db.TiposDocumento.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Clave, cancellationToken);
        return [.. plantillas.Select(p => new PlantillaRecurrenteResumenResponse(p.Id, p.Nombre, tipos.GetValueOrDefault(p.TipoOperacionId, "?"),
            p.AnioInicio, p.MesInicio, p.NumeroPeriodos, p.ReversaAutomatica, p.NaceAutorizada, p.Estado, p.TotalCargos,
            generadas.GetValueOrDefault(p.Id), p.CreadaPor, p.Version))];
    }
}

public sealed record ObtenerPlantillaRecurrenteQuery(Guid Id) : IRequest<PlantillaRecurrenteResponse>;

public sealed class ObtenerPlantillaRecurrenteHandler(LecturaRecurrentes lectura) : IRequestHandler<ObtenerPlantillaRecurrenteQuery, PlantillaRecurrenteResponse>
{
    public Task<PlantillaRecurrenteResponse> Handle(ObtenerPlantillaRecurrenteQuery request, CancellationToken cancellationToken) =>
        lectura.ObtenerAsync(request.Id, cancellationToken);
}

/// <summary>Plantilla con su calendario: por cada periodo, la póliza generada y su reversa (si aplica).</summary>
public sealed class LecturaRecurrentes(
    ContabilidadDbContext db, Ports.ICentroCostoContabilidadPort centros, Ports.ISucursalContabilidadPort sucursales,
    Ports.ITerceroContabilidadPort terceros, Ports.ICuentaBancariaContabilidadPort bancos)
{
    private async Task<IReadOnlyList<PartidaPlantillaResponse>> PartidasAsync(IReadOnlyList<PartidaPlantilla> ps, CancellationToken ct)
    {
        var cuentaIds = ps.Select(x => x.CuentaId).Distinct().ToList();
        var cuentas = await db.Cuentas.AsNoTracking().Where(c => cuentaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => new { c.Codigo, c.Nombre }, ct);
        var dimIds = ps.SelectMany(x => new[] { x.Dim1Id, x.Dim2Id, x.Dim3Id }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var nodos = dimIds.Count == 0 ? new Dictionary<Guid, Ports.CentroCostoNodo>() : await centros.ObtenerAsync(dimIds, ct);
        var suc = (await sucursales.ListarAsync(ct)).ToDictionary(x => x.Id);
        static List<Guid> Ids(IEnumerable<Guid?> xs) => [.. xs.Where(x => x is not null).Select(x => x!.Value).Distinct()];
        var clientes = await terceros.ObtenerAsync(Ports.TipoAuxiliar.Cliente, Ids(ps.Select(x => x.ClienteId)), ct);
        var proveedores = await terceros.ObtenerAsync(Ports.TipoAuxiliar.Proveedor, Ids(ps.Select(x => x.ProveedorId)), ct);
        var cuentasBanco = await bancos.ObtenerAsync(Ids(ps.Select(x => x.CuentaBancariaId)), ct);
        CentroRefDto? Ref(Guid? id) => id is { } x && nodos.TryGetValue(x, out var n) ? new(n.Id, n.Clave, n.Nombre, n.ActivoEnCadena) : null;
        static Ports.AuxiliarContable? Aux(IReadOnlyDictionary<Guid, Ports.AuxiliarContable> d, Guid? id) => id is { } x ? d.GetValueOrDefault(x) : null;
        return [.. ps.Select(x => new PartidaPlantillaResponse(x.CuentaId, cuentas.GetValueOrDefault(x.CuentaId)?.Codigo, cuentas.GetValueOrDefault(x.CuentaId)?.Nombre,
            x.Concepto, x.Cargo, x.Abono, x.SucursalId, suc.GetValueOrDefault(x.SucursalId)?.Nombre, x.Dim1Id, x.Dim2Id, x.Dim3Id,
            Ref(x.Dim1Id), Ref(x.Dim2Id), Ref(x.Dim3Id), x.Proyecto, x.ClienteId, x.ProveedorId, x.CuentaBancariaId,
            Aux(clientes, x.ClienteId), Aux(proveedores, x.ProveedorId), Aux(cuentasBanco, x.CuentaBancariaId)))];
    }

    public async Task<PlantillaRecurrenteResponse> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var p = await db.PlantillasRecurrentes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("CONTAB_RECURRENTE_NO_ENCONTRADA", $"No existe la plantilla recurrente '{id}'.");
        var tipoClave = await db.TiposDocumento.AsNoTracking().Where(t => t.Id == p.TipoOperacionId).Select(t => t.Clave).FirstOrDefaultAsync(ct) ?? "?";
        var polizas = await db.Polizas.AsNoTracking().Where(x => x.PlantillaRecurrenteId == p.Id)
            .Select(x => new { x.Id, x.TipoClave, x.Numero, x.Estado, x.Relacion, x.PolizaRelacionadaId, x.LlaveIdempotencia, x.Fecha }).ToListAsync(ct);
        string? Folio(string clave, int? n) => n is { } v ? Poliza.FormatearFolio(clave, v) : null;
        var aplicaciones = Enumerable.Range(0, p.NumeroPeriodos).Select(k =>
        {
            var (anio, mes) = p.Periodo(k);
            var poliza = polizas.FirstOrDefault(x => x.LlaveIdempotencia == PlantillaRecurrente.Llave(p.Id, anio, mes));
            var reversa = poliza is null ? null : polizas.FirstOrDefault(x => x.Relacion == RelacionPoliza.Reversa && x.PolizaRelacionadaId == poliza.Id);
            return new AplicacionRecurrenteResponse(anio, mes, p.Fecha(anio, mes), poliza?.Id, poliza is null ? null : Folio(poliza.TipoClave, poliza.Numero),
                poliza?.Estado, p.ReversaAutomatica ? PlantillaRecurrente.FechaReversa(anio, mes) : null, reversa?.Id,
                reversa is null ? null : Folio(reversa.TipoClave, reversa.Numero));
        }).ToList();
        return new PlantillaRecurrenteResponse(p.Id, p.Nombre, p.TipoOperacionId, tipoClave, p.Concepto, p.AnioInicio, p.MesInicio, p.NumeroPeriodos,
            p.DiaDelMes, p.ReversaAutomatica, p.NaceAutorizada, p.Estado, p.TotalCargos, await PartidasAsync(p.Partidas, ct),
            p.CreadaPorId, p.CreadaPor, p.CreatedAt, p.ActivadaPorId, p.ActivadaPor, p.ActivadaEn,
            aplicaciones.Count(a => a.PolizaId is not null), aplicaciones, p.Version);
    }
}

internal static class PoliticaRecurrentes
{
    public static async Task<PlantillaRecurrente> CargarAsync(ContabilidadDbContext db, Guid id, int versionEsperada, CancellationToken ct)
    {
        var p = await db.PlantillasRecurrentes.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("CONTAB_RECURRENTE_NO_ENCONTRADA", $"No existe la plantilla recurrente '{id}'.");
        if (p.Version != versionEsperada) throw new ConcurrencyException(nameof(PlantillaRecurrente), id);
        return p;
    }

    public static async Task ValidarPartidasAsync(RegistroPolizas registro, PlantillaRecurrenteRequest r, Guid tipoId, CancellationToken ct)
    {
        var primera = new DateOnly(r.AnioInicio, r.MesInicio, 1);
        await registro.ValidarPartidasAsync(r.Partidas, tipoId, primera, OrigenMovimiento.Manual, ct);
    }
}
