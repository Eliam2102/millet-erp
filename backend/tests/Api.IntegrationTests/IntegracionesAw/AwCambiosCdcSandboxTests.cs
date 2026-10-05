using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Integraciones.Aw.Application.Cambios;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Cambios;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// Lector de cambios CDC (<see cref="AwCambiosCdcOrigen"/>) contra el sandbox SQL Server con CDC habilitado
/// (<c>schema/05-cdc.sql</c>, usuario solo-lectura). OPT-IN como el resto: sin <c>AW_SANDBOX_CONN</c> retorna temprano.
/// Las mutaciones pasan por <c>aw-sandbox.sh mutar</c>; el job de captura de CDC sondea cada ~5 s, por eso se espera a que el LSN se estabilice.
/// </summary>
[Trait("Category", "AwSandbox")]
public class AwCambiosCdcSandboxTests(ITestOutputHelper salida) : IAsyncLifetime
{
    private static readonly string? ConnEnv = Environment.GetEnvironmentVariable("AW_SANDBOX_CONN") is { Length: > 0 } v ? v : null;

    private sealed class Fabrica(string cadena) : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => new SqlConnection(cadena);
    }

    private static AwCambiosCdcOrigen Origen()
    {
        var b = new SqlConnectionStringBuilder(ConnEnv!)
        {
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true, // solo sandbox autofirmado
            ApplicationIntent = ApplicationIntent.ReadOnly,
        };
        return new(new Fabrica(b.ConnectionString),
            new AwPedidosOptions { SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15 }, NullLogger<AwCambiosCdcOrigen>.Instance);
    }

    public Task InitializeAsync() => ConnEnv is null ? Task.CompletedTask : Script("reseed");
    public Task DisposeAsync() => ConnEnv is null ? Task.CompletedTask : Script("reseed");

    private static async Task Script(params string[] args)
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "tools", "aw-sandbox", "aw-sandbox.sh"))) raiz = raiz.Parent;
        var psi = new ProcessStartInfo("bash") { WorkingDirectory = raiz!.FullName, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Path.Combine(raiz.FullName, "tools", "aw-sandbox", "aw-sandbox.sh"));
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);
        if (p.ExitCode != 0) throw new InvalidOperationException($"aw-sandbox.sh {string.Join(' ', args)} falló: {await err}");
    }

    /// <summary>Espera a que el job de captura alcance lo escrito: LSN igual en dos lecturas separadas por más que su sondeo.</summary>
    private static async Task<string> LsnEstableAsync(AwCambiosCdcOrigen o)
    {
        var previo = await o.ObtenerLsnActualAsync(default);
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            var actual = await o.ObtenerLsnActualAsync(default);
            if (actual == previo) return actual;
            previo = actual;
        }
        throw new TimeoutException("El LSN de CDC no se estabilizó.");
    }

    private static async Task<List<AwCambio>> TodoAsync(AwCambiosCdcOrigen o, AwEntidadCambio e, string desde, int tamano)
    {
        var res = new List<AwCambio>();
        for (var lote = await o.LeerCambiosAsync(e, desde, tamano, default); ; lote = await o.LeerCambiosAsync(e, desde, tamano, default))
        {
            res.AddRange(lote.Cambios);
            if (lote.Completo) return res;
            Assert.NotEqual(desde, lote.SiguienteLsn); // avanza siempre
            desde = lote.SiguienteLsn;
        }
    }

    [Fact]
    public async Task Detecta_actualizaciones_y_borrados_fisicos_de_clientes_y_productos()
    {
        if (ConnEnv is null) { salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN."); return; }
        var o = Origen();
        var baseline = await LsnEstableAsync(o);

        Assert.Empty((await o.LeerCambiosAsync(AwEntidadCambio.Cliente, baseline, 100, default)).Cambios);

        await Script("mutar", "actualizar-cliente");   // cliente 10
        await Script("mutar", "actualizar-producto");  // producto 10 (BA_PRODUKTE + BA_PRODUKTE_BEZ)
        await Script("mutar", "borrar-fila");          // cliente 150 y producto 160 (+ su BOM) borrados
        await LsnEstableAsync(o);

        var clientes = await TodoAsync(o, AwEntidadCambio.Cliente, baseline, 100);
        Assert.Equal(AwTipoCambio.Upsert, clientes.Single(c => c.Referencia == "10").Tipo);
        Assert.Equal(AwTipoCambio.Eliminado, clientes.Single(c => c.Referencia == "150").Tipo);
        Assert.Equal(2, clientes.Count);

        var productos = await TodoAsync(o, AwEntidadCambio.Producto, baseline, 100);
        Assert.Equal(AwTipoCambio.Upsert, productos.Single(c => c.Referencia == "10").Tipo);
        Assert.Equal(AwTipoCambio.Eliminado, productos.Single(c => c.Referencia == "160").Tipo); // el borrado de BOM/BEZ no lo degrada a Upsert
        Assert.Equal(2, productos.Count);
    }

    [Fact]
    public async Task Pagina_sin_perder_cambios_y_un_LSN_expirado_exige_barrido_completo()
    {
        if (ConnEnv is null) { salida.WriteLine("OMITIDO: falta AW_SANDBOX_CONN."); return; }
        var o = Origen();
        var baseline = await LsnEstableAsync(o);
        await Script("mutar", "actualizar-cliente");
        await Script("mutar", "borrar-fila");
        await LsnEstableAsync(o);

        var chico = await TodoAsync(o, AwEntidadCambio.Producto, baseline, 1);
        var grande = await TodoAsync(o, AwEntidadCambio.Producto, baseline, 100);
        Assert.Equal(grande.Select(c => c.Referencia).Order(), chico.Select(c => c.Referencia).Distinct().Order());

        var ex = await Assert.ThrowsAsync<AwReaderException>(() =>
            o.LeerCambiosAsync(AwEntidadCambio.Cliente, new string('0', 20), 10, default));
        Assert.Equal("cdc_lsn_expirado", ex.Kind);
    }
}
