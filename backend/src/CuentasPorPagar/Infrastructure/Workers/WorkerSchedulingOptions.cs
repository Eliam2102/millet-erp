namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// Options de scheduling de los workers de CxP. Tras la migración
/// completa a <c>Millet.Integraciones.Fiscal</c> (descarga + refresh
/// SAT), CxP solo retiene el scheduling del mailbox + cualquier otro
/// worker no-SAT. Las opciones de SAT viven ahora en
/// <c>IntegracionesFiscal:Workers</c>.
/// </summary>
public sealed class WorkerSchedulingOptions
{
    public const string SectionName = "CuentasPorPagar:Workers";

    /// <summary>
    /// Si <c>true</c>, los workers no inician su loop principal — útil
    /// para tests E2E que disparan ticks manualmente.
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// Intervalo del worker de ingesta de mailbox (§9 del 01-diseno).
    /// Default 5 minutos (300s) — alto enough para no martillar Graph,
    /// bajo enough para que un CFDI llegue al área en menos de 10 min
    /// desde que el proveedor lo envió.
    /// </summary>
    public int MailboxIntervalSeconds { get; init; } = 300;

    /// <summary>
    /// Mapping de RFCs receptores → EmpresaId usado por el
    /// <c>CfdiMailboxIngestionWorker</c> para asignar el CFDI a la
    /// empresa correcta cuando llega por correo.
    ///
    /// <para>PLATFORM-TODO(&lt;MailboxRfcMapping&gt;): cuando el mailbox
    /// se promueva al módulo <c>Integraciones.Mailbox</c>, este mapeo se
    /// resolverá vía puerto inverso contra
    /// <c>integraciones_fiscal.rfcs_receptores</c> en lugar de appsettings.</para>
    /// </summary>
    public IDictionary<string, Guid> RfcsReceptores { get; init; } = new Dictionary<string, Guid>();
}
