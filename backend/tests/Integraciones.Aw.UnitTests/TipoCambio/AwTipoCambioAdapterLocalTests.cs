using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.Compartido.Domain.Ports;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.TipoCambio;

namespace Millet.Integraciones.Aw.UnitTests.TipoCambio;

public sealed class AwTipoCambioAdapterLocalTests
{
    [Fact]
    public void Constantes_de_candados_y_queries_son_correctas()
    {
        AwTipoCambioAdapter.CandadoSqlServer.Should().Be(
            "EXEC sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000");

        AwTipoCambioAdapter.CandadoPostgres.Should().Be(
            "SELECT pg_advisory_xact_lock(hashtextextended(@recurso, 0))");

        AwTipoCambioAdapter.RecursoCandado("USD").Should().Be("erp_tipo_cambio:USD");
        AwTipoCambioAdapter.RecursoCandado(" eur ").Should().Be("erp_tipo_cambio:EUR");
        AwTipoCambioAdapter.RecursoCandado("CAD").Should().Be("erp_tipo_cambio:CAD");

        AwTipoCambioAdapter.SqlUltimaVersion.Should().Contain("dbo.erp_tipo_cambio");
        AwTipoCambioAdapter.SqlUltimaVersion.Should().Contain("@moneda");
        AwTipoCambioAdapter.SqlUltimaVersion.Should().Contain("@fecha");
        AwTipoCambioAdapter.SqlUltimaVersion.Should().Contain("MAX(x.version)");

        AwTipoCambioAdapter.SqlInsertar.Should().Contain("INSERT INTO dbo.erp_tipo_cambio");
        AwTipoCambioAdapter.SqlInsertar.Should().Contain("moneda, fecha_aplicacion, version, valor, fecha_fix, origen");

        AwTipoCambioAdapter.SqlAcuses.Should().Contain("SELECT r.moneda, r.fecha_aplicacion, r.version, r.valor, r.aplicada_at, r.resultado, r.motivo, r.intentos");
        AwTipoCambioAdapter.SqlAcuses.Should().Contain("dbo.erp_tipo_cambio r");
        AwTipoCambioAdapter.SqlAcuses.Should().Contain("@desde");

        AwTipoCambioAdapter.SqlTipoCambioVigenteAw.Should().Contain("sysadm.ka_waehrungen");
        AwTipoCambioAdapter.SqlTipoCambioVigenteAw.Should().Contain("UPPER(fremd_key) = @moneda OR UPPER(waehrung) = @moneda");
    }

    [Fact]
    public async Task PublicarFilaAsync_sin_version_previa_inserta_version_1_y_adquiere_candado()
    {
        var fakeConn = new FakeDbConnection();
        // Cuando consulta la última versión, no devuelve filas (tabla vacía para esa fecha)
        fakeConn.OnExecuteReader = cmd =>
        {
            if (cmd.CommandText == AwTipoCambioAdapter.SqlUltimaVersion)
            {
                return new FakeDbDataReader([]);
            }
            return new FakeDbDataReader([]);
        };

        var adapter = new AwTipoCambioAdapter(
            new FakeFabrica(fakeConn),
            Options.Create(new AwPedidosOptions()),
            AwTipoCambioAdapter.CandadoSqlServer);

        var fila = new AwTipoCambioFila("USD", new DateOnly(2026, 11, 8), 1, 20.1500m, null, "Banxico");
        var version = await adapter.PublicarFilaAsync(fila);

        version.Should().Be(1);

        // Verifica que se ejecutaron: candado, consulta de versión, e INSERT
        fakeConn.ExecutedCommands.Should().HaveCount(3);

        var cmdCandado = fakeConn.ExecutedCommands[0];
        cmdCandado.CommandText.Should().Be(AwTipoCambioAdapter.CandadoSqlServer);
        cmdCandado.Parameters["@recurso"].Value.Should().Be("erp_tipo_cambio:USD");

        var cmdConsulta = fakeConn.ExecutedCommands[1];
        cmdConsulta.CommandText.Should().Be(AwTipoCambioAdapter.SqlUltimaVersion);
        cmdConsulta.Parameters["@moneda"].Value.Should().Be("USD");

        var cmdInsert = fakeConn.ExecutedCommands[2];
        cmdInsert.CommandText.Should().Be(AwTipoCambioAdapter.SqlInsertar);
        cmdInsert.Parameters["@moneda"].Value.Should().Be("USD");
        cmdInsert.Parameters["@version"].Value.Should().Be(1);
        cmdInsert.Parameters["@valor"].Value.Should().Be(20.1500m);
        cmdInsert.Parameters["@origen"].Value.Should().Be("Banxico");
    }

    [Fact]
    public async Task PublicarFilaAsync_con_mismo_valor_omite_insercion_y_retorna_null()
    {
        var fakeConn = new FakeDbConnection();
        // Simular que existe versión 2 con valor 20.1500m
        fakeConn.OnExecuteReader = cmd =>
        {
            if (cmd.CommandText == AwTipoCambioAdapter.SqlUltimaVersion)
            {
                return new FakeDbDataReader([
                    new Dictionary<string, object> { ["version"] = 2, ["valor"] = 20.1500m }
                ]);
            }
            return new FakeDbDataReader([]);
        };

        var adapter = new AwTipoCambioAdapter(
            new FakeFabrica(fakeConn),
            Options.Create(new AwPedidosOptions()),
            AwTipoCambioAdapter.CandadoSqlServer);

        var fila = new AwTipoCambioFila("USD", new DateOnly(2026, 11, 8), 1, 20.1500m, null, "Banxico");
        var version = await adapter.PublicarFilaAsync(fila);

        version.Should().BeNull();

        // Candado y consulta, pero NINGÚN INSERT ejecutado
        fakeConn.ExecutedCommands.Should().HaveCount(2);
        fakeConn.ExecutedCommands.Any(c => c.CommandText == AwTipoCambioAdapter.SqlInsertar).Should().BeFalse();
    }

    [Fact]
    public async Task PublicarFilaAsync_con_distinto_valor_inserta_version_siguiente()
    {
        var fakeConn = new FakeDbConnection();
        // Simular que existe versión 2 con valor 20.1500m
        fakeConn.OnExecuteReader = cmd =>
        {
            if (cmd.CommandText == AwTipoCambioAdapter.SqlUltimaVersion)
            {
                return new FakeDbDataReader([
                    new Dictionary<string, object> { ["version"] = 2, ["valor"] = 20.1500m }
                ]);
            }
            return new FakeDbDataReader([]);
        };

        var adapter = new AwTipoCambioAdapter(
            new FakeFabrica(fakeConn),
            Options.Create(new AwPedidosOptions()),
            AwTipoCambioAdapter.CandadoSqlServer);

        // Nuevo valor: 20.2500m
        var fila = new AwTipoCambioFila("USD", new DateOnly(2026, 11, 8), 1, 20.2500m, new DateOnly(2026, 11, 7), "Manual");
        var version = await adapter.PublicarFilaAsync(fila);

        version.Should().Be(3);

        fakeConn.ExecutedCommands.Should().HaveCount(3);
        var cmdInsert = fakeConn.ExecutedCommands[2];
        cmdInsert.CommandText.Should().Be(AwTipoCambioAdapter.SqlInsertar);
        cmdInsert.Parameters["@version"].Value.Should().Be(3);
        cmdInsert.Parameters["@valor"].Value.Should().Be(20.2500m);
        cmdInsert.Parameters["@origen"].Value.Should().Be("Manual");
        cmdInsert.Parameters["@fechaFix"].Value.Should().Be(new DateTime(2026, 11, 7));
    }

    [Fact]
    public async Task ObtenerUltimosAcusesAsync_lee_y_filtra_por_moneda()
    {
        var fakeConn = new FakeDbConnection();
        fakeConn.OnExecuteReader = cmd =>
        {
            if (cmd.CommandText == AwTipoCambioAdapter.SqlAcuses)
            {
                return new FakeDbDataReader([
                    new Dictionary<string, object>
                    {
                        ["moneda"] = "USD",
                        ["fecha_aplicacion"] = new DateTime(2026, 11, 8),
                        ["version"] = 1,
                        ["valor"] = 20.10m,
                        ["aplicada_at"] = new DateTimeOffset(2026, 11, 8, 8, 30, 0, TimeSpan.Zero),
                        ["resultado"] = (byte)1,
                        ["motivo"] = DBNull.Value,
                        ["intentos"] = 1
                    },
                    new Dictionary<string, object>
                    {
                        ["moneda"] = "EUR",
                        ["fecha_aplicacion"] = new DateTime(2026, 11, 8),
                        ["version"] = 1,
                        ["valor"] = 21.80m,
                        ["aplicada_at"] = DBNull.Value,
                        ["resultado"] = DBNull.Value,
                        ["motivo"] = DBNull.Value,
                        ["intentos"] = 0
                    },
                    new Dictionary<string, object>
                    {
                        ["moneda"] = "JPY",
                        ["fecha_aplicacion"] = new DateTime(2026, 11, 8),
                        ["version"] = 1,
                        ["valor"] = 0.13m,
                        ["aplicada_at"] = DBNull.Value,
                        ["resultado"] = DBNull.Value,
                        ["motivo"] = DBNull.Value,
                        ["intentos"] = 0
                    }
                ]);
            }
            return new FakeDbDataReader([]);
        };

        var adapter = new AwTipoCambioAdapter(
            new FakeFabrica(fakeConn),
            Options.Create(new AwPedidosOptions()),
            AwTipoCambioAdapter.CandadoSqlServer);

        // Filtrando solo USD y EUR
        var acuses = await adapter.ObtenerUltimosAcusesAsync(["USD", "EUR"], new DateOnly(2026, 11, 1));

        acuses.Should().HaveCount(2);
        acuses[0].Moneda.Should().Be("USD");
        acuses[0].Resultado.Should().Be(1);
        acuses[0].Intentos.Should().Be(1);
        acuses[1].Moneda.Should().Be("EUR");
        acuses[1].Resultado.Should().BeNull();
    }

    [Fact]
    public async Task ObtenerTipoCambioVigenteAwAsync_ejecuta_scalar_con_moneda()
    {
        var fakeConn = new FakeDbConnection();
        fakeConn.OnExecuteScalar = cmd =>
        {
            if (cmd.CommandText == AwTipoCambioAdapter.SqlTipoCambioVigenteAw)
            {
                return 20.123456m;
            }
            return null;
        };

        var adapter = new AwTipoCambioAdapter(
            new FakeFabrica(fakeConn),
            Options.Create(new AwPedidosOptions()),
            AwTipoCambioAdapter.CandadoSqlServer);

        var valor = await adapter.ObtenerTipoCambioVigenteAwAsync("usd");

        valor.Should().Be(20.123456m);
        var cmd = fakeConn.ExecutedCommands.Single();
        cmd.CommandText.Should().Be(AwTipoCambioAdapter.SqlTipoCambioVigenteAw);
        cmd.Parameters["@moneda"].Value.Should().Be("USD");
    }

    [Fact]
    public void DependencyInjection_registra_adaptadores_real_demo_y_selectores()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AwIntegracionDb"] = "Server=localhost;Database=test;Trusted_Connection=True;",
            ["ConnectionStrings:AwOrigenPgDb"] = "Host=localhost;Database=demo;Username=demo",
        }).Build();

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddIntegracionesAwPedidosAdapters(config);
        services.AddIntegracionesAwOrigen(config);
        services.AddScoped<IAwOrigenActivo, FakeOrigenActivo>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        // 1. Unkeyed resuelve AwOrigenSelectores
        var portUnkeyed = scope.ServiceProvider.GetRequiredService<IAwTipoCambioPort>();
        portUnkeyed.Should().BeOfType<AwOrigenSelectores>();

        // 2. Keyed "Real" resuelve AwTipoCambioAdapter con candado SqlServer
        var portReal = scope.ServiceProvider.GetRequiredKeyedService<IAwTipoCambioPort>("Real");
        portReal.Should().BeOfType<AwTipoCambioAdapter>();

        // 3. Keyed "Demo" resuelve AwTipoCambioAdapter con candado Postgres
        var portDemo = scope.ServiceProvider.GetRequiredKeyedService<IAwTipoCambioPort>("Demo");
        portDemo.Should().BeOfType<AwTipoCambioAdapter>();
    }

    // ================================= Fake ADO.NET Infrastructure =================================

    private sealed class FakeOrigenActivo : IAwOrigenActivo
    {
        public Task<AwOrigenEstado> LeerAsync(CancellationToken ct) => Task.FromResult(
            new AwOrigenEstado("Real", false, false, "Sql", "Simulado", null, null, 1));
    }

    private sealed class FakeFabrica(FakeDbConnection conn) : IIntegracionSqlConnectionFactory
    {
        public DbConnection CreateConnection() => conn;
    }

    private sealed class FakeDbConnection : DbConnection
    {
        public List<FakeDbCommand> ExecutedCommands { get; } = [];
        public Func<FakeDbCommand, DbDataReader>? OnExecuteReader { get; set; }
        public Func<FakeDbCommand, object?>? OnExecuteScalar { get; set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = "FakeConnectionString";
        public override string Database => "FakeDb";
        public override string DataSource => "FakeDataSource";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new FakeDbTransaction(this);

        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);
    }

    private sealed class FakeDbTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class FakeDbCommand(FakeDbConnection conn) : DbCommand
    {
        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = conn;
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery()
        {
            conn.ExecutedCommands.Add(this);
            return 1;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            conn.ExecutedCommands.Add(this);
            return Task.FromResult(1);
        }

        public override object? ExecuteScalar()
        {
            conn.ExecutedCommands.Add(this);
            return conn.OnExecuteScalar?.Invoke(this);
        }

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            conn.ExecutedCommands.Add(this);
            return Task.FromResult(conn.OnExecuteScalar?.Invoke(this));
        }

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            conn.ExecutedCommands.Add(this);
            return conn.OnExecuteReader?.Invoke(this) ?? new FakeDbDataReader([]);
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            conn.ExecutedCommands.Add(this);
            return Task.FromResult(conn.OnExecuteReader?.Invoke(this) ?? new FakeDbDataReader([]));
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = "";
        public override int Size { get; set; }
        [AllowNull]
        public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];
        public override int Count => _parameters.Count;
        public override object SyncRoot => this;
        public override int Add(object value) { _parameters.Add((DbParameter)value); return _parameters.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v); }
        public override void Clear() => _parameters.Clear();
        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
        public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _parameters.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _parameters.RemoveAll(p => p.ParameterName == parameterName);
        protected override DbParameter GetParameter(int index) => _parameters[index];
        protected override DbParameter GetParameter(string parameterName) => _parameters.First(p => p.ParameterName == parameterName);
        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var idx = IndexOf(parameterName);
            if (idx >= 0) _parameters[idx] = value;
            else _parameters.Add(value);
        }
    }

    private sealed class FakeDbDataReader(IReadOnlyList<IReadOnlyDictionary<string, object>> rows) : DbDataReader
    {
        private int _currentIndex = -1;
        private IReadOnlyDictionary<string, object>? CurrentRow =>
            _currentIndex >= 0 && _currentIndex < rows.Count ? rows[_currentIndex] : null;

        public override int FieldCount => CurrentRow?.Count ?? 0;
        public override bool HasRows => rows.Count > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => rows.Count;
        public override int Depth => 0;

        public override bool Read()
        {
            _currentIndex++;
            return _currentIndex < rows.Count;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => CurrentRow![name];

        public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal));
        public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal));
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));
        public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));
        public override Type GetFieldType(int ordinal) => GetValue(ordinal)?.GetType() ?? typeof(object);
        public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));
        public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));
        public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));
        public override string GetName(int ordinal) => CurrentRow!.Keys.ElementAt(ordinal);
        public override int GetOrdinal(string name) => CurrentRow!.Keys.ToList().IndexOf(name);
        public override string GetString(int ordinal) => Convert.ToString(GetValue(ordinal))!;
        public override object GetValue(int ordinal) => CurrentRow!.Values.ElementAt(ordinal);
        public override int GetValues(object[] values)
        {
            var count = Math.Min(values.Length, FieldCount);
            for (var i = 0; i < count; i++) values[i] = GetValue(i);
            return count;
        }

        public override bool IsDBNull(int ordinal)
        {
            var val = GetValue(ordinal);
            return val is null || val is DBNull;
        }

        public override bool NextResult() => false;
        public override System.Collections.IEnumerator GetEnumerator() => rows.GetEnumerator();
    }
}
