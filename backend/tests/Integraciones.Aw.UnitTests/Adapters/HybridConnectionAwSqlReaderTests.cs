using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.UnitTests.Adapters;

/// <summary>
/// Tests del clasificador de errores SQL del
/// <see cref="HybridConnectionAwSqlReader"/>. La SUT mockea
/// <see cref="ISqlConnectionFactory"/> para simular cada modo de fallo
/// sin necesidad de SQL Server real.
///
/// <para>
/// NOTA: <c>SqlException</c> no es construible directamente. Para los
/// tests de error path usamos <see cref="InvalidOperationException"/>
/// (no es SqlException pero el adapter atrapa
/// <c>InvalidOperationException</c> por separado para errores de
/// connection state — testea el path "transient"). Los tests de SQL
/// errores específicos (auth 18456, timeout -2) se cubren en
/// integration tests con SQL Server real (Aw.IntegrationTests futuro).
/// </para>
/// </summary>
public sealed class HybridConnectionAwSqlReaderTests
{
    private static readonly string[] SingleRef = ["Q-001"];

    [Fact]
    public async Task ListaVacia_RetornaArrayVacio()
    {
        var reader = BuildReader(new ThrowingFactory(new InvalidOperationException("not called")));

        var result = await reader.GetOrdersByExternalRefsAsync(
            Array.Empty<string>(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectionFactory_TiraInvalidOperation_LanzaTransient()
    {
        var reader = BuildReader(new ThrowingFactory(new InvalidOperationException("conn pool exhausted")));

        var ex = (await reader.Invoking(r => r.GetOrdersByExternalRefsAsync(
                SingleRef, CancellationToken.None))
            .Should().ThrowAsync<AwReaderException>()).Subject.First();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("connection");
    }

    [Fact]
    public async Task TruncaListaSiExcede1000Refs()
    {
        // Pasar >1000 refs y verificar que no tira "lista demasiado grande"
        // (acepta y trunca silenciosamente con log warning).
        var reader = BuildReader(new ThrowingFactory(new InvalidOperationException("expected")));

        var refs = Enumerable.Range(0, 1500).Select(i => $"Q-{i}").ToList();

        // Esperamos AwReaderException (porque la factory tira) — pero NO
        // ArgumentException por exceso de refs.
        await reader.Invoking(r => r.GetOrdersByExternalRefsAsync(refs, CancellationToken.None))
            .Should().ThrowAsync<AwReaderException>();
    }

    [Fact]
    public async Task OpenAsync_NoCompletaDentroDeTimeout_LanzaConnectTimeout()
    {
        // Bug detectado en validación E2E PR D: si SQL Server on-prem no
        // acepta la conexión TCP (HC down, SQL crash, firewall), el
        // SqlConnection.OpenAsync se cuelga indefinidamente aunque la
        // connection string declare "Connection Timeout=10". El fix
        // aplica un CancellationTokenSource.CancelAfter con
        // SqlConnectTimeoutSeconds que sí respeta el SDK.
        //
        // El test usa un fake connection cuyo OpenAsync nunca completa
        // (Task.Delay infinito con el cancellationToken pasado por el
        // adapter). Si el fix funciona, dentro del timeout el adapter
        // lanza AwReaderException(kind=connect_timeout, isTransient=true).
        var factory = new HangingFactory();
        var reader = BuildReader(factory, sqlConnectTimeoutSeconds: 1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = (await reader.Invoking(r => r.GetOrdersByExternalRefsAsync(
                SingleRef, CancellationToken.None))
            .Should().ThrowAsync<AwReaderException>()).Subject.First();
        sw.Stop();

        ex.IsTransient.Should().BeTrue();
        ex.Kind.Should().Be("connect_timeout");
        // Margen generoso (CTS + scheduler): timeout 1s + max 2s extra.
        sw.ElapsedMilliseconds.Should().BeLessThan(3500,
            "el CancellationTokenSource.CancelAfter debe cortar OpenAsync " +
            "antes del timeout default del SDK (~30s)");
    }

    // ─── Helpers ───

    private static HybridConnectionAwSqlReader BuildReader(
        ISqlConnectionFactory factory,
        int sqlConnectTimeoutSeconds = 15)
    {
        var options = Options.Create(new IntegracionesAwOptions
        {
            SqlQueryTimeoutSeconds = 5,
            SqlConnectTimeoutSeconds = sqlConnectTimeoutSeconds,
        });
        return new HybridConnectionAwSqlReader(
            factory, options, NullLogger<HybridConnectionAwSqlReader>.Instance);
    }

    private sealed class ThrowingFactory : ISqlConnectionFactory
    {
        private readonly Exception _exception;
        public ThrowingFactory(Exception exception) { _exception = exception; }
        public DbConnection CreateConnection() => throw _exception;
    }

    /// <summary>
    /// Factory que devuelve una conexión cuyo <c>OpenAsync</c> nunca
    /// completa hasta que el caller cancele el cancellationToken. Simula
    /// SQL Server no-respondiente.
    /// </summary>
    private sealed class HangingFactory : ISqlConnectionFactory
    {
        public DbConnection CreateConnection() => new HangingConnection();
    }

    private sealed class HangingConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "Server=fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "fake";
        public override ConnectionState State => ConnectionState.Closed;

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            // Espera infinita; solo respeta el token (que el adapter
            // debería cortar via CTS.CancelAfter).
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public override void Open() => throw new NotSupportedException();
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        protected override DbCommand CreateDbCommand() =>
            throw new NotSupportedException("OpenAsync nunca completa — no hay command.");
        protected override DbTransaction BeginDbTransaction(IsolationLevel il) =>
            throw new NotSupportedException();
    }
}
