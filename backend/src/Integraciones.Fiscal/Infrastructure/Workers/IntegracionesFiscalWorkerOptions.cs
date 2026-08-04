namespace Millet.Integraciones.Fiscal.Infrastructure.Workers;

/// <summary>
/// Opciones de los workers del módulo Integraciones.Fiscal. Vienen
/// bindeadas de la sección <c>"IntegracionesFiscal:Workers"</c> de
/// <c>IConfiguration</c>.
///
/// <para>
/// <b>Disabled</b>: bandera maestra. Cambió de <c>true</c> (PR-6
/// introductorio) a <c>false</c> (PR-7 cutover). El
/// <c>CfdiDescargaMasivaSatWorker</c> viejo de CxP queda apagado por
/// <c>CuentasPorPagar:Workers:DescargaMasivaSatDisabled=true</c>.
/// </para>
///
/// <para>
/// <b>Rollback rápido</b>: setear este flag a <c>true</c> +
/// <c>CuentasPorPagar:Workers:DescargaMasivaSatDisabled=false</c> +
/// restart App Service.
/// </para>
///
/// <para>
/// <b>TickIntervalSeconds</b>: cuánto duerme el worker entre ticks.
/// Default conservador (1 hora) — la frecuencia REAL por configuración
/// vive en cada <c>configuracion_pac.descarga_intervalo_segundos</c>.
/// El tick global solo decide cuándo escanear las configuraciones; el
/// per-config decide si toca procesar (basado en checkpoint).
/// </para>
/// </summary>
public sealed class IntegracionesFiscalWorkerOptions
{
    public const string SectionName = "IntegracionesFiscal:Workers";

    // PR-13: opciones top-level legacy (Disabled, TickIntervalSeconds,
    // RefreshBatchSize) eliminadas junto con los workers viejos
    // DescargaMasivaSatWorker y EstadoSatRefreshWorker. Los workers
    // nuevos (Submitter + Poller) tienen sus propios sub-options.

    /// <summary>Opciones del Submitter (1×/día crea solicitudes).</summary>
    public SubmitterOptions Submitter { get; init; } = new();

    /// <summary>Opciones del Poller (cada N min consulta + cosecha).</summary>
    public PollerOptions Poller { get; init; } = new();

    public sealed class SubmitterOptions
    {
        /// <summary>
        /// Apaga el worker. Default <c>true</c> hasta que se valide el
        /// flujo en QA con FIEL real cargada. PR-13 lo activará junto
        /// con el deprecation del worker viejo.
        /// </summary>
        public bool Disabled { get; init; } = true;

        /// <summary>
        /// Segundos entre ticks del Submitter. Default 1h — pero el
        /// trabajo real solo crea solicitudes para días que aún no han
        /// sido cubiertos (idempotente), así que un tick más frecuente
        /// no genera carga extra.
        /// </summary>
        public int TickIntervalSeconds { get; init; } = 3600;

        /// <summary>
        /// Cuántos días hacia atrás cubrir. Default 7 — al onboarding de
        /// una empresa nueva, los primeros 7 días se descargan al
        /// arrancar el worker (luego solo el día anterior).
        /// </summary>
        public int BackfillDays { get; init; } = 7;

        /// <summary>
        /// Días máximos por solicitud individual. Límite FiscalAPI/SAT:
        /// 31 para CFDI/Metadata, 400 para Retenciones. El Submitter
        /// reparte el backfill en N solicitudes de máximo este tamaño.
        /// </summary>
        public int MaxDaysPerRequest { get; init; } = 1;
    }

    public sealed class PollerOptions
    {
        /// <summary>Apaga el worker. Default <c>true</c> hasta validar.</summary>
        public bool Disabled { get; init; } = true;

        /// <summary>
        /// Segundos entre ticks. Default 900 (15min) — alineado con el
        /// polling interno de FiscalAPI hacia SAT (cada 30 min). Bajarlo
        /// más no acelera; el bottleneck es el SAT.
        /// </summary>
        public int TickIntervalSeconds { get; init; } = 900;

        /// <summary>
        /// Cuántas solicitudes vivas procesar por tick. Default 50;
        /// previene que una sola empresa con cola larga monopolice el
        /// worker en clusters con muchas empresas.
        /// </summary>
        public int BatchSize { get; init; } = 50;

        /// <summary>
        /// Cuántos meta-items cosechar antes de hacer SaveChanges. Default
        /// 200 — balance entre transacciones largas y robustez ante crash.
        /// </summary>
        public int CosechaChunkSize { get; init; } = 200;

        /// <summary>
        /// Cap del backoff exponencial entre polls de una solicitud
        /// individual (minutos). Default 360 (6h).
        /// </summary>
        public int MaxBackoffMinutes { get; init; } = 360;
    }
}
