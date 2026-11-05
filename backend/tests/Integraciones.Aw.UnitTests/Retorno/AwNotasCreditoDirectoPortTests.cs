using System.Data;
using System.Data.Common;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Infrastructure.NotasCredito;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

/// <summary>86e3jvv6z · NC de A+W leídas directo (simulador: conexión falsa con la forma real de las columnas). Datos ficticios.</summary>
public sealed class AwNotasCreditoDirectoPortTests
{
    private sealed class Sim(Func<DataTable> tabla, TimeSpan? demora = null)
    {
        public List<string> Comandos { get; } = [];
        public int Aperturas;
        public Func<DbConnection> Fabrica => () => new Conexion(this);
        public DataTable Tabla() => tabla();
        public TimeSpan? Demora => demora;
    }

    private sealed class Conexion(Sim sim) : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "sim";
        public override string DataSource => "sim";
        public override string ServerVersion => "sim";
        public override ConnectionState State => ConnectionState.Open;
        public override async Task OpenAsync(CancellationToken ct)
        {
            sim.Aperturas++;
            if (sim.Demora is { } d) await Task.Delay(d, ct);
        }
        public override void Open() => sim.Aperturas++;
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel il) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new Comando(sim);
    }

    private sealed class Comando(Sim sim) : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new Parametros();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new InvalidOperationException("el lector no escribe");
        public override object? ExecuteScalar() => throw new InvalidOperationException("el lector no escribe");
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new Parametro();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            sim.Comandos.Add(CommandText);
            return sim.Tabla().CreateDataReader();
        }
    }

    private sealed class Parametro : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ParameterName { get; set; } = "";
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string SourceColumn { get; set; } = "";
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class Parametros : DbParameterCollection
    {
        private readonly List<DbParameter> _l = [];
        public override int Count => _l.Count;
        public override object SyncRoot => _l;
        public override int Add(object value) { _l.Add((DbParameter)value); return _l.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
        public override void Clear() => _l.Clear();
        public override bool Contains(object value) => _l.Contains((DbParameter)value);
        public override bool Contains(string value) => _l.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => throw new NotSupportedException();
        public override System.Collections.IEnumerator GetEnumerator() => _l.GetEnumerator();
        public override int IndexOf(object value) => _l.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _l.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _l.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _l.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _l.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _l.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _l[index];
        protected override DbParameter GetParameter(string parameterName) => _l[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _l[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _l[IndexOf(parameterName)] = value;
    }

    private static DataTable Tabla(params object?[][] filas)
    {
        var t = new DataTable();
        t.Columns.Add("ID", typeof(int)); t.Columns.Add("AH_HAUPT_AUFTR", typeof(int)); t.Columns.Add("AH_IDENT", typeof(int));
        t.Columns.Add("FI_BETR_BRUTTO", typeof(decimal)); t.Columns.Add("DATUM", typeof(DateTime));
        t.Columns.Add("PED_FALZ", typeof(decimal)); t.Columns.Add("OR_BEARBEITER", typeof(string));
        t.Columns.Add("VORNAME", typeof(string)); t.Columns.Add("NAME", typeof(string));
        foreach (var f in filas) t.Rows.Add(f.Select(x => x ?? DBNull.Value).ToArray());
        return t;
    }

    private static readonly string[] Escritura = ["INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "DROP"];
    private static readonly AwNotasCreditoLecturaOptions On = new() { Habilitada = true, VentanaDias = 7, MaxPorLectura = 200 };
    private static readonly DateTime Dia = DateTime.Now.AddHours(-3);

    private static (AwNotasCreditoDirectoPort Puerto, Sim Sim, AwNotasCreditoVistas Vistas) Armar(Sim sim, AwNotasCreditoLecturaOptions? o = null)
    {
        var vistas = new AwNotasCreditoVistas();
        return (new AwNotasCreditoDirectoPort(new AwNotasCreditoLectorAdapter(sim.Fabrica, o ?? On), o ?? On, vistas), sim, vistas);
    }

    private static Sim Dos() => new(() => Tabla(
        [80024994, 80275798, 1203, 317.13m, Dia, 317.13m, "VPRUEBA", "Ana", "Prueba"],      // igual a la ranura del pedido
        [80024995, 80275799, 1204, 1500m, Dia, 0m, "VPRUEBA2", null, null]));                // nota comercial, vendedor sin ficha

    [Fact]
    public async Task Caso_feliz_la_lectura_arma_la_solicitud_con_ranura_vendedor_y_motivo()
    {
        var (puerto, sim, _) = Armar(Dos());

        var n = await puerto.LeerNotasPendientesAsync(50, default);

        n.Should().HaveCount(2);
        n[0].NumeroNota.Should().Be("80024994"); n[0].NumeroPedido.Should().Be("80275798"); n[0].ClienteRef.Should().Be("1203");
        n[0].Version.Should().Be(1); n[0].Importe.Should().Be(317.13m); n[0].Motivo.Should().Be("Bonificacion");
        n[0].EsRanura.Should().BeTrue(); n[0].Vendedor.Should().Be("Ana Prueba");
        n[1].EsRanura.Should().BeFalse(); n[1].Vendedor.Should().Be("VPRUEBA2");
        sim.Comandos.Should().ContainSingle();
    }

    [Fact]
    public async Task No_escribe_nada_en_A_W_todo_comando_es_SELECT_y_el_acuse_no_abre_conexion()
    {
        var (puerto, sim, _) = Armar(Dos());
        var n = await puerto.LeerNotasPendientesAsync(50, default);
        var aperturas = sim.Aperturas;

        foreach (var s in n)
            await puerto.EscribirAcuseAsync(new AcuseNotaCreditoAw(s.SolicitudId, Guid.NewGuid(), "PorAutorizar", ResultadoSolicitudAw.Aplicada, null), default);
        await puerto.EscribirAcuseAsync(new AcuseNotaCreditoAw(n[0].SolicitudId, null, null, ResultadoSolicitudAw.Rechazada, "x"), default);

        sim.Aperturas.Should().Be(aperturas);
        sim.Comandos.Should().OnlyContain(c => c.TrimStart().StartsWith("SELECT", StringComparison.Ordinal));
        sim.Comandos.Should().NotContain(c => Escritura.Any(w => c.Contains(w, StringComparison.OrdinalIgnoreCase)));
        sim.Comandos.Should().NotContain(c => c.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Duplicado_una_nota_aplicada_no_se_vuelve_a_ofrecer_y_una_rechazada_si()
    {
        var (puerto, _, _) = Armar(Dos());
        var n = await puerto.LeerNotasPendientesAsync(50, default);
        await puerto.EscribirAcuseAsync(new AcuseNotaCreditoAw(n[0].SolicitudId, null, null, ResultadoSolicitudAw.Aplicada, "ya cubierta"), default);
        await puerto.EscribirAcuseAsync(new AcuseNotaCreditoAw(n[1].SolicitudId, null, null, ResultadoSolicitudAw.Rechazada, "sin factura"), default);

        var otra = await puerto.LeerNotasPendientesAsync(50, default);

        otra.Should().ContainSingle().Which.NumeroNota.Should().Be("80024995");
    }

    [Fact]
    public async Task Misma_nota_dos_veces_en_la_lectura_mantiene_el_mismo_id_de_solicitud()
    {
        var (puerto, _, _) = Armar(Dos());
        var a = await puerto.LeerNotasPendientesAsync(50, default);
        var b = await puerto.LeerNotasPendientesAsync(50, default);
        b.Select(x => x.SolicitudId).Should().Equal(a.Select(x => x.SolicitudId));
    }

    [Fact]
    public async Task Falla_si_A_W_no_responde_el_error_sale_y_no_se_marca_nada()
    {
        var (puerto, _, vistas) = Armar(new Sim(() => throw new DataException("A+W caído")));
        await puerto.Invoking(p => p.LeerNotasPendientesAsync(50, default)).Should().ThrowAsync<DataException>();
        vistas.Contiene(80024994).Should().BeFalse();
    }

    [Fact]
    public async Task Lento_la_cancelacion_corta_la_lectura()
    {
        var (puerto, _, _) = Armar(new Sim(() => Tabla(), TimeSpan.FromMinutes(5)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await puerto.Invoking(p => p.LeerNotasPendientesAsync(50, cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Apagada_no_abre_conexion()
    {
        var (puerto, sim, _) = Armar(Dos(), new AwNotasCreditoLecturaOptions());
        await puerto.Invoking(p => p.LeerNotasPendientesAsync(50, default)).Should().ThrowAsync<Exception>();
        sim.Aperturas.Should().Be(0);
    }

    [Fact]
    public async Task El_tope_por_lectura_se_respeta()
    {
        var (puerto, _, _) = Armar(Dos(), new AwNotasCreditoLecturaOptions { Habilitada = true, MaxPorLectura = 1 });
        (await puerto.LeerNotasPendientesAsync(50, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Con_una_ranura_de_cero_o_importe_distinto_no_es_ranura()
    {
        var f = Tabla([1, 2, 3, 100m, Dia, 0m, "V", null, null], [2, 2, 3, 100m, Dia, 100.05m, "V", null, null]);
        // Los dos casos: sin ranura en el pedido y con diferencia mayor a 0.02.
        var sim = new Sim(() => f);
        var (puerto, _, _) = Armar(sim);
        var n = await puerto.LeerNotasPendientesAsync(5, default);
        n.Should().OnlyContain(x => !x.EsRanura);
    }
}
