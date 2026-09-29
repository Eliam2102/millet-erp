using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Application.Clientes;

/// <summary>Rechazo explícito de la operación (flag apagada, configuración, estado inválido).</summary>
public sealed class AwClientesSyncException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Orquesta la sincronización de clientes A+W (ADM-06, doc 05 §7): barridos por lotes con
/// progreso durable, reanudación y reintento por referencia. Solo lee el origen y aplica vía
/// <see cref="AplicarClienteAwService"/>; la ausencia de una fila en el origen NUNCA toca el
/// Estatus del cliente. Sin versión monotónica en la fuente: convergencia por relectura.
/// </summary>
public sealed class AwClientesSincronizador
{
    private readonly IntegracionesAwDbContext _db;
    private readonly CompartidoDbContext _compartidoDb;
    private readonly AplicarClienteAwService _aplicar;
    private readonly IServiceProvider _sp;
    private readonly AwClientesOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AwClientesSincronizador> _logger;

    public AwClientesSincronizador(
        IntegracionesAwDbContext db,
        CompartidoDbContext compartidoDb,
        AplicarClienteAwService aplicar,
        IServiceProvider sp,
        IOptions<AwClientesOptions> options,
        TimeProvider time,
        ILogger<AwClientesSincronizador> logger)
    {
        _db = db;
        _compartidoDb = compartidoDb;
        _aplicar = aplicar;
        _sp = sp;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>Crea un barrido Pendiente. Rechaza si hay flags apagadas, origen sin configurar o ya hay uno vivo.</summary>
    public async Task<Guid> IniciarBarridoAsync(string actor, CancellationToken ct)
    {
        VerificarOperable();
        var origen = _options.Origen.ToString();
        if (await _db.ClientesEjecuciones.AnyAsync(e =>
                e.Origen == origen && e.Tipo == AwClientesEjecucionTipo.Barrido
                && (e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso), ct))
            throw new AwClientesSyncException("barrido_en_curso", "Ya hay un barrido de clientes en curso para este origen.");

        var ejec = AwClientesEjecucion.Crear(origen, AwClientesEjecucionTipo.Barrido, actor, _time.GetUtcNow());
        _db.ClientesEjecuciones.Add(ejec);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Carrera con otra instancia: el índice único parcial impide el segundo barrido vivo.
            throw new AwClientesSyncException("barrido_en_curso", "Ya hay un barrido de clientes en curso para este origen.");
        }
        return ejec.Id;
    }

    /// <summary>Recorre lotes desde <c>CursorActual</c>. Cancelar deja la ejecución EnCurso (reanudable).</summary>
    public async Task EjecutarAsync(Guid ejecucionId, CancellationToken ct)
    {
        VerificarOperable();
        var ejec = await _db.ClientesEjecuciones.FirstOrDefaultAsync(e => e.Id == ejecucionId, ct)
            ?? throw new AwClientesSyncException("ejecucion_no_encontrada", "Ejecución de clientes inexistente.");
        if (ejec.Tipo != AwClientesEjecucionTipo.Barrido || !ejec.EstaViva)
            throw new AwClientesSyncException("ejecucion_no_ejecutable", $"La ejecución está en estado {ejec.Estado}; solo se ejecutan barridos Pendiente/EnCurso.");

        var origen = ObtenerOrigen();
        var tamano = Math.Clamp(_options.TamanoLote, 1, AwClientesOptions.TamanoLoteMaximo);
        ejec.Iniciar();
        await _db.SaveChangesAsync(ct);

        while (true)
        {
            AwClientesPagina pagina;
            try
            {
                pagina = await origen.LeerPaginaAsync(ejec.CursorActual, tamano, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Error de lectura del origen (conexión/esquema/timeout): la ejecución falla; lo ya aplicado queda.
                _logger.LogError("Barrido de clientes {EjecucionId} falló leyendo el origen ({Tipo}).", ejec.Id, ex.GetType().Name);
                ejec.Fallar(ex.Message, _time.GetUtcNow());
                await _db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            foreach (var fila in pagina.Filas)
            {
                ct.ThrowIfCancellationRequested();
                await ProcesarFilaAsync(ejec, fila, ct);
            }
            _compartidoDb.ChangeTracker.Clear();

            ejec.AvanzarCursor(pagina.SiguienteCursor ?? ejec.CursorActual);
            if (pagina.SiguienteCursor is null || pagina.Filas.Count == 0)
            {
                ejec.Finalizar(_time.GetUtcNow());
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Barrido de clientes {EjecucionId} terminó en {Estado}. leidos={Leidos} creados={Creados} actualizados={Actualizados} sinCambios={SinCambios} pendientes={Pendientes} conflictos={Conflictos} errores={Errores}",
                    ejec.Id, ejec.Estado, ejec.Leidos, ejec.Creados, ejec.Actualizados, ejec.SinCambios, ejec.Pendientes, ejec.Conflictos, ejec.Errores);
                return;
            }
            await _db.SaveChangesAsync(ct); // progreso durable por lote
        }
    }

    /// <summary>Continúa un barrido EnCurso (o Pendiente) desde su cursor, sin duplicar filas ya contadas.</summary>
    public Task ReanudarAsync(Guid ejecucionId, CancellationToken ct) => EjecutarAsync(ejecucionId, ct);

    /// <summary>
    /// Reintenta UNA referencia: RELEE el origen (nunca reaplica un payload guardado) y aplica.
    /// Crea una ejecución de tipo Referencia; devuelve su id.
    /// </summary>
    public async Task<Guid> ReintentarReferenciaAsync(string referencia, string actor, CancellationToken ct)
    {
        VerificarOperable();
        if (string.IsNullOrWhiteSpace(referencia))
            throw new AwClientesSyncException("referencia_invalida", "La referencia es obligatoria.");
        var origen = ObtenerOrigen();
        referencia = referencia.Trim();

        var ejec = AwClientesEjecucion.Crear(_options.Origen.ToString(), AwClientesEjecucionTipo.Referencia, actor, _time.GetUtcNow());
        ejec.Iniciar();
        _db.ClientesEjecuciones.Add(ejec);
        await _db.SaveChangesAsync(ct);

        AwClienteOrigenFila? fila;
        try
        {
            fila = await origen.LeerPorReferenciaAsync(referencia, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Reintento de cliente {Referencia} falló leyendo el origen ({Tipo}).", referencia, ex.GetType().Name);
            ejec.Fallar(ex.Message, _time.GetUtcNow());
            await _db.SaveChangesAsync(CancellationToken.None);
            return ejec.Id;
        }

        if (fila is null)
            // Ausente en el origen: se reporta, pero NO es baja (no se toca el Estatus del cliente).
            ejec.RegistrarError(referencia, "no_encontrada_en_origen", "La referencia ya no existe en el origen; no se modificó el cliente.", _time.GetUtcNow());
        else
            await ProcesarFilaAsync(ejec, fila, ct);

        _compartidoDb.ChangeTracker.Clear();
        ejec.Finalizar(_time.GetUtcNow());
        await _db.SaveChangesAsync(ct);
        return ejec.Id;
    }

    /// <summary>Un ciclo programado: reanuda el barrido vivo si existe, si no inicia uno nuevo.</summary>
    public async Task EjecutarProgramadoAsync(string actor, CancellationToken ct)
    {
        var origen = _options.Origen.ToString();
        var vivo = await _db.ClientesEjecuciones
            .Where(e => e.Origen == origen && e.Tipo == AwClientesEjecucionTipo.Barrido
                && (e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso))
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
        await EjecutarAsync(vivo ?? await IniciarBarridoAsync(actor, ct), ct);
    }

    private async Task ProcesarFilaAsync(AwClientesEjecucion ejec, AwClienteOrigenFila fila, CancellationToken ct)
    {
        var referencia = fila.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ahora = _time.GetUtcNow();
        var mapeo = AwClienteSnapshotMapper.Mapear(fila, _options.MapeoMoneda, ahora.UtcDateTime);
        if (!mapeo.EsValido)
        {
            ejec.RegistrarError(referencia, "fila_invalida", mapeo.Error!, ahora);
            return;
        }
        try
        {
            var r = await _aplicar.AplicarAsync(mapeo.Snapshot! with { EjecucionId = ejec.Id }, ct);
            ejec.AcumularFila(Reducir(r));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallo por fila: se registra y el barrido sigue (terminará Parcial).
            _compartidoDb.ChangeTracker.Clear();
            _logger.LogWarning("Cliente {Referencia} no se pudo aplicar en {EjecucionId} ({Tipo}).", referencia, ejec.Id, ex.GetType().Name);
            ejec.RegistrarError(referencia, "aplicacion_fallida", ex.Message, ahora);
        }
    }

    private static AwClientesFilaResultado Reducir(AplicarClienteAwResultado r) => r.Accion switch
    {
        AplicarClienteAwAccion.Conflicto => AwClientesFilaResultado.Conflicto,
        _ when r.Resultado == ResultadoSincronizacionAw.Pendiente => AwClientesFilaResultado.Pendiente,
        AplicarClienteAwAccion.Creado => AwClientesFilaResultado.Creado,
        AplicarClienteAwAccion.Actualizado => AwClientesFilaResultado.Actualizado,
        _ => AwClientesFilaResultado.SinCambios,
    };

    private void VerificarOperable()
    {
        if (!_options.LecturaHabilitada)
            throw new AwClientesSyncException("lectura_deshabilitada", "La lectura de clientes A+W está deshabilitada (IntegracionesAw:Clientes:LecturaHabilitada).");
        if (!_options.AplicacionHabilitada)
            throw new AwClientesSyncException("aplicacion_deshabilitada", "La aplicación de clientes A+W está deshabilitada (IntegracionesAw:Clientes:AplicacionHabilitada).");
    }

    // Origen=Sql sin connection string no registra el adaptador: error claro en vez de fallar en DI.
    private IAwClientesOrigen ObtenerOrigen() =>
        _sp.GetService<IAwClientesOrigen>()
        ?? throw new AwClientesSyncException("origen_sin_configurar",
            $"Origen '{_options.Origen}' sin adaptador: falta ConnectionStrings:{AwClientesOptions.ConnectionStringName}.");
}
