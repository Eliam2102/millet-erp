using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;

namespace Millet.Integraciones.Aw.Application.Productos;

/// <summary>Rechazo explícito de la operación (origen apagado o sin configurar).</summary>
public sealed class AwProductosSyncException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record AwProductoError(string Referencia, string Codigo, string Mensaje);

public sealed record AwProductosResumen(
    int Leidos, int Creados, int Actualizados, int SinCambios, int Pendientes, int Conflictos, int Errores,
    IReadOnlyList<AwProductoError> ErroresPorReferencia);

/// <summary>
/// Sincroniza productos A+W (ADM-07): barrido por páginas o por referencia, aplicando solo vía
/// <see cref="AplicarProductoAwService"/>. Un error de fila no aborta el lote; la ausencia de una
/// fila en el origen NO es baja. Síncrono y acotado (sin ejecución durable: el estado por fila
/// vive en <c>ProductoSincronizacionAw</c>).
/// </summary>
public sealed class AwProductosSincronizador
{
    private readonly CompartidoDbContext _db;
    private readonly AplicarProductoAwService _aplicar;
    private readonly IServiceProvider _sp;
    private readonly AwProductosOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AwProductosSincronizador> _logger;

    public AwProductosSincronizador(
        CompartidoDbContext db, AplicarProductoAwService aplicar, IServiceProvider sp,
        IOptions<AwProductosOptions> options, TimeProvider time, ILogger<AwProductosSincronizador> logger)
    {
        _db = db;
        _aplicar = aplicar;
        _sp = sp;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<AwProductosResumen> SincronizarBarridoAsync(CancellationToken ct)
    {
        var origen = ObtenerOrigen();
        var tamano = Math.Clamp(_options.TamanoLote, 1, AwProductosOptions.TamanoLoteMaximo);
        var acc = new Acumulador();
        string? cursor = null;
        while (true)
        {
            var pagina = await origen.LeerPaginaAsync(cursor, tamano, ct);
            foreach (var fila in pagina.Filas) await ProcesarFilaAsync(fila, acc, ct);
            if (pagina.SiguienteCursor is null || pagina.Filas.Count == 0) break;
            cursor = pagina.SiguienteCursor;
        }
        return acc.Resumen();
    }

    /// <summary>
    /// Relee UNA referencia del origen y la aplica. <paramref name="versionEsperada"/> (If-Match, ADR-0012):
    /// si el producto ya cambió, el resultado es conflicto y no se escribe nada.
    /// </summary>
    public async Task<AwProductosResumen> SincronizarReferenciaAsync(
        string referencia, CancellationToken ct, int? versionEsperada = null)
    {
        if (string.IsNullOrWhiteSpace(referencia))
            throw new AwProductosSyncException("referencia_invalida", "La referencia es obligatoria.");
        var origen = ObtenerOrigen();
        referencia = referencia.Trim();
        var acc = new Acumulador();
        var fila = await origen.LeerPorReferenciaAsync(referencia, ct);
        if (fila is null)
        {
            acc.Error(referencia, "no_encontrada_en_origen", "La referencia ya no existe en el origen; no se modificó el producto.");
            acc.Leidos--; // no se leyó ninguna fila
        }
        else await ProcesarFilaAsync(fila, acc, ct, versionEsperada);
        return acc.Resumen();
    }

    private async Task ProcesarFilaAsync(AwProductoOrigenFila fila, Acumulador acc, CancellationToken ct, int? versionEsperada = null)
    {
        acc.Leidos++;
        var referencia = fila.ProductoRef?.Trim() ?? "(sin referencia)";
        var mapeo = AwProductoSnapshotMapper.Mapear(fila, _time.GetUtcNow().UtcDateTime, _options.ReglasFiscales);
        if (!mapeo.EsValido)
        {
            acc.Error(referencia, "fila_invalida", mapeo.Error!);
            return;
        }
        try
        {
            var r = await _aplicar.AplicarAsync(mapeo.Snapshot! with { VersionEsperada = versionEsperada }, ct);
            switch (r.Accion)
            {
                case AplicarProductoAwAccion.Creado: acc.Creados++; break;
                case AplicarProductoAwAccion.Actualizado: acc.Actualizados++; break;
                case AplicarProductoAwAccion.SinCambios: acc.SinCambios++; break;
                case AplicarProductoAwAccion.Conflicto: acc.Conflictos++; break;
                case AplicarProductoAwAccion.NoAplicado:
                    acc.Pendientes++;
                    acc.Error(referencia, r.Causa ?? "no_aplicado", $"Producto no aplicado ({r.Causa}).", contar: false);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallo por fila: se registra y el lote sigue. Solo tipo: ex.Message puede traer datos.
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Producto {Referencia} no se pudo aplicar ({Tipo}).", referencia, ex.GetType().Name);
            acc.Error(referencia, "aplicacion_fallida", $"No se pudo aplicar el producto ({ex.GetType().Name})");
        }
        _db.ChangeTracker.Clear();
    }

    // Sin adaptador registrado (origen apagado o sin archivo/conexión): error claro en vez de fallar en DI.
    private IAwProductosOrigen ObtenerOrigen()
    {
        if (!_options.OrigenHabilitado)
            throw new AwProductosSyncException("origen_deshabilitado",
                $"La lectura de productos A+W está deshabilitada ({AwProductosOptions.SectionName}:OrigenHabilitado).");
        return _sp.GetService<IAwProductosOrigen>()
            ?? throw new AwProductosSyncException("origen_sin_configurar", "Origen de productos sin adaptador configurado.");
    }

    private sealed class Acumulador
    {
        public int Leidos, Creados, Actualizados, SinCambios, Pendientes, Conflictos, Errores;
        private readonly List<AwProductoError> _errores = [];

        public void Error(string referencia, string codigo, string mensaje, bool contar = true)
        {
            if (contar) Errores++;
            _errores.Add(new(referencia, codigo, mensaje));
        }

        public AwProductosResumen Resumen() =>
            new(Leidos, Creados, Actualizados, SinCambios, Pendientes, Conflictos, Errores, _errores);
    }
}
