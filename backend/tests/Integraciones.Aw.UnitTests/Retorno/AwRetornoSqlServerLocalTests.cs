using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Retorno;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure;
using Xunit.Abstractions;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

/// <summary>Corre solo con <c>AW_RETORNO_SQLSERVER</c> (cadena del usuario del ERP); en la CI se omite.</summary>
public sealed class SqlServerLocalFactAttribute : FactAttribute
{
    public const string Variable = "AW_RETORNO_SQLSERVER";
    public SqlServerLocalFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Prueba local de TR-01 contra SQL Server: define {Variable}.";
    }
}

/// <summary>
/// TR-01 · parte 1 (prueba local): el ERP escribe <c>aw_retorno</c> (script 14 v4) en SQL Server con el usuario del ERP
/// (SELECT + INSERT; UPDATE y DELETE denegados). Recorre el camino real: evento de Facturación (JSON del outbox) →
/// <see cref="FacturacionRetornoListenerWorker"/> → <see cref="AwRetornoDocumentoService"/> → <see cref="AwRetornoAdapter"/>
/// (candado <c>sp_getapplock</c>), con los pagos acumulados y la versión del pedido leídos de Facturación por sus adaptadores
/// reales y las equivalencias de forma de pago y moneda del <c>appsettings.json</c> del API. El acuse del servicio de Windows
/// se simula con <c>AW_RETORNO_SQLSERVER_TI</c> (UPDATE solo de aplicada_at, resultado, motivo e intentos).
/// Cada caso usa su propio pedido <c>TR01-*</c>; para repetir, vaciar la tabla como sa.
/// </summary>
public sealed class AwRetornoSqlServerLocalTests(ITestOutputHelper salida)
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DatosFiscalesEmisor Emisor = new("AAA010101AAA", "Emisor", "601", "76120");
    private static readonly DatosFiscalesReceptor Receptor = new("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false);

    private static string Cs => Environment.GetEnvironmentVariable(SqlServerLocalFactAttribute.Variable)!;
    private static string CsTi => Environment.GetEnvironmentVariable("AW_RETORNO_SQLSERVER_TI") ?? "";

    // ---------- Facturación (InMemory, entidades reales) ----------
    private sealed class Empresas(Guid? actual) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = actual;
        public bool IsBypassed => Current is null;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }

    private sealed class Fabrica(string cs) : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => new SqlConnection(cs);
    }

    private sealed class Escenario : IAsyncDisposable
    {
        public required FacturacionDbContext Db { get; init; }
        public required ServiceProvider Sp { get; init; }
        public required ILocalIntegrationEventConsumer Oyente { get; init; }
        public required AwRetornoAcuseWorker Acuse { get; init; }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Sp.DisposeAsync(); }
    }

    /// <summary>Equivalencias reales del API (IntegracionesAw:Pedidos de appsettings.json).</summary>
    private static AwPedidosOptions OpcionesDelApi()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend", "src", "Api", "appsettings.json"))) dir = dir.Parent;
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(dir!.FullName, "backend", "src", "Api", "appsettings.json")).Build();
        var o = new AwPedidosOptions { SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15 };
        config.GetSection(AwPedidosOptions.SectionName).Bind(o);
        return o;
    }

    private static Escenario Crear(string? cs = null)
    {
        var db = new FacturacionDbContext(new DbContextOptionsBuilder<FacturacionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresas(Empresa));
        var opciones = Options.Create(OpcionesDelApi());
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        servicios.AddSingleton<ICurrentEmpresaContext>(new Empresas(null));
        servicios.AddScoped(_ => new AwRetornoDocumentoService(
            new AwRetornoAdapter(new Fabrica(cs ?? Cs), opciones, AwRetornoAdapter.CandadoSqlServer),
            new AwVersionPedidoReadAdapter(db, new Empresas(null)), new AwPagosReadAdapter(db, new Empresas(null)),
            opciones, NullLogger<AwRetornoDocumentoService>.Instance));
        var sp = servicios.BuildServiceProvider();
        return new Escenario
        {
            Db = db, Sp = sp,
            Oyente = new FacturacionRetornoListenerWorker(null!, sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<FacturacionRetornoListenerWorker>.Instance),
            Acuse = new AwRetornoAcuseWorker(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<AwRetornoAcuseWorker>.Instance),
        };
    }

    private static (PedidoFacturable Pedido, IngestaControl Control) Pedido(Escenario e, string numero, long version = 1)
    {
        var p = PedidoFacturable.ImportarDesdeAw(Empresa, numero, Sucursal, Guid.NewGuid(), "Cliente", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "42");
        var c = IngestaControl.Crear(Empresa, OrigenPedido.Aw, numero, "h1", version, EstadoIngesta.Facturado, p.Id, Ahora);
        e.Db.PedidosFacturables.Add(p);
        e.Db.IngestaControles.Add(c);
        return (p, c);
    }

    private static FacturaVenta Factura(Escenario e, string folio, PedidoFacturable pedido, string metodo, decimal precio)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, folio, 1, Sucursal, null, null, Receptor, Emisor,
            metodo, metodo == "PPD" ? "99" : "01", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        f.AgregarLinea(null, "01010101", "Vidrio", "H87", 1m, precio, 0m, "01", 0m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString().ToUpperInvariant(), null, null, null, Ahora, null, null);
        e.Db.FacturasVenta.Add(f);
        return f;
    }

    private static ReciboPago Repp(Escenario e, FacturaVenta f, string forma, decimal importe)
    {
        var r = ReciboPago.CrearBorrador(Empresa, $"REPP-{Guid.NewGuid():N}"[..12], 1, Sucursal, null, null, Receptor, Emisor,
            2026, 11, Ahora, "MXN", 1);
        r.AgregarFacturaPagada(f.Id, f.Uuid!, 1, "MXN", importe, f.Total, forma, null, null, null, null, "REF", "01", f.Total, []);
        r.EstablecerImporteTotalPago(importe);
        r.MarcarTimbradoEnProceso();
        r.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        e.Db.RecibosPago.Add(r);
        return r;
    }

    private static CobroMostrador Cobro(Escenario e, FacturaVenta f, params (string Forma, decimal Importe)[] formas)
    {
        var c = CobroMostrador.Registrar(Empresa, Guid.NewGuid(), Sucursal, null, f.Id, OrigenCobroMostrador.Mostrador, Ahora, Guid.NewGuid(),
            formas.Select(x => (x.Forma, x.Importe, (string?)null, (string?)null, (string?)null, (string?)null)).ToList());
        e.Db.CobrosMostrador.Add(c);
        return c;
    }

    // ---------- Eventos: mismo JSON (PascalCase) que serializa el outbox ----------
    private static Task Timbrada(Escenario e, FacturaVenta f, string pedido, long version) =>
        e.Oyente.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeFacturaTimbrada, Guid.NewGuid(), JsonSerializer.Serialize(
            new FacturaVentaTimbradaIntegrationEvent(Empresa, Ahora, f.Id, f.Uuid!, f.Total, "MXN", Guid.NewGuid(), "BBB010101BBB", "Cliente SA",
                f.Folio, f.MetodoPago, Ahora, NumeroPedidoAw: pedido, VersionPedidoAw: version)), default);

    private static Task ReppTimbrado(Escenario e, ReciboPago r) =>
        e.Oyente.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeReciboPagoTimbrado, Guid.NewGuid(), JsonSerializer.Serialize(
            new ReciboPagoTimbradoIntegrationEvent(Empresa, Ahora, r.Id, r.Uuid!, r.ImporteTotalPago, 0m, [])), default);

    private static Task CobroRegistrado(Escenario e, CobroMostrador c, FacturaVenta f) =>
        e.Oyente.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeCobroRegistrado, Guid.NewGuid(), JsonSerializer.Serialize(
            new CobroMostradorRegistradoIntegrationEvent(Empresa, Ahora, c.Id, Guid.NewGuid(), Guid.NewGuid(), f.Id, "FacturaVenta", "Mostrador",
                c.FormasPago.Sum(x => x.Importe), c.FormasPago.Select(x => new CobroFormaPagoAplicada(x.FormaPago, x.Importe)).ToList(),
                Moneda: "MXN")), default);

    private static Task Cancelada(Escenario e, FacturaVenta f, string pedido, long version) =>
        e.Oyente.ConsumirLocalAsync(FacturacionRetornoListenerWorker.EventTypeComprobanteCancelado, Guid.NewGuid(), JsonSerializer.Serialize(
            new ComprobanteCanceladoIntegrationEvent(Empresa, Ahora, f.Id, "FacturaVenta", f.Uuid!, NumeroPedidoAw: pedido, VersionPedidoAw: version,
                Folio: f.Folio, Total: f.Total)), default);

    // ---------- Lectura de aw_retorno con el usuario del ERP ----------
    private async Task<List<string>> Filas(string pedido)
    {
        await using var cn = new SqlConnection(Cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT secuencia, tipo, version_pedido, tipo_documento, folio, uuid, importe, forma_pago, moneda, resultado, intentos
              FROM dbo.aw_retorno WHERE numero_pedido = @p ORDER BY secuencia
            """, cn);
        cmd.Parameters.AddWithValue("@p", pedido);
        var filas = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            filas.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "-"
                : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        salida.WriteLine($"aw_retorno {pedido} (secuencia|tipo|versión|tipo_documento|folio|uuid|importe|forma_pago|moneda|resultado|intentos):");
        filas.ForEach(f => salida.WriteLine("  " + f));
        return filas;
    }

    private static string M(decimal importe) => importe.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string SinUuid(string fila) { var c = fila.Split('|'); c[5] = c[5] == "-" ? "-" : "UUID"; return string.Join("|", c); }

    /// <summary>Lo que hace la tarea de TI (servicio de Windows) al aplicar una fila.</summary>
    private static async Task AcuseTi(string pedido, int secuencia, byte resultado, string motivo, int intentos)
    {
        await using var cn = new SqlConnection(CsTi);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            UPDATE dbo.aw_retorno SET aplicada_at = SYSDATETIMEOFFSET(), resultado = @r, motivo = @m, intentos = @i
             WHERE numero_pedido = @p AND secuencia = @s
            """, cn);
        cmd.Parameters.AddWithValue("@r", resultado);
        cmd.Parameters.AddWithValue("@m", motivo);
        cmd.Parameters.AddWithValue("@i", intentos);
        cmd.Parameters.AddWithValue("@p", pedido);
        cmd.Parameters.AddWithValue("@s", secuencia);
        (await cmd.ExecuteNonQueryAsync()).Should().Be(1);
    }

    // ===== Casos 1–4: factura PPD, dos pagos parciales (acumulado que sustituye), reproceso sin duplicar =====
    [SqlServerLocalFact]
    public async Task Caso1a4_factura_PPD_y_dos_pagos_parciales_con_acumulado_sin_duplicar()
    {
        await using var e = Crear();
        const string pedido = "TR01-1001";
        var (p, _) = Pedido(e, pedido);
        var f = Factura(e, "FTR-1001", p, "PPD", 1000m);
        await e.Db.SaveChangesAsync();

        // (1) Factura PPD timbrada → su fila Documento FacturaFinal.
        await Timbrada(e, f, pedido, 1);
        (await Filas(pedido)).Select(SinUuid).Should().Equal($"1|3|1|FacturaFinal|FTR-1001|UUID|{M(f.Total)}|-|-|-|0");

        // (2) Primer pago parcial (REPP, transferencia, 400) → fila Pago con el acumulado 400.
        var r1 = Repp(e, f, "03", 400m);
        await e.Db.SaveChangesAsync();
        await ReppTimbrado(e, r1);

        // (3) Segundo pago (300) → fila NUEVA con el acumulado total 700; la de 400 no se toca.
        var r2 = Repp(e, f, "03", 300m);
        await e.Db.SaveChangesAsync();
        await ReppTimbrado(e, r2);

        // (4) Reprocesar los mismos eventos (factura y ambos REPP) no agrega filas.
        await Timbrada(e, f, pedido, 1);
        await ReppTimbrado(e, r1);
        await ReppTimbrado(e, r2);

        (await Filas(pedido)).Select(SinUuid).Should().Equal(
            $"1|3|1|FacturaFinal|FTR-1001|UUID|{M(f.Total)}|-|-|-|0",
            "2|4|1|-|FTR-1001|-|400.00|TRANSFERENCIA|PESOSMX|-|0",
            "3|4|1|-|FTR-1001|-|700.00|TRANSFERENCIA|PESOSMX|-|0");
    }

    // ===== Dos parcialidades del mismo pedido + cancelación informativa (caso 5) =====
    [SqlServerLocalFact]
    public async Task Caso5_dos_parcialidades_y_la_cancelacion_es_una_fila_nueva_informativa()
    {
        await using var e = Crear();
        const string pedido = "TR01-1002";
        var (p, _) = Pedido(e, pedido);
        var a = Factura(e, "FTR-1002A", p, "PUE", 600m);
        var b = Factura(e, "FTR-1002B", p, "PUE", 400m);
        await e.Db.SaveChangesAsync();
        await Timbrada(e, a, pedido, 1);
        await Timbrada(e, b, pedido, 1);

        b.MarcarCancelacionPendiente();
        b.MarcarCancelado();
        await e.Db.SaveChangesAsync();
        await Cancelada(e, b, pedido, 1);
        await Cancelada(e, b, pedido, 1); // reentrega

        var filas = await Filas(pedido);
        filas.Select(SinUuid).Should().Equal(
            $"1|3|1|FacturaFinal|FTR-1002A|UUID|{M(a.Total)}|-|-|-|0",
            $"2|3|1|FacturaFinal|FTR-1002B|UUID|{M(b.Total)}|-|-|-|0",
            $"3|3|1|Cancelacion|FTR-1002B|UUID|{M(b.Total)}|-|-|-|0");
        filas[2].Split('|')[5].Should().Be(b.Uuid, "la cancelación lleva el UUID cancelado");
        filas[0].Split('|')[5].Should().NotBe(filas[1].Split('|')[5], "cada parcialidad con su UUID");
    }

    // ===== PESOSMX completo + agrupación por folio + forma + moneda (diez pagos en efectivo = una fila con la suma) =====
    [SqlServerLocalFact]
    public async Task Caso7_diez_pagos_en_efectivo_MXN_son_una_fila_EFECTIVO_PESOSMX_con_la_suma()
    {
        await using var e = Crear();
        const string pedido = "TR01-1003";
        var (p, _) = Pedido(e, pedido);
        var f = Factura(e, "FTR-1003", p, "PUE", 2000m);
        // Diez pagos en efectivo de 100 (un cobro con diez formas 01) y uno con tarjeta: dos grupos.
        var c = Cobro(e, f, [.. Enumerable.Repeat(("01", 100m), 10), ("04", 50m)]);
        await e.Db.SaveChangesAsync();

        await Timbrada(e, f, pedido, 1);
        await CobroRegistrado(e, c, f);
        await CobroRegistrado(e, c, f); // reentrega

        var filas = await Filas(pedido);
        filas.Select(SinUuid).Should().Equal(
            $"1|3|1|FacturaFinal|FTR-1003|UUID|{M(f.Total)}|-|-|-|0",
            "2|4|1|-|FTR-1003|-|1000.00|EFECTIVO|PESOSMX|-|0",
            "3|4|1|-|FTR-1003|-|50.00|TARJETA CREDITO|PESOSMX|-|0");
    }

    // ===== Diez cobros en efectivo que llegan uno por uno: cada fila nueva lleva el acumulado (sustituye, no suma) =====
    [SqlServerLocalFact]
    public async Task Caso7b_diez_cobros_separados_dejan_el_acumulado_vigente_en_la_ultima_fila_del_grupo()
    {
        await using var e = Crear();
        const string pedido = "TR01-1004";
        var (p, _) = Pedido(e, pedido);
        var f = Factura(e, "FTR-1004", p, "PUE", 2000m);
        await e.Db.SaveChangesAsync();
        await Timbrada(e, f, pedido, 1);

        for (var i = 0; i < 10; i++)
        {
            var c = Cobro(e, f, ("01", 100m));
            await e.Db.SaveChangesAsync();
            await CobroRegistrado(e, c, f);
        }

        var pagos = (await Filas(pedido)).Skip(1).ToList();
        pagos.Should().HaveCount(10).And.OnlyContain(x => x.Contains("|EFECTIVO|PESOSMX|"));
        pagos.Select(x => x.Split('|')[6]).Should().Equal(Enumerable.Range(1, 10).Select(i => $"{i * 100}.00"));
    }

    // ===== Acuse del servicio de Windows: el ERP lo lee y reacciona =====
    [SqlServerLocalFact]
    public async Task Caso8_acuse_de_TI_aplicada_rechazada_y_desfasada_se_leen_bien()
    {
        CsTi.Should().NotBeEmpty("define AW_RETORNO_SQLSERVER_TI (usuario con UPDATE de las 4 columnas)");
        await using var e = Crear();
        const string pedido = "TR01-1005", detenido = "TR01-1006";
        var (p, control) = Pedido(e, pedido);
        var (p2, _) = Pedido(e, detenido);
        var f = Factura(e, "FTR-1005", p, "PPD", 1000m);
        var g = Factura(e, "FTR-1006", p2, "PUE", 300m);
        var r = Repp(e, f, "03", 250m);
        await e.Db.SaveChangesAsync();
        await Timbrada(e, f, pedido, 1);
        await ReppTimbrado(e, r);
        await Timbrada(e, g, detenido, 1);

        await AcuseTi(pedido, 1, 1, "Aplicada", 1);                                  // factura aplicada
        await AcuseTi(pedido, 2, 3, "A+W ya tiene la versión 2 del pedido", 1);      // pago desfasado
        await AcuseTi(detenido, 1, 2, "Pedido bloqueado en A+W", 3);                 // rechazada 3 veces

        // Antes de aplicar la versión nueva: espera (no inventa una fila con la misma versión).
        (await e.Acuse.ConciliarOnceAsync(default)).Should().Be(new AwRetornoConciliacion(0, 1, 1));

        // El ERP aplica la versión 2 del pedido → reemplaza el pago desfasado con la versión 2; la aplicada no se toca.
        control.Aplicar("h2", 2, EstadoIngesta.Facturado, p.Id, Ahora);
        await e.Db.SaveChangesAsync();
        (await e.Acuse.ConciliarOnceAsync(default)).Should().Be(new AwRetornoConciliacion(1, 0, 1));
        (await e.Acuse.ConciliarOnceAsync(default)).Should().Be(new AwRetornoConciliacion(0, 0, 1), "un segundo ciclo no duplica el reemplazo");

        (await Filas(pedido)).Select(SinUuid).Should().Equal(
            $"1|3|1|FacturaFinal|FTR-1005|UUID|{M(f.Total)}|-|-|1|1",
            "2|4|1|-|FTR-1005|-|250.00|TRANSFERENCIA|PESOSMX|3|1",
            "3|4|2|-|FTR-1005|-|250.00|TRANSFERENCIA|PESOSMX|-|0");
        (await Filas(detenido)).Select(SinUuid).Should().Equal($"1|3|1|FacturaFinal|FTR-1006|UUID|{M(g.Total)}|-|-|2|3");
    }

    // ===== Caso 6: destino inexistente o SQL caído → el evento falla (queda pendiente en el outbox), nada «enviado» =====
    [SqlServerLocalFact]
    public async Task Caso6_destino_inexistente_o_SQL_caido_lanza_y_el_reintento_escribe_una_sola_fila()
    {
        const string pedido = "TR01-1007";
        var caido = new SqlConnectionStringBuilder(Cs) { DataSource = "127.0.0.1,14339", ConnectTimeout = 3 }.ConnectionString;
        var sinTabla = new SqlConnectionStringBuilder(Cs) { InitialCatalog = "master" }.ConnectionString;

        await using var e1 = Crear(caido);
        var (p, _) = Pedido(e1, pedido);
        var f = Factura(e1, "FTR-1007", p, "PPD", 500m);
        await e1.Db.SaveChangesAsync();
        var sqlCaido = async () => await Timbrada(e1, f, pedido, 1);
        (await sqlCaido.Should().ThrowAsync<Exception>()).Which.GetType().Name.Should().BeOneOf(nameof(SqlException), nameof(OperationCanceledException), nameof(TaskCanceledException));

        await using var e2 = Crear(sinTabla);
        var sinDestino = async () => await Timbrada(e2, f, pedido, 1);
        (await sinDestino.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain("aw_retorno");
        (await Filas(pedido)).Should().BeEmpty("nada se dio por enviado");

        // El outbox reintenta cuando el destino vuelve: una sola fila, aunque el reintento se repita.
        await using var e3 = Crear();
        await Timbrada(e3, f, pedido, 1);
        await Timbrada(e3, f, pedido, 1);
        (await Filas(pedido)).Select(SinUuid).Should().Equal($"1|3|1|FacturaFinal|FTR-1007|UUID|{M(f.Total)}|-|-|-|0");
    }

    // ===== El usuario del ERP no puede editar ni borrar filas (script 14) =====
    [SqlServerLocalFact]
    public async Task El_usuario_del_ERP_no_puede_actualizar_ni_borrar()
    {
        await using var cn = new SqlConnection(Cs);
        await cn.OpenAsync();
        var actualizar = async () => { await using var c = new SqlCommand("UPDATE dbo.aw_retorno SET importe = 0 WHERE 1 = 0", cn); await c.ExecuteNonQueryAsync(); };
        var borrar = async () => { await using var c = new SqlCommand("DELETE FROM dbo.aw_retorno WHERE 1 = 0", cn); await c.ExecuteNonQueryAsync(); };
        (await actualizar.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229, "UPDATE denegado");
        (await borrar.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229, "DELETE denegado");
    }
}
