using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.SharedKernel.Application;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// G1.10 (ADM-06): corrida OPT-IN de clientes Y productos, dos veces seguidas, contra la copia local <c>AW_FULL</c> de A+W
/// (SQL Server, solo lectura, usuario <c>aw_ro</c>) hacia la BD PostgreSQL desechable de las pruebas de integración.
/// Escribe SOLO conteos agregados en <c>AW_FULL_SALIDA/resumen-corrida-maestros.{md,json}</c>: nunca RFC, nombres,
/// domicilios ni descripciones. Sin <c>AW_FULL_CONN</c> o <c>AW_FULL_SALIDA</c> la prueba se reporta OMITIDA (skip visible)
/// y no abre ninguna conexión. Nunca escribe en SQL Server ni llama a <c>aw-sandbox.sh</c>.
/// Cómo correrla (la lanza el owner o la sesión principal, nunca CI): ver docs/integration/05 §4.1.
/// La BD de destino debe estar recién creada (<c>tools/validate-integration-isolated.sh</c>).
/// </summary>
[Trait("Category", "AwFull")]
[Collection("AwClientesSync")]
public sealed class AwFullCorridaMaestrosTests(WebApplicationFactory<Program> factory, ITestOutputHelper salida)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string? ConnEnv = Environment.GetEnvironmentVariable("AW_FULL_CONN") is { Length: > 0 } c ? c : null;
    private static readonly string? SalidaEnv = Environment.GetEnvironmentVariable("AW_FULL_SALIDA") is { Length: > 0 } s ? s : null;
    private static readonly JsonSerializerOptions JsonIndentado = new() { WriteIndented = true };
    private static readonly string[] TablasHuella = ["KU_KUNDEN", "KA_ZAHLBED", "BA_PRODUKTE", "BA_STUKL", "BA_LAGMA"];

    /// <summary>Checksum de una tabla de A+W; prueba que la corrida no alteró la copia.</summary>
    private sealed record HuellaTabla(string Tabla, long Filas, string Checksum);

    private sealed record Corrida(AwClientesEjecucion Clientes, AwProductosResumen Productos, double Segundos);

    [AwFullFact]
    public async Task Corrida_AW_FULL_clientes_y_productos_dos_veces()
    {
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            if (await db.Clientes.AnyAsync(c => c.Origen == OrigenMaster.Aw) || await db.ProductosAw.AnyAsync(p => p.Origen == OrigenMaster.Aw))
                Assert.Fail("La corrida exige una BD recién creada (tools/validate-integration-isolated.sh).");
        }

        var antes = await HuellaAsync();
        var corrida1 = await CorrerAsync();
        var corrida2 = await CorrerAsync();
        var despues = await HuellaAsync();

        // Destino: solo conteos.
        using var scopeDestino = factory.Services.CreateScope();
        using var bypassDestino = scopeDestino.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var dbDestino = scopeDestino.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var cambios = await dbDestino.CambiosMaestrosAw.AsNoTracking()
            .Select(c => new { c.TipoEntidad, c.ReferenciaExterna, c.Campo, c.Tipo, c.VersionEvento, c.Estado }).ToListAsync();
        var gruposDuplicados = cambios.GroupBy(c => (c.TipoEntidad, c.ReferenciaExterna, c.Campo, c.Tipo, c.VersionEvento)).Count(g => g.Count() > 1);
        var rfcRepetidosErp = (await dbDestino.Clientes.AsNoTracking().Select(c => c.Rfc).ToListAsync())
            .Where(RfcCliente.EsLigable).GroupBy(RfcCliente.Normalizar).Count(g => g.Count() > 1);
        var conflictosPorTipo = cambios.Where(c => c.Tipo is TipoCambioMaestroAw.RfcCompartido or TipoCambioMaestroAw.EditadoEnErp)
            .GroupBy(c => $"{c.TipoEntidad}/{c.Tipo}/{c.Estado}").OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        var cambiosPorCampo = cambios.Where(c => c.Estado == EstadoCambioMaestroAw.Informativo)
            .GroupBy(c => $"{c.TipoEntidad}/{c.Campo}").OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        var erroresClientes = corrida1.Clientes.ErroresPorReferencia.GroupBy(e => e.Codigo).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        var erroresProductos = corrida1.Productos.ErroresPorReferencia.GroupBy(e => e.Codigo).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());

        var resumen = new
        {
            fechaUtc = DateTime.UtcNow,
            huellaAntes = antes,
            huellaDespues = despues,
            huellasIdenticas = antes.SequenceEqual(despues),
            corrida1 = Resumen(corrida1),
            corrida2 = Resumen(corrida2),
            conflictosPorTipo,
            cambiosPorCampo,
            erroresClientesPorCodigo = erroresClientes,
            erroresProductosPorCodigo = erroresProductos,
            gruposDuplicadosEnBitacora = gruposDuplicados,
            clientesConMismoRfcEnErp = rfcRepetidosErp,
        };
        Directory.CreateDirectory(SalidaEnv!);
        await File.WriteAllTextAsync(Path.Combine(SalidaEnv!, "resumen-corrida-maestros.json"), JsonSerializer.Serialize(resumen, JsonIndentado));
        await File.WriteAllTextAsync(Path.Combine(SalidaEnv!, "resumen-corrida-maestros.md"), Markdown(resumen.fechaUtc, antes, despues, corrida1, corrida2,
            conflictosPorTipo, cambiosPorCampo, erroresClientes, erroresProductos, gruposDuplicados, rfcRepetidosErp));
        salida.WriteLine($"clientes c1={corrida1.Clientes.Leidos}/{corrida1.Clientes.Creados} c2 creados={corrida2.Clientes.Creados}; " +
            $"productos c1={corrida1.Productos.Leidos}/{corrida1.Productos.Creados} c2 creados={corrida2.Productos.Creados}; gruposDuplicados={gruposDuplicados}");

        Assert.Equal(antes, despues);
        Assert.Equal(0, corrida2.Clientes.Creados);
        Assert.Equal(0, corrida2.Productos.Creados);
        Assert.Equal(0, gruposDuplicados);
        Assert.Equal(0, rfcRepetidosErp);
    }

    private static object Resumen(Corrida c) => new
    {
        clientes = new { c.Clientes.Leidos, c.Clientes.Creados, c.Clientes.Actualizados, c.Clientes.SinCambios, c.Clientes.Conflictos, c.Clientes.Errores, c.Clientes.Pendientes, Estado = c.Clientes.Estado.ToString() },
        productos = new { c.Productos.Leidos, c.Productos.Creados, c.Productos.Actualizados, c.Productos.SinCambios, c.Productos.Conflictos, c.Productos.Errores, c.Productos.Pendientes },
        duracionSeg = Math.Round(c.Segundos, 1),
    };

    private static string Markdown(DateTime fecha, List<HuellaTabla> antes, List<HuellaTabla> despues, Corrida c1, Corrida c2,
        Dictionary<string, int> conflictos, Dictionary<string, int> cambios, Dictionary<string, int> errCli, Dictionary<string, int> errProd,
        int duplicados, int rfcRepetidos)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Corrida de maestros contra AW_FULL (solo conteos)").AppendLine();
        sb.AppendLine($"Generado: {fecha:yyyy-MM-dd HH:mm} UTC. Copia local de A+W, solo lectura. No contiene RFC, nombres, domicilios ni descripciones.").AppendLine();
        sb.AppendLine($"Huellas idénticas antes/después: **{(antes.SequenceEqual(despues) ? "sí" : "NO")}**").AppendLine();
        sb.AppendLine("| Tabla | Filas antes | Filas después | Checksum antes | Checksum después |").AppendLine("|---|---|---|---|---|");
        foreach (var (a, d) in antes.Zip(despues)) sb.AppendLine($"| {a.Tabla} | {a.Filas} | {d.Filas} | {a.Checksum} | {d.Checksum} |");
        sb.AppendLine().AppendLine("| Corrida | Entidad | Leídos | Creados | Actualizados | Sin cambios | Conflictos | Errores | Duración (s) |").AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var (n, c) in new[] { (1, c1), (2, c2) })
        {
            sb.AppendLine($"| {n} | Clientes | {c.Clientes.Leidos} | {c.Clientes.Creados} | {c.Clientes.Actualizados} | {c.Clientes.SinCambios} | {c.Clientes.Conflictos} | {c.Clientes.Errores} | {c.Segundos:0.0} |");
            sb.AppendLine($"| {n} | Productos | {c.Productos.Leidos} | {c.Productos.Creados} | {c.Productos.Actualizados} | {c.Productos.SinCambios} | {c.Productos.Conflictos} | {c.Productos.Errores} | |");
        }
        void Tabla(string titulo, Dictionary<string, int> d)
        {
            sb.AppendLine().AppendLine($"## {titulo}").AppendLine();
            if (d.Count == 0) sb.AppendLine("Ninguno.");
            foreach (var (k, v) in d) sb.AppendLine($"- {k}: {v}");
        }
        Tabla("Conflictos por tipo y estado", conflictos);
        Tabla("Cambios registrados por campo", cambios);
        Tabla("Errores de clientes por código (corrida 1)", errCli);
        Tabla("Errores de productos por código (corrida 1)", errProd);
        sb.AppendLine().AppendLine($"Grupos duplicados en la bitácora: {duplicados}. Clientes con el mismo RFC no genérico: {rfcRepetidos}.");
        return sb.ToString();
    }

    // ---------- corrida ----------
    private async Task<Corrida> CorrerAsync()
    {
        var sw = Stopwatch.StartNew();
        await LimpiarVivosAsync();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var optsClientes = new AwClientesOptions
        {
            Origen = AwClientesOrigenTipo.Sql, LecturaHabilitada = true, AplicacionHabilitada = true, TamanoLote = 500,
            SqlConnectTimeoutSeconds = 15, SqlQueryTimeoutSeconds = 120,
            MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["PESOSMX"] = "MXN", ["USD"] = "USD", ["Euro"] = "EUR" },
        };
        var optsProductos = new AwProductosOptions
        {
            OrigenHabilitado = true, Origen = AwProductosOrigenTipo.Sql, TamanoLote = 200,
            SqlConnectTimeoutSeconds = 15, SqlQueryTimeoutSeconds = 120,
        };
        var fabrica = new FabricaSql(Cadena());
        var spClientes = new ServiceCollection()
            .AddSingleton<IAwClientesOrigen>(new AwClientesSqlOrigen(fabrica, Options.Create(optsClientes), NullLogger<AwClientesSqlOrigen>.Instance))
            .BuildServiceProvider();
        var spProductos = new ServiceCollection()
            .AddSingleton<IAwProductosOrigen>(new AwProductosSqlOrigen(fabrica, Options.Create(optsProductos), NullLogger<AwProductosSqlOrigen>.Instance))
            .BuildServiceProvider();
        var db = sp.GetRequiredService<CompartidoDbContext>();

        var clientes = new AwClientesSincronizador(sp.GetRequiredService<IntegracionesAwDbContext>(), db,
            sp.GetRequiredService<AplicarClienteAwService>(), spClientes, Options.Create(optsClientes), TimeProvider.System,
            NullLogger<AwClientesSincronizador>.Instance);
        var id = await clientes.IniciarBarridoAsync("test-aw-full", CancellationToken.None);
        await clientes.EjecutarAsync(id, CancellationToken.None);

        var productos = new AwProductosSincronizador(db, new AplicarProductoAwService(db), spProductos, Options.Create(optsProductos),
            TimeProvider.System, NullLogger<AwProductosSincronizador>.Instance);
        var resumenProductos = await productos.SincronizarBarridoAsync(CancellationToken.None, id);

        using var scopeLectura = factory.Services.CreateScope();
        var ejecucion = await scopeLectura.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .AsNoTracking().Include(e => e.ErroresPorReferencia).SingleAsync(e => e.Id == id);
        return new Corrida(ejecucion, resumenProductos, sw.Elapsed.TotalSeconds);
    }

    private async Task LimpiarVivosAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
    }

    // ---------- origen (solo SELECT) ----------
    private static async Task<List<HuellaTabla>> HuellaAsync()
    {
        var sql = string.Join(" UNION ALL ", TablasHuella.Select(t =>
            $"SELECT '{t}', COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM SYSADM.{t}"));
        var lista = new List<HuellaTabla>();
        await using var cn = new SqlConnection(Cadena());
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 };
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(new HuellaTabla(rd.GetString(0), rd.GetInt64(1), rd.IsDBNull(2) ? "null" : rd.GetValue(2).ToString()!));
        return lista;
    }

    /// <summary>TrustServerCertificate SOLO aquí (copia local con certificado autofirmado), nunca en producción.</summary>
    private static string Cadena() => new SqlConnectionStringBuilder(ConnEnv!)
    {
        Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true, ApplicationIntent = ApplicationIntent.ReadOnly,
    }.ConnectionString;

    private sealed class FabricaSql(string cadena) : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => new SqlConnection(cadena);
    }
}
