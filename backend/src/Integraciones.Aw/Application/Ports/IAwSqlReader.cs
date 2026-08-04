namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Puerto out-going que abstrae la lectura SQL del A+W on-prem
/// (<c>pool_auftrag</c> y otras tablas) via Hybrid Connection. Lo
/// consumirá <c>AwCorrelationWorker</c> en PR C para hacer matching
/// de cotizaciones enviadas contra los pedidos materializados en A+W.
///
/// <para>
/// // PLATFORM-TODO(&lt;AwSqlReaderImpl&gt;): implementación SQL via
/// Hybrid Connection en PR C. Conexión a SQL Server on-prem via
/// hc-aw-business-sql, consultando pool_auftrag con
/// auftragsnummer_kunde IN (lista de referencias externas pendientes).
/// </para>
/// </summary>
public interface IAwSqlReader
{
    Task<IReadOnlyList<AwOrderRecord>> GetOrdersByExternalRefsAsync(
        IReadOnlyList<string> externalRefs,
        CancellationToken cancellationToken);
}

/// <summary>
/// Registro mínimo de una orden encontrada en A+W para correlación.
/// Subset de <c>pool_auftrag</c> — solo los campos que el worker
/// necesita. El snapshot completo se persiste opcionalmente en
/// <c>Correlacion.AwRecordSnapshot</c> via JSON serialization si se
/// requiere para auditoría.
/// </summary>
public sealed record AwOrderRecord(string QuoteReference, long AwDocId, DateTimeOffset CreatedAtAw);
