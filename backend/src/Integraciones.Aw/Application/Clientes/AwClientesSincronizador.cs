using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Origen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Incidencias;
using Millet.Compartido.Application.Incidencias;

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
        var origen = await NombreOrigenActivoAsync(ct);
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
                ejec.Fallar(DescribirErrorOrigen(ex), _time.GetUtcNow());
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
                await UnificarSinHistorialAsync(ejec, ct);
                ejec.Finalizar(_time.GetUtcNow());
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Barrido de clientes {EjecucionId} terminó en {Estado}. leidos={Leidos} creados={Creados} actualizados={Actualizados} sinCambios={SinCambios} pendientes={Pendientes} conflictos={Conflictos} errores={Errores} unificados={Unificados}",
                    ejec.Id, ejec.Estado, ejec.Leidos, ejec.Creados, ejec.Actualizados, ejec.SinCambios, ejec.Pendientes, ejec.Conflictos, ejec.Errores, ejec.Unificados);
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

        var ejec = AwClientesEjecucion.Crear(await NombreOrigenActivoAsync(ct), AwClientesEjecucionTipo.Referencia, actor, _time.GetUtcNow());
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
            ejec.Fallar(DescribirErrorOrigen(ex), _time.GetUtcNow());
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

    private async Task ProcesarFilaAsync(AwClientesEjecucion ejec, AwClienteOrigenFila fila, CancellationToken ct)
    {
        var referencia = fila.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ahora = _time.GetUtcNow();
        var mapeo = AwClienteSnapshotMapper.Mapear(fila, _options.MapeoMoneda, ahora.UtcDateTime, fila.AplicarFiscalesDeOrigen);
        if (!mapeo.EsValido)
        {
            ejec.RegistrarError(referencia, "fila_invalida", mapeo.Error!, ahora);
            return;
        }
        // N-08 (Jorge, 10-nov): la carga solo trae clientes activos (KZ_GESPERRT = 0). Un bloqueado que el ERP no tiene no
        // se da de alta; el que ya existe sí recibe la baja (0 → 1) al aplicarse.
        if (_sp.GetService<IOptions<MapeoEstatusClientesAwOptions>>()?.Value.Resolver(fila.KzStatus, fila.KzGesperrt) == true
            && !await _compartidoDb.Clientes.AnyAsync(c => c.ReferenciaExterna == referencia, ct))
        {
            ejec.AcumularFila(AwClientesFilaResultado.SinCambios, false);
            return;
        }
        try
        {
            var r = await _aplicar.AplicarAsync(mapeo.Snapshot! with { EjecucionId = ejec.Id }, ct);
            if (r.Accion == AplicarClienteAwAccion.NoCreado)
                ejec.RegistrarError(referencia, "moneda_sin_equivalencia",
                    $"Cliente no creado: la moneda de origen '{mapeo.Snapshot!.MonedaCodigoOrigen ?? "(vacía)"}' no tiene equivalencia validada.", ahora);
            else if (r.Accion == AplicarClienteAwAccion.Conflicto)
                ejec.RegistrarConflicto(referencia, "conflicto_correlacion", "Existe una coincidencia de RFC o referencia externa. Revisa el ligado en Datos maestros → Clientes.", ahora);
            else
                ejec.AcumularFila(Reducir(r), r.Resultado == ResultadoSincronizacionAw.Pendiente);
            if (r.Cliente is not null && r.Accion != AplicarClienteAwAccion.Conflicto)
                await RevisarIncidenciaBloqueoAsync(r.Cliente, fila, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallo por fila: se registra y el barrido sigue (terminará Parcial).
            _compartidoDb.ChangeTracker.Clear();
            _logger.LogWarning("Cliente {Referencia} no se pudo aplicar en {EjecucionId} ({Tipo}).", referencia, ejec.Id, ex.GetType().Name);
            // Solo código estable + tipo: ex.Message puede traer datos personales o secretos.
            ejec.RegistrarError(referencia, "aplicacion_fallida", $"No se pudo aplicar el cliente ({ex.GetType().Name})", ahora);
        }
    }

    /// <summary>
    /// M-299: una marca de estado o bloqueo de A+W sin equivalencia abre una incidencia por cliente (una sola mientras siga
    /// pendiente) con responsable y regreso a la ficha. Cuando la marca ya tiene equivalencia y se re-sincroniza, se resuelve.
    /// Un fallo aquí no detiene el barrido: el cliente ya quedó Pendiente en la bitácora.
    /// </summary>
    private async Task RevisarIncidenciaBloqueoAsync(Cliente cliente, AwClienteOrigenFila fila, CancellationToken ct)
    {
        var mapeo = _sp.GetService<IOptions<MapeoEstatusClientesAwOptions>>()?.Value;
        if (mapeo is not { Habilitado: true }) return;
        const string paso = PasosIncidencia.AdmClienteBloqueoAwSinEquivalencia;
        try
        {
            var pendientes = await _compartidoDb.Incidencias.Include(x => x.Historial)
                .Where(x => x.Paso == paso && x.DocumentoId == cliente.Id
                    && (x.Estado == EstadoIncidencia.Abierta || x.Estado == EstadoIncidencia.EnAtencion))
                .ToListAsync(ct);
            var ahora = _time.GetUtcNow();
            if (!mapeo.TieneCodigoSinMapeo(fila.KzStatus, fila.KzGesperrt))
            {
                foreach (var i in pendientes)
                    i.Resolver(null, null, "La marca de A+W ya tiene equivalencia; se aplicó al re-sincronizar.", ahora);
                if (pendientes.Count > 0) await _compartidoDb.SaveChangesAsync(ct);
                return;
            }
            if (pendientes.Count > 0) return;

            var cfg = _options.Incidencias;
            if (cfg.EmpresaId is not { } empresaId || cfg.ResponsableUsuarioId is not { } responsableId)
            {
                _logger.LogWarning("Cliente {Clave} con marca de A+W sin equivalencia y sin responsable configurado ({Seccion}:Incidencias); no se abrió incidencia.",
                    cliente.Clave, AwClientesOptions.SectionName);
                return;
            }
            var nombres = _sp.GetService<IIncidenciaUsuariosReadPort>() is { } usuarios
                ? await usuarios.ObtenerNombresAsync([responsableId], ct) : new Dictionary<Guid, string>();
            var responsable = nombres.TryGetValue(responsableId, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Responsable de clientes";
            var marca = $"KZ_GESPERRT={fila.KzGesperrt}" + (fila.KzStatus is { } e ? $", KZ_STATUS={e}" : "");
            _compartidoDb.Incidencias.Add(Incidencia.Abrir(new Incidencia.Datos(
                empresaId, "datos_maestros", "cliente", cliente.Id, $"{cliente.Clave} · {cliente.RazonSocial}", null, paso,
                "AW_MARCA_SIN_EQUIVALENCIA",
                $"El cliente llegó de A+W con una marca sin equivalencia ({marca}); no se aplicó baja ni reactivación.",
                "Agrega la equivalencia en el mapeo de estatus de clientes A+W y vuelve a sincronizar el cliente.",
                null, responsableId, responsable, null, null, $"/admin/datos-maestros/clientes/{cliente.Id}"), null, null, ahora));
            await _compartidoDb.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _compartidoDb.ChangeTracker.Clear();
            _logger.LogWarning("No se pudo revisar la incidencia de bloqueo del cliente {Clave} ({Tipo}).", cliente.Clave,
                ex is BusinessRuleException br ? br.Code : ex.GetType().Name);
        }
    }

    /// <summary>Un fallo aquí no tumba el barrido: los grupos sin unificar siguen pendientes y se reintentan en el siguiente.</summary>
    private async Task UnificarSinHistorialAsync(AwClientesEjecucion ejec, CancellationToken ct)
    {
        if (!_options.UnificacionAutomaticaHabilitada || _sp.GetService<ISender>() is not { } sender) return;
        // Simulado/Demo no leen el historial real (AwClientesPedidosVacio dice "sin historial"): unificaría grupos con pedidos.
        if (ejec.Origen != nameof(AwClientesOrigenTipo.Sql))
        {
            _logger.LogInformation("Unificación automática omitida en {EjecucionId}: el origen {Origen} no tiene historial real de A+W.", ejec.Id, ejec.Origen);
            return;
        }
        try
        {
            ejec.RegistrarUnificados(await sender.Send(new UnificarRfcRepetidosAutomaticoCommand("sistema:unificacion-automatica"), ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Unificación automática de RFC repetido falló en {EjecucionId} ({Tipo}).", ejec.Id, ex.GetType().Name);
        }
        _compartidoDb.ChangeTracker.Clear();
    }

    private static AwClientesFilaResultado Reducir(AplicarClienteAwResultado r) => r.Accion switch
    {
        AplicarClienteAwAccion.Conflicto => AwClientesFilaResultado.Conflicto,
        AplicarClienteAwAccion.Creado => AwClientesFilaResultado.Creado,
        AplicarClienteAwAccion.Actualizado => AwClientesFilaResultado.Actualizado,
        _ => AwClientesFilaResultado.SinCambios,
    };

    private void VerificarOperable()
    {
        if (!_options.LecturaHabilitada)
            throw new AwClientesSyncException("lectura_deshabilitada", "La lectura de clientes A+W está apagada en este ambiente. Avisa a TI.");
        if (!_options.AplicacionHabilitada)
            throw new AwClientesSyncException("aplicacion_deshabilitada", "La aplicación de clientes A+W está apagada en este ambiente. Avisa a TI.");
    }

    private async Task<string> NombreOrigenActivoAsync(CancellationToken ct)
    {
        // Sin provider en los tests/consumidores previos, se conserva el nombre de su área.
        var activo = _sp.GetService<IAwOrigenActivo>();
        if (activo is null) return _options.Origen.ToString();
        var estado = await activo.LeerAsync(ct);
        if (estado.Origen != "Demo") return _options.Origen.ToString();
        AwOrigenActivo.VerificarDemo(estado);
        return "Demo";
    }

    // Solo mensajes de configuración controlados; nunca exponer mensajes de proveedores/credenciales.
    private static string DescribirErrorOrigen(Exception ex) => ex switch
    {
        AwClientesSyncException { Code: "origen_sin_configurar" } => $"origen_sin_configurar: {ex.Message}",
        BusinessRuleException { Code: "AW_DEMO_NO_CONFIGURADA" or "AW_DEMO_NO_PERMITIDO" } => ex.Message,
        _ => $"lectura_origen_fallida ({ex.GetType().Name})",
    };

    // Compatible con consumidores sin selector; el selector rechaza al usar un Real no configurado.
    private IAwClientesOrigen ObtenerOrigen() =>
        _sp.GetService<IAwClientesOrigen>()
        ?? throw new AwClientesSyncException("origen_sin_configurar",
            $"Origen '{_options.Origen}' sin adaptador: falta ConnectionStrings:{AwClientesOptions.ConnectionStringName}.");
}
