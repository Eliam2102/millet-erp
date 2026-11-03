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
    /// Default 15 minutos (900s, P8.1): no martilla Graph y el CFDI
    /// llega al área en menos de 20 min desde que el proveedor lo envió.
    /// </summary>
    public int MailboxIntervalSeconds { get; init; } = 900;

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
