using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.Adapters;

/// <summary>
/// Implementación SQL de <see cref="IAwSqlReader"/> contra SQL Server
/// on-prem (<c>SER-DATA</c>) vía Hybrid Connection. El código solo abre
/// <c>Microsoft.Data.SqlClient.SqlConnection</c> contra la connection
/// string configurada — Azure App Service tunelea el TCP por la HC.
///
/// <para>
/// <b>Cierre del PLATFORM-TODO &lt;AwSqlReaderImpl&gt;</b> que PR B dejó
/// abierto en <see cref="IAwSqlReader"/>.
/// </para>
///
/// <para>
/// <b>Connection per-call:</b> cada llamada abre + cierra una nueva
/// conexión via <c>using</c>. El pooling de Microsoft.Data.SqlClient
/// reutiliza conexiones físicas entre llamadas — no hay overhead real.
/// </para>
/// </summary>
public sealed class HybridConnectionAwSqlReader : IAwSqlReader
{
    /// <summary>
    /// Campo en <c>pool_auftrag</c> donde A+W persiste la
    /// <c>quote_reference</c> que el ERP envía en el EDI. Si el equipo
    /// A+W de Millet termina diciendo que el campo correcto es otro
    /// (ej. <c>referenz_extern</c>, <c>bestellnummer_kunde</c>, custom),
    /// cambiar acá — UN solo lugar.
    ///
    /// // PLATFORM-TODO(&lt;AwCorrelationField&gt;): confirmar con equipo
    ///   A+W de Millet. Default <c>auftragsnummer_kunde</c> según doc 02 §6.3.
    /// </summary>
    internal const string AwExternalReferenceField = "auftragsnummer_kunde";

    /// <summary>
    /// Tope defensivo de refs por query. Si el caller pide más, el
    /// reader lo trunca (con log warning) — evita queries gigantes que
    /// puedan saturar SQL on-prem.
    /// </summary>
    private const int MaxRefsPerQuery = 1000;

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IntegracionesAwOptions _options;
    private readonly ILogger<HybridConnectionAwSqlReader> _logger;

    public HybridConnectionAwSqlReader(
        ISqlConnectionFactory connectionFactory,
        IOptions<IntegracionesAwOptions> options,
        ILogger<HybridConnectionAwSqlReader> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AwOrderRecord>> GetOrdersByExternalRefsAsync(
        IReadOnlyList<string> externalRefs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalRefs);
        if (externalRefs.Count == 0)
        {
            return Array.Empty<AwOrderRecord>();
        }

        var refs = externalRefs;
        if (refs.Count > MaxRefsPerQuery)
        {
            _logger.LogWarning(
                "GetOrdersByExternalRefs recibió {Count} refs, truncando a {Max}.",
                refs.Count, MaxRefsPerQuery);
            refs = refs.Take(MaxRefsPerQuery).ToList();
        }

        using var activity = IntegracionesAwActivitySource.Instance.StartActivity(
            "HybridConnectionAwSqlReader.GetOrders");
        activity?.SetTag("aw.sql.refs_count", refs.Count);

        // Construye query con parámetros nombrados @p0, @p1, ... — evita
        // SQL injection y permite reutilizar plan en SQL Server.
        var paramNames = new string[refs.Count];
        for (var i = 0; i < refs.Count; i++) paramNames[i] = $"@p{i}";
        var inClause = string.Join(",", paramNames);

        var sql = $$"""
            SELECT {{AwExternalReferenceField}} AS quote_reference,
                   id AS aw_doc_id,
                   created_at AS created_at_aw
              FROM dbo.pool_auftrag
             WHERE {{AwExternalReferenceField}} IN ({{inClause}})
            """;

        var sw = Stopwatch.StartNew();
        DbDataAlias result;
        try
        {
            using var connection = _connectionFactory.CreateConnection();

            // Timeout duro para OpenAsync. Fix detectado en validación E2E
            // PR D: con SQL Server on-prem no-respondiente, OpenAsync se
            // colgaba indefinidamente aunque el connection string declarara
            // "Connection Timeout=10". El linked CTS aplica un cancel real
            // que el SDK Microsoft.Data.SqlClient sí respeta. Sin esto, el
            // AwCorrelationWorker quedaba en limbo silencioso hasta restart.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(_options.SqlConnectTimeoutSeconds));
            try
            {
                await connection.OpenAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (
                connectCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                _logger.LogWarning(
                    "AwSqlReader connect timeout. Configured {Timeout}s. duration_ms={DurationMs}",
                    _options.SqlConnectTimeoutSeconds, sw.ElapsedMilliseconds);
                throw new AwReaderException(
                    $"SQL connect timeout tras {_options.SqlConnectTimeoutSeconds}s " +
                    "(SQL Server on-prem o Hybrid Connection no respondieron a tiempo).",
                    kind: "connect_timeout", isTransient: true);
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = _options.SqlQueryTimeoutSeconds;
            for (var i = 0; i < refs.Count; i++)
            {
                command.Parameters.Add(new SqlParameter(paramNames[i], SqlDbType.NVarChar, 50)
                {
                    Value = refs[i],
                });
            }

            var rows = new List<AwOrderRecord>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new AwOrderRecord(
                    QuoteReference: reader.GetString(0),
                    AwDocId: reader.GetInt64(1),
                    CreatedAtAw: new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero)));
            }
            result = new DbDataAlias(rows, sw.ElapsedMilliseconds);
        }
        catch (SqlException ex) when (IsAuthFailure(ex))
        {
            sw.Stop();
            _logger.LogError(ex,
                "AwSqlReader auth failure. SQL state code {Number}. duration_ms={DurationMs}",
                ex.Number, sw.ElapsedMilliseconds);
            throw new AwReaderException(
                $"SQL Server auth failed (#{ex.Number}): {ex.Message}",
                kind: "auth", isTransient: false, inner: ex);
        }
        catch (SqlException ex) when (IsTimeout(ex))
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "AwSqlReader timeout. SQL Number={Number} duration_ms={DurationMs}",
                ex.Number, sw.ElapsedMilliseconds);
            throw new AwReaderException(
                $"SQL query timeout ({_options.SqlQueryTimeoutSeconds}s).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (SqlException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "AwSqlReader connection error. SQL Number={Number} duration_ms={DurationMs}",
                ex.Number, sw.ElapsedMilliseconds);
            throw new AwReaderException(
                $"SQL Server error (#{ex.Number}): {ex.Message}",
                kind: "connection", isTransient: true, inner: ex);
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "AwSqlReader connection state error. duration_ms={DurationMs}",
                sw.ElapsedMilliseconds);
            throw new AwReaderException(
                $"SQL connection state error: {ex.Message}",
                kind: "connection", isTransient: true, inner: ex);
        }
        sw.Stop();

        activity?.SetTag("aw.sql.matches_count", result.Rows.Count);
        activity?.SetTag("aw.sql.duration_ms", result.DurationMs);
        _logger.LogInformation(
            "AwSqlReader OK. refs_in={RefsIn} matches={Matches} duration_ms={DurationMs}",
            refs.Count, result.Rows.Count, result.DurationMs);
        return result.Rows;
    }

    /// <summary>
    /// SQL Server error number 18456 = "Login failed for user". Auth
    /// permanente — no recuperable por reintentos.
    /// </summary>
    private static bool IsAuthFailure(SqlException ex) => ex.Number == 18456;

    /// <summary>
    /// SQL Server error numbers asociados a timeout: -2 (client timeout
    /// from CommandTimeout), 11 (general network), 121 (semaphore timeout).
    /// </summary>
    private static bool IsTimeout(SqlException ex) =>
        ex.Number is -2 or 11 or 121;

    private sealed record DbDataAlias(IReadOnlyList<AwOrderRecord> Rows, long DurationMs);
}
