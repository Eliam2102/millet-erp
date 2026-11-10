using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

/// <summary>
/// Pruebas unitarias para <see cref="PinturasCatalogosAdapter"/>.
/// Verifica publicación idempotente para SQL Server y Postgres, recorte de longitudes,
/// mapeo de nulos a DBNull, transaccionalidad, rollback ante fallos y uso de IClock.
/// </summary>
public sealed class PinturasCatalogosAdapterTests
{
    private static readonly DateTimeOffset HoraFija = new(2026, 11, 8, 14, 30, 0, TimeSpan.Zero);

    private static PinturasCatalogosBatch CrearBatchEjemplo()
    {
        return new PinturasCatalogosBatch(
            Clientes:
            [
                new FilaPinturasCliente(
                    Clave: 1001,
                    RazonSocial: " Millet Vidrios SA de CV ",
                    NombreComercial: " Millet ",
                    Contacto: " Juan Perez ",
                    Telefono: " 9991234567 ",
                    Celular: " 9997654321 ",
                    Email: " ventas@millet.com.mx ",
                    Activo: true)
            ],
            Servicios:
            [
                new FilaPinturasServicio(
                    Clave: " SER95224 ",
                    Nombre: " Recubrimiento Blanco Brillante ",
                    Activo: true)
            ],
            Obras:
            [
                new FilaPinturasObra(
                    Clave: " OBRA-100 ",
                    Nombre: " Torre Norte ",
                    Origen: FilaPinturasObra.OrigenAw,
                    ReferenciaAw: " 5544 ",
                    Activo: true)
            ],
            Empleados:
            [
                new FilaPinturasEmpleado(
                    Clave: " EMP-01 ",
                    Nombre: " Carlos Ramirez ",
                    Activo: true)
            ]);
    }

    [Fact]
    public async Task Batch_vacio_retorna_vacio_sin_abrir_conexion()
    {
        var clock = new TestClock(HoraFija);
        var fabrica = new FakeConnectionFactory { DebeFallarAlCrear = true };
        var adapter = new PinturasCatalogosAdapter(
            fabrica,
            postgres: false,
            Options.Create(new PinturasCatalogosOptions()),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);

        var resultado = await adapter.PublicarAsync(PinturasCatalogosBatch.Empty, CancellationToken.None);

        resultado.TotalPublicados.Should().Be(0);
        resultado.ClientesPublicados.Should().Be(0);
        resultado.ServiciosPublicados.Should().Be(0);
        resultado.ObrasPublicadas.Should().Be(0);
        resultado.EmpleadosPublicados.Should().Be(0);
        resultado.PublicadoAt.Should().Be(HoraFija);
        fabrica.ConteoCreaciones.Should().Be(0);
        fabrica.Conexion.ConteoAperturas.Should().Be(0);
    }

    [Fact]
    public async Task Genera_comandos_y_parametros_correctos_para_sql_server()
    {
        var clock = new TestClock(HoraFija);
        var fabrica = new FakeConnectionFactory();
        var adapter = new PinturasCatalogosAdapter(
            fabrica,
            postgres: false,
            Options.Create(new PinturasCatalogosOptions()),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);

        var batch = CrearBatchEjemplo();
        var resultado = await adapter.PublicarAsync(batch, CancellationToken.None);

        resultado.TotalPublicados.Should().Be(4);
        resultado.PublicadoAt.Should().Be(HoraFija);

        var comandos = fabrica.Conexion.ComandosEjecutados;
        comandos.Should().HaveCount(4);

        // Cliente
        var cmdCliente = comandos[0];
        cmdCliente.CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertCliente(false));
        cmdCliente.Parametro("@clave").Value.Should().Be(1001);
        cmdCliente.Parametro("@razon_social").Value.Should().Be("Millet Vidrios SA de CV");
        cmdCliente.Parametro("@nombre_comercial").Value.Should().Be("Millet");
        cmdCliente.Parametro("@contacto").Value.Should().Be("Juan Perez");
        cmdCliente.Parametro("@telefono").Value.Should().Be("9991234567");
        cmdCliente.Parametro("@celular").Value.Should().Be("9997654321");
        cmdCliente.Parametro("@email").Value.Should().Be("ventas@millet.com.mx");
        cmdCliente.Parametro("@activo").Value.Should().Be(true);
        cmdCliente.Parametro("@updated_at").Value.Should().Be(HoraFija.UtcDateTime);

        // Servicio
        var cmdServicio = comandos[1];
        cmdServicio.CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertServicio(false));
        cmdServicio.Parametro("@clave").Value.Should().Be("SER95224");
        cmdServicio.Parametro("@nombre").Value.Should().Be("Recubrimiento Blanco Brillante");
        cmdServicio.Parametro("@activo").Value.Should().Be(true);
        cmdServicio.Parametro("@updated_at").Value.Should().Be(HoraFija.UtcDateTime);

        // Obra
        var cmdObra = comandos[2];
        cmdObra.CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertObra(false));
        cmdObra.Parametro("@clave").Value.Should().Be("OBRA-100");
        cmdObra.Parametro("@nombre").Value.Should().Be("Torre Norte");
        cmdObra.Parametro("@origen").Value.Should().Be("AW");
        cmdObra.Parametro("@referencia_aw").Value.Should().Be("5544");
        cmdObra.Parametro("@activo").Value.Should().Be(true);
        cmdObra.Parametro("@updated_at").Value.Should().Be(HoraFija.UtcDateTime);

        // Empleado
        var cmdEmpleado = comandos[3];
        cmdEmpleado.CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertEmpleado(false));
        cmdEmpleado.Parametro("@clave").Value.Should().Be("EMP-01");
        cmdEmpleado.Parametro("@nombre").Value.Should().Be("Carlos Ramirez");
        cmdEmpleado.Parametro("@activo").Value.Should().Be(true);
        cmdEmpleado.Parametro("@updated_at").Value.Should().Be(HoraFija.UtcDateTime);

        // Transacción verificada
        fabrica.Conexion.TransaccionActiva.Should().NotBeNull();
        fabrica.Conexion.TransaccionActiva!.Committed.Should().BeTrue();
        fabrica.Conexion.TransaccionActiva.RolledBack.Should().BeFalse();
        comandos.Should().OnlyContain(c => c.TransactionUsed == fabrica.Conexion.TransaccionActiva);
    }

    [Fact]
    public async Task Genera_comandos_y_parametros_correctos_para_postgresql()
    {
        var clock = new TestClock(HoraFija);
        var fabrica = new FakeConnectionFactory();
        var adapter = new PinturasCatalogosAdapter(
            fabrica,
            postgres: true,
            Options.Create(new PinturasCatalogosOptions()),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);

        var batch = CrearBatchEjemplo();
        var resultado = await adapter.PublicarAsync(batch, CancellationToken.None);

        resultado.TotalPublicados.Should().Be(4);

        var comandos = fabrica.Conexion.ComandosEjecutados;
        comandos.Should().HaveCount(4);

        // Cliente Postgres
        comandos[0].CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertCliente(true));
        comandos[0].CommandText.Should().Contain("INSERT INTO dbo.pinturas_cliente")
            .And.Contain("ON CONFLICT (clave) DO UPDATE SET");

        // Servicio Postgres
        comandos[1].CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertServicio(true));
        comandos[1].CommandText.Should().Contain("INSERT INTO dbo.pinturas_servicio")
            .And.Contain("ON CONFLICT (clave) DO UPDATE SET");

        // Obra Postgres
        comandos[2].CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertObra(true));
        comandos[2].CommandText.Should().Contain("INSERT INTO dbo.pinturas_obra")
            .And.Contain("ON CONFLICT (clave) DO UPDATE SET");

        // Empleado Postgres
        comandos[3].CommandText.Should().Be(PinturasCatalogosAdapter.SqlUpsertEmpleado(true));
        comandos[3].CommandText.Should().Contain("INSERT INTO dbo.pinturas_empleado")
            .And.Contain("ON CONFLICT (clave) DO UPDATE SET");

        fabrica.Conexion.TransaccionActiva!.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Maneja_valores_null_y_espacios_en_blanco_como_DBNull()
    {
        var clock = new TestClock(HoraFija);
        var fabrica = new FakeConnectionFactory();
        var adapter = new PinturasCatalogosAdapter(
            fabrica,
            postgres: false,
            Options.Create(new PinturasCatalogosOptions()),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);

        var batch = new PinturasCatalogosBatch(
            Clientes:
            [
                new FilaPinturasCliente(
                    Clave: 2002,
                    RazonSocial: "Cliente Sin Opcionales",
                    NombreComercial: null,
                    Contacto: "   ",
                    Telefono: null,
                    Celular: "",
                    Email: "   ",
                    Activo: false)
            ],
            Servicios: [],
            Obras:
            [
                new FilaPinturasObra(
                    Clave: "OBRA-MANUAL",
                    Nombre: "Obra Manual",
                    Origen: FilaPinturasObra.OrigenPinturas,
                    ReferenciaAw: null,
                    Activo: true)
            ],
            Empleados: []);

        await adapter.PublicarAsync(batch, CancellationToken.None);

        var comandos = fabrica.Conexion.ComandosEjecutados;
        comandos.Should().HaveCount(2);

        var cmdCliente = comandos[0];
        cmdCliente.Parametro("@nombre_comercial").Value.Should().Be(DBNull.Value);
        cmdCliente.Parametro("@contacto").Value.Should().Be(DBNull.Value);
        cmdCliente.Parametro("@telefono").Value.Should().Be(DBNull.Value);
        cmdCliente.Parametro("@celular").Value.Should().Be(DBNull.Value);
        cmdCliente.Parametro("@email").Value.Should().Be(DBNull.Value);
        cmdCliente.Parametro("@activo").Value.Should().Be(false);

        var cmdObra = comandos[1];
        cmdObra.Parametro("@referencia_aw").Value.Should().Be(DBNull.Value);
        cmdObra.Parametro("@origen").Value.Should().Be("PINTURAS");
    }

    [Fact]
    public async Task Recorta_cadenas_que_exceden_limites_segun_script_16_v2()
    {
        var clock = new TestClock(HoraFija);
        var fabrica = new FakeConnectionFactory();
        var adapter = new PinturasCatalogosAdapter(
            fabrica,
            postgres: false,
            Options.Create(new PinturasCatalogosOptions()),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);

        var batch = new PinturasCatalogosBatch(
            Clientes:
            [
                new FilaPinturasCliente(
                    Clave: 3003,
                    RazonSocial: new string('R', 250),
                    NombreComercial: new string('N', 250),
                    Contacto: new string('C', 250),
                    Telefono: new string('T', 80),
                    Celular: new string('X', 80),
                    Email: new string('E', 250),
                    Activo: true)
            ],
            Servicios:
            [
                new FilaPinturasServicio(
                    Clave: new string('S', 80),
                    Nombre: new string('D', 250),
                    Activo: true)
            ],
            Obras:
            [
                new FilaPinturasObra(
                    Clave: new string('O', 80),
                    Nombre: new string('B', 250),
                    Origen: FilaPinturasObra.OrigenAw,
                    ReferenciaAw: new string('A', 80),
                    Activo: true)
            ],
            Empleados:
            [
                new FilaPinturasEmpleado(
                    Clave: new string('E', 80),
                    Nombre: new string('M', 250),
                    Activo: true)
            ]);

        await adapter.PublicarAsync(batch, CancellationToken.None);

        var comandos = fabrica.Conexion.ComandosEjecutados;
        comandos.Should().HaveCount(4);

        var c = comandos[0];
        ((string)c.Parametro("@razon_social").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoRazonSocial);
        ((string)c.Parametro("@nombre_comercial").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoNombreComercial);
        ((string)c.Parametro("@contacto").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoContacto);
        ((string)c.Parametro("@telefono").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoTelefono);
        ((string)c.Parametro("@celular").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoCelular);
        ((string)c.Parametro("@email").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoEmail);

        var s = comandos[1];
        ((string)s.Parametro("@clave").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoServicioClave);
        ((string)s.Parametro("@nombre").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoServicioNombre);

        var o = comandos[2];
        ((string)o.Parametro("@clave").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoObraClave);
        ((string)o.Parametro("@nombre").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoObraNombre);
        ((string)o.Parametro("@referencia_aw").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoObraReferenciaAw);

        var e = comandos[3];
        ((string)e.Parametro("@clave").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoEmpleadoClave);
        ((string)e.Parametro("@nombre").Value!).Length.Should().Be(PinturasCatalogosAdapter.LargoEmpleadoNombre);
    }

    [Fact]
    public async Task Transaccionalidad_ejecuta_rollback_ante_fallos()
    {
        var clock = new TestClock(HoraFija);
        var fabrica = new FakeConnectionFactory();
        fabrica.Conexion.FallaEnComando = new InvalidOperationException("Falla simulada en base de datos al ejecutar comando.");

        var adapter = new PinturasCatalogosAdapter(
            fabrica,
            postgres: false,
            Options.Create(new PinturasCatalogosOptions()),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);

        var batch = CrearBatchEjemplo();

        var act = async () => await adapter.PublicarAsync(batch, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Falla simulada en base de datos al ejecutar comando.");

        fabrica.Conexion.TransaccionActiva.Should().NotBeNull();
        fabrica.Conexion.TransaccionActiva!.RolledBack.Should().BeTrue();
        fabrica.Conexion.TransaccionActiva.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task Cuenta_correctamente_elementos_y_asigna_fecha_de_IClock()
    {
        var clock = new TestClock(HoraFija);
        var fabrica = new FakeConnectionFactory();
        var adapter = new PinturasCatalogosAdapter(
            fabrica,
            postgres: false,
            Options.Create(new PinturasCatalogosOptions()),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);

        var batch = new PinturasCatalogosBatch(
            Clientes:
            [
                new FilaPinturasCliente(1, "Cliente 1"),
                new FilaPinturasCliente(2, "Cliente 2")
            ],
            Servicios:
            [
                new FilaPinturasServicio("S1", "Servicio 1"),
                new FilaPinturasServicio("S2", "Servicio 2"),
                new FilaPinturasServicio("S3", "Servicio 3")
            ],
            Obras:
            [
                new FilaPinturasObra("O1", "Obra 1")
            ],
            Empleados:
            [
                new FilaPinturasEmpleado("E1", "Empleado 1"),
                new FilaPinturasEmpleado("E2", "Empleado 2"),
                new FilaPinturasEmpleado("E3", "Empleado 3"),
                new FilaPinturasEmpleado("E4", "Empleado 4")
            ]);

        var resultado = await adapter.PublicarAsync(batch, CancellationToken.None);

        resultado.ClientesPublicados.Should().Be(2);
        resultado.ServiciosPublicados.Should().Be(3);
        resultado.ObrasPublicadas.Should().Be(1);
        resultado.EmpleadosPublicados.Should().Be(4);
        resultado.TotalPublicados.Should().Be(10);
        resultado.PublicadoAt.Should().Be(HoraFija);
        fabrica.Conexion.ComandosEjecutados.Should().HaveCount(10);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("hola", "hola")]
    [InlineData("  abc  ", "abc")]
    [InlineData("  espacios  ", "espac")]
    [InlineData("1234567890", "12345")]
    public void RecortarOpcional_comportamiento_esperado(string? entrada, string? esperado)
    {
        PinturasCatalogosAdapter.RecortarOpcional(entrada, 5).Should().Be(esperado);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("hola", "hola")]
    [InlineData("  abc  ", "abc")]
    [InlineData("  espacios  ", "espac")]
    [InlineData("1234567890", "12345")]
    public void RecortarRequerido_comportamiento_esperado(string? entrada, string esperado)
    {
        PinturasCatalogosAdapter.RecortarRequerido(entrada, 5).Should().Be(esperado);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sql_estatico_cumple_esquema_y_tablas(bool postgres)
    {
        PinturasCatalogosAdapter.SqlUpsertCliente(postgres).Should().Contain("dbo.pinturas_cliente");
        PinturasCatalogosAdapter.SqlUpsertServicio(postgres).Should().Contain("dbo.pinturas_servicio");
        PinturasCatalogosAdapter.SqlUpsertObra(postgres).Should().Contain("dbo.pinturas_obra");
        PinturasCatalogosAdapter.SqlUpsertEmpleado(postgres).Should().Contain("dbo.pinturas_empleado");

        if (!postgres)
        {
            PinturasCatalogosAdapter.SqlUpsertCliente(false).Should().Contain("IF @@ROWCOUNT = 0 BEGIN INSERT INTO dbo.pinturas_cliente");
            PinturasCatalogosAdapter.SqlUpsertServicio(false).Should().Contain("IF @@ROWCOUNT = 0 BEGIN INSERT INTO dbo.pinturas_servicio");
            PinturasCatalogosAdapter.SqlUpsertObra(false).Should().Contain("IF @@ROWCOUNT = 0 BEGIN INSERT INTO dbo.pinturas_obra");
            PinturasCatalogosAdapter.SqlUpsertEmpleado(false).Should().Contain("IF @@ROWCOUNT = 0 BEGIN INSERT INTO dbo.pinturas_empleado");
        }
        else
        {
            PinturasCatalogosAdapter.SqlUpsertCliente(true).Should().Contain("ON CONFLICT (clave) DO UPDATE SET");
            PinturasCatalogosAdapter.SqlUpsertServicio(true).Should().Contain("ON CONFLICT (clave) DO UPDATE SET");
            PinturasCatalogosAdapter.SqlUpsertObra(true).Should().Contain("ON CONFLICT (clave) DO UPDATE SET");
            PinturasCatalogosAdapter.SqlUpsertEmpleado(true).Should().Contain("ON CONFLICT (clave) DO UPDATE SET");
        }
    }

    // --- Clases Fake ADO.NET para pruebas deterministas sin BD real ---

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = string.Empty;
        [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => _items.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => throw new NotSupportedException();
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;

        public FakeParameter? Obtener(string nombre) => _items.OfType<FakeParameter>().FirstOrDefault(p => p.ParameterName == nombre);
    }

    private sealed class FakeCommand : DbCommand
    {
        private readonly FakeConnection _connection;
        public FakeCommand(FakeConnection connection) => _connection = connection;

        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get => _connection; set { } }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }
        public DbTransaction? TransactionUsed => DbTransaction;

        public FakeParameter Parametro(string nombre) =>
            ((FakeParameterCollection)DbParameterCollection).Obtener(nombre)
            ?? throw new KeyNotFoundException($"Parámetro {nombre} no encontrado");

        public override void Cancel() { }
        public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();
        public override object? ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            if (_connection.FallaEnComando is not null)
            {
                throw _connection.FallaEnComando;
            }

            _connection.ComandosEjecutados.Add(this);
            return Task.FromResult(1);
        }
    }

    private sealed class FakeTransaction : DbTransaction
    {
        private readonly FakeConnection _connection;
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }

        public FakeTransaction(FakeConnection connection) => _connection = connection;

        protected override DbConnection DbConnection => _connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit() => Committed = true;
        public override Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public override void Rollback() => RolledBack = true;
        public override Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RolledBack = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConnection : DbConnection
    {
        public bool Abierta { get; private set; }
        public int ConteoAperturas { get; private set; }
        public Exception? FallaEnComando { get; set; }
        public List<FakeCommand> ComandosEjecutados { get; } = [];
        public FakeTransaction? TransaccionActiva { get; private set; }

        [AllowNull] public override string ConnectionString { get; set; } = "Server=fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "fake";
        public override ConnectionState State => Abierta ? ConnectionState.Open : ConnectionState.Closed;

        public override void Open() { Abierta = true; ConteoAperturas++; }
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Abierta = true;
            ConteoAperturas++;
            return Task.CompletedTask;
        }

        public override void Close() => Abierta = false;
        public override void ChangeDatabase(string databaseName) { }

        protected override DbCommand CreateDbCommand() => new FakeCommand(this);

        protected override DbTransaction BeginDbTransaction(IsolationLevel il)
        {
            TransaccionActiva = new FakeTransaction(this);
            return TransaccionActiva;
        }

        protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel il, CancellationToken cancellationToken)
        {
            TransaccionActiva = new FakeTransaction(this);
            return ValueTask.FromResult<DbTransaction>(TransaccionActiva);
        }
    }

    private sealed class FakeConnectionFactory : IIntegracionSqlConnectionFactory
    {
        public FakeConnection Conexion { get; } = new();
        public int ConteoCreaciones { get; private set; }
        public bool DebeFallarAlCrear { get; set; }

        public DbConnection CreateConnection()
        {
            ConteoCreaciones++;
            if (DebeFallarAlCrear)
            {
                throw new InvalidOperationException("No se debía llamar a CreateConnection");
            }
            return Conexion;
        }
    }
}
