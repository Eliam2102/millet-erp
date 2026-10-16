using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

namespace Millet.Contabilidad.Application.Polizas;

/// <summary>Partida tal como la captura el usuario o la envía el motor. Cargo y abono en MXN.</summary>
public sealed record PartidaRequest(
    Guid CuentaId, string? Concepto, decimal Cargo, decimal Abono, Guid SucursalId,
    Guid? Dim1Id = null, Guid? Dim2Id = null, Guid? Dim3Id = null, string? Proyecto = null,
    Guid? ClienteId = null, Guid? ProveedorId = null, Guid? CuentaBancariaId = null,
    string? Moneda = null, decimal? TipoCambio = null, decimal? ImporteOriginal = null);

/// <summary>Partidas ya validadas y los ids de las reglas de dimensión que las validaron (para registrar su uso).</summary>
public sealed record PartidasValidadas(IReadOnlyList<DatosPartida> Partidas, IReadOnlyList<Guid> ReglasAplicadas);

/// <summary>
/// Piezas comunes de las pólizas (C1.3, K10.3, K10.9): validación de partidas con el validador de dimensiones (cuenta, tipo,
/// sucursal, centros, auxiliares y reglas de K10.2), candado de periodo, consecutivo por tipo y año sin huecos, y registro del uso
/// de cuentas y reglas al autorizar.
/// </summary>
public sealed class RegistroPolizas(
    ContabilidadDbContext db, ValidadorDimensiones validador, IPeriodoContableConsultaPort periodos, ICurrentEmpresaContext empresa,
    IMediator mediator)
{
    /// <summary>Consumidor con que la póliza registra el uso de cuentas y reglas (la referencia es el id de la póliza).</summary>
    public const string ConsumidorPoliza = "POLIZA";

    /// <summary>
    /// Transacción de escritura de pólizas. Toma los candados de periodos y de reglas en modo COMPARTIDO: las pólizas no se
    /// bloquean entre sí, pero un cierre de periodo o un cambio de reglas (que los toman exclusivos) espera a que terminen, y
    /// viceversa. Mismo orden que los movimientos de prueba (periodos, luego reglas): sin interbloqueos.
    /// </summary>
    public async Task EnTransaccionAsync(Func<CancellationToken, Task> operacion, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await TomarCandadosAsync(ct);
            await operacion(ct);
            return;
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await TomarCandadosAsync(ct);
        await operacion(ct);
        await tx.CommitAsync(ct);
    }

    private Task<int> TomarCandadosAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock_shared({PoliticaPeriodos.LockPeriodos}), pg_advisory_xact_lock_shared({PoliticaReglas.LockReglas})", ct);

    /// <summary>
    /// K10.9: siguiente consecutivo del tipo en el año. UPSERT con <c>RETURNING</c>: la fila queda bloqueada hasta el commit de la
    /// transacción que guarda la póliza, así que dos pólizas concurrentes del mismo tipo esperan su turno (sin duplicados) y un
    /// rollback devuelve el número (sin huecos). Un tipo nuevo crea su fila en el primer uso (K10.9-c).
    /// </summary>
    public async Task<int> ReservarNumeroAsync(Guid tipoOperacionId, int anio, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("El consecutivo se reserva dentro de la transacción que guarda la póliza.");
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(ConsecutivoPoliza));
        try
        {
            var numeros = await db.Database.SqlQuery<int>($"""
                INSERT INTO contabilidad.consecutivos_poliza (empresa_id, tipo_operacion_id, anio, ultimo_numero)
                VALUES ({empresaId}, {tipoOperacionId}, {anio}, 1)
                ON CONFLICT (empresa_id, tipo_operacion_id, anio)
                DO UPDATE SET ultimo_numero = consecutivos_poliza.ultimo_numero + 1
                RETURNING ultimo_numero AS "Value"
                """).ToListAsync(ct);
            return numeros[0];
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation)
        {
            throw new BusinessRuleException("CONTAB_POLIZA_CONSECUTIVO_AGOTADO", $"El consecutivo del tipo en {anio} llegó al máximo de 6 dígitos.");
        }
    }

    public async Task<TipoDocumentoContable> TipoAsync(Guid tipoOperacionId, CancellationToken ct)
    {
        var tipo = await db.TiposDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tipoOperacionId, ct)
            ?? throw new ValidationException([new("tipoOperacionId", "CONTAB_POLIZA_TIPO_NO_EXISTE", "El tipo de operación no existe.")]);
        if (!tipo.Activo)
            throw new ValidationException([new("tipoOperacionId", "CONTAB_POLIZA_TIPO_INACTIVO", $"El tipo de operación {tipo.Clave} está inactivo.")]);
        return tipo;
    }

    /// <summary>
    /// Candado de periodo (F1-CON-03) para una póliza: falla cerrada si el periodo no existe, no está abierto o está cerrado; el 13
    /// de ajustes solo admite pólizas manuales (CA10.12).
    /// </summary>
    public async Task VerificarPeriodoAsync(int anio, int numero, OrigenPoliza origen, CancellationToken ct)
    {
        if (await RechazoPeriodoAsync(anio, numero, origen, ct) is { } rechazo) throw rechazo;
    }

    public async Task<BusinessRuleException?> RechazoPeriodoAsync(int anio, int numero, OrigenPoliza origen, CancellationToken ct)
    {
        var estado = await periodos.ConsultarAsync(anio, numero, ct);
        return VerificadorPeriodoContable.Rechazo(estado,
            origen == OrigenPoliza.Manual ? OrigenMovimiento.Manual : OrigenMovimiento.AuxiliarCxP) is { } rechazo
            ? new BusinessRuleException(rechazo.Code, rechazo.Message.Replace("movimientos", "pólizas"))
            : null;
    }

    /// <summary>
    /// Valida cada partida contra el catálogo y las reglas de dimensión (K10.2) a la fecha de la póliza y junta TODOS los errores
    /// (400 con el campo <c>partidas[i].campo</c>). Guarda los centros efectivos (los superiores derivados por jerarquía).
    /// </summary>
    public async Task<PartidasValidadas> ValidarPartidasAsync(
        IReadOnlyList<PartidaRequest> partidas, Guid tipoOperacionId, DateOnly fecha, OrigenMovimiento origen, CancellationToken ct)
    {
        var errores = new List<ValidationError>();
        var datos = new List<DatosPartida>();
        var reglas = new HashSet<Guid>();
        if (partidas.Count == 0)
            errores.Add(new("partidas", "CONTAB_POLIZA_SIN_PARTIDAS", "Capture al menos dos partidas."));
        var ids = partidas.Select(p => p.CuentaId).Distinct().ToList();
        var codigos = await db.Cuentas.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Codigo, ct);
        for (var i = 0; i < partidas.Count; i++)
        {
            var p = partidas[i];
            var proyecto = string.IsNullOrWhiteSpace(p.Proyecto) ? null : p.Proyecto.Trim();
            var v = await validador.ValidarAsync(new MovimientoDimensionado(p.CuentaId, tipoOperacionId, fecha, p.SucursalId,
                p.Dim1Id, p.Dim2Id, p.Dim3Id, origen, proyecto, p.ClienteId, p.ProveedorId, p.CuentaBancariaId), ct);
            // El tipo se reporta una sola vez en el encabezado (TipoAsync), no en cada partida.
            errores.AddRange(v.Errores.Where(e => e.Campo != "tipoDocumentoId")
                .Select(e => new ValidationError($"partidas[{i}].{e.Campo}", e.Codigo, $"Partida {i + 1}: {e.Mensaje}")));
            foreach (var r in v.Requerimientos.Where(r => r.ReglaId is not null)) reglas.Add(r.ReglaId!.Value);
            datos.Add(new DatosPartida(p.CuentaId, codigos.GetValueOrDefault(p.CuentaId, "?"), p.Concepto, p.Cargo, p.Abono,
                p.Moneda ?? Poliza.MonedaNacional, p.TipoCambio ?? 1m, p.ImporteOriginal, p.SucursalId,
                v.Centros.Dim1Id, v.Centros.Dim2Id, v.Centros.Dim3Id, proyecto, p.ClienteId, p.ProveedorId, p.CuentaBancariaId));
        }
        if (errores.Count > 0) throw new ValidationException(errores);
        return new(datos, [.. reglas]);
    }

    /// <summary>
    /// Al autorizar (o al nacer autorizada la automática): marca las cuentas como usadas con <see cref="RegistrarUsoCuentaCommand"/>
    /// (ADR-0055) y registra el uso de las reglas de dimensión que validaron las partidas. Corre dentro de la transacción de la póliza.
    /// </summary>
    public async Task RegistrarUsosAsync(Poliza poliza, IEnumerable<Guid> reglasAplicadas, CancellationToken ct)
    {
        var referencia = poliza.Id.ToString();
        foreach (var cuentaId in poliza.Partidas.Select(x => x.CuentaId).Distinct())
            await mediator.Send(new RegistrarUsoCuentaCommand(cuentaId, ConsumidorPoliza, referencia), ct);
        foreach (var reglaId in reglasAplicadas.Distinct())
            db.ReglasDimensionUso.Add(new ReglaDimensionUso(Guid.CreateVersion7(), reglaId, ConsumidorPoliza, referencia, poliza.Fecha));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// K10.3: anula la póliza dentro de la transacción en curso. El destino es el periodo de la original si sigue admitiendo
    /// pólizas; si no, el periodo abierto indicado (<paramref name="destino"/>). La anulación toma el siguiente consecutivo del mismo
    /// tipo en el año del periodo destino.
    /// </summary>
    public async Task<Poliza> AnularAsync(Poliza original, string motivo, (int Anio, int Numero)? destino, DateOnly hoy,
        Guid? usuarioId, string usuarioNombre, DateTimeOffset ahora, CancellationToken ct)
    {
        original.ExigirAnulable(motivo);
        var tipo = await db.TiposDocumento.AsNoTracking().FirstAsync(t => t.Id == original.TipoOperacionId, ct);
        var periodoOriginal = (original.PeriodoAnio, original.PeriodoNumero);
        var rechazoOriginal = await RechazoPeriodoAsync(original.PeriodoAnio, original.PeriodoNumero, original.Origen, ct);
        DateOnly fecha;
        (int Anio, int Numero) periodo;
        if (rechazoOriginal is null)
        {
            if (destino is { } d && d != periodoOriginal)
                throw new BusinessRuleException("CONTAB_POLIZA_ANULACION_PERIODO_ORIGINAL_ABIERTO",
                    $"El periodo {PeriodoContable.Etiqueta(original.PeriodoAnio, original.PeriodoNumero)} de la póliza sigue abierto: la anulación va en ese mismo periodo.");
            periodo = periodoOriginal;
            fecha = original.Fecha;
        }
        else
        {
            if (destino is not { } d)
                throw new BusinessRuleException("CONTAB_POLIZA_ANULACION_REQUIERE_PERIODO",
                    $"{rechazoOriginal.Message} Indique el periodo abierto en que se registra la anulación.");
            await VerificarPeriodoAsync(d.Anio, d.Numero, original.Origen, ct);
            periodo = d;
            fecha = FechaEnPeriodo(d.Anio, d.Numero, hoy);
        }
        var numero = await ReservarNumeroAsync(original.TipoOperacionId, periodo.Anio, ct);
        var anulacion = original.Anular(Guid.CreateVersion7(), tipo, fecha, periodo.Anio, periodo.Numero, numero, motivo,
            usuarioId, usuarioNombre, ahora);
        db.Polizas.Add(anulacion);
        return anulacion;
    }

    /// <summary>Fecha de la anulación en un periodo distinto: hoy si cae en él; si no, su primer día (el 13 siempre es 31-dic).</summary>
    public static DateOnly FechaEnPeriodo(int anio, int numero, DateOnly hoy)
    {
        if (numero == PeriodoContable.NumeroAjuste) return new DateOnly(anio, 12, 31);
        return PeriodoContable.PorFecha(hoy) == (anio, numero) ? hoy : new DateOnly(anio, numero, 1);
    }

    /// <summary>Periodo de una póliza: el 13 se indica explícitamente; los demás salen de la fecha.</summary>
    public static (int Anio, int Numero) Periodo(DateOnly fecha, int? numeroPeriodo) =>
        numeroPeriodo == PeriodoContable.NumeroAjuste ? (fecha.Year, PeriodoContable.NumeroAjuste) : PeriodoContable.PorFecha(fecha);
}
