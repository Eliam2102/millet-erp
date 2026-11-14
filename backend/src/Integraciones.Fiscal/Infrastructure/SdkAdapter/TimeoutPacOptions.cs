namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// Plazo máximo de espera de las llamadas de timbrado al PAC (crear el CFDI y bajar su XML). El SDK de FiscalAPI no acepta
/// CancellationToken: sin este plazo, un PAC colgado retiene el request HTTP, su scope y su DbContext. Vencido el plazo, la
/// emisión sigue el camino ya existente de resultado ambiguo (EnProceso) o de timbrado sin XML.
/// <para>
/// Hoy aplica solo a Carta Porte (<see cref="AplicaATodos"/> = false). Generalizarlo a los demás tipos espera la conciliación
/// por UUID/folio (M-341, PLATFORM-TODO(&lt;TimbradoReconciliacionFolio&gt;)): sin ella, el TimbradoPendienteWorker marca el
/// comprobante como fallido a los 30 min sin consultar al PAC, y re-emitir puede duplicar el CFDI si el PAC sí lo creó.
/// </para>
/// </summary>
public sealed class TimeoutPacOptions
{
    public const string SectionName = "Integraciones:Fiscal";

    /// <summary>Segundos de espera por llamada al PAC (45 por omisión; admite fracciones). 0 o negativo = sin plazo.</summary>
    public double TimeoutPacSegundos { get; set; } = 45;

    /// <summary>false = solo Carta Porte. Reservado para generalizar cuando exista la conciliación por UUID/folio.</summary>
    public bool AplicaATodos { get; set; }
}
