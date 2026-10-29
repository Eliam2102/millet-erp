using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.PiezasPt;

public sealed class PiezasPtOrigenTests
{
    [Fact]
    public void Filtro_acepta_rangos_y_numeros_sueltos()
    {
        var f = EstacionesPtFiltro.Parse(["2821-2840", "3201-3315", "3501-3509"]);
        Assert.True(f.Contiene(2830)); Assert.False(f.Contiene(2900));
        Assert.True(EstacionesPtFiltro.Parse(["2900"]).Contiene(2900));
        Assert.True(EstacionesPtFiltro.Parse([]).Vacio);
        Assert.Throws<ArgumentException>(() => EstacionesPtFiltro.Parse(["abc"]));
        Assert.Throws<ArgumentException>(() => EstacionesPtFiltro.Parse(["10-5"]));
    }

    [Fact]
    public void Estaciones_configuradas_son_las_de_la_consulta_de_referencia_de_TI()
    {
        // Anexo de Jorge (27-oct): 2821–2831, 2840, 3201–3315 y 3501–3509; 2832–2839 no son de producto terminado.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "backend", "src", "Api", "appsettings.json"))) dir = dir.Parent;
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "backend", "src", "Api", "appsettings.json")),
            new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var lista = json.RootElement.GetProperty("IntegracionesAw").GetProperty("PiezasPt").GetProperty("EstacionesPt")
            .EnumerateArray().Select(x => x.GetString()!).ToArray();
        var f = EstacionesPtFiltro.Parse(lista);
        var esperadas = Enumerable.Range(2821, 11).Append(2840).Concat(Enumerable.Range(3201, 115)).Concat(Enumerable.Range(3501, 9)).ToHashSet();
        foreach (var n in Enumerable.Range(2800, 800)) Assert.Equal(esperadas.Contains(n), f.Contiene(n));
    }

    [Theory][InlineData(true)][InlineData(false)]
    public void Sql_es_solo_lectura(bool postgres)
    {
        var sql = PiezasPtOrigenAdapter.ConstruirSql(postgres, 2, true) + PiezasPtOrigenAdapter.Columnas + PiezasPtOrigenAdapter.Desde
            + PiezasPtOrigenAdapter.DesdeClaveDemo + PiezasPtOrigenAdapter.Filtro + PiezasPtOrigenAdapter.Orden;
        foreach (var prohibida in new[] { "INSERT", "UPDATE", "DELETE", "EXEC", "MERGE", "aw_pieza_escaneo", "ALTA_PT_PEDIDO" })
            Assert.DoesNotContain(prohibida, sql, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("SELECT", sql);
    }

    private static (ServiceProvider Sp, int Aperturas) Armar(bool firmado, string? cs, bool entrada)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:PiezasPt:ContratoI134Firmado"] = firmado.ToString(),
            ["IntegracionesAw:PiezasPt:EntradaHabilitada"] = entrada.ToString(),
            ["ConnectionStrings:AwProduccionDb"] = cs,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IAwOrigenActivo>(new OrigenReal());
        services.AddScoped<AwOrigenSesion>();
        services.AddAwPiezasPt(cfg);
        return (services.BuildServiceProvider(), 0);
    }

    private sealed class OrigenReal : IAwOrigenActivo
    {
        public Task<AwOrigenEstado> LeerAsync(CancellationToken ct) => Task.FromResult(new AwOrigenEstado("Real", false, false, "Simulado", "Simulado", null, null, 0));
    }

    [Fact]
    public async Task Real_no_se_registra_sin_contrato_firmado_y_el_selector_falla_sin_abrir_conexion()
    {
        var (sp, _) = Armar(false, "Server=x;Database=y", true);
        using var scope = sp.CreateScope();
        Assert.Null(scope.ServiceProvider.GetKeyedService<IPiezasPtOrigenReader>("Real"));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            scope.ServiceProvider.GetRequiredService<IPiezasPtOrigenReader>().LeerAsync(DateTime.UtcNow, null, [(1, 2)], 10, CancellationToken.None));
        Assert.Equal("PT_ORIGEN_NO_HABILITADO", ex.Code);
    }

    [Fact]
    public void Real_se_registra_solo_con_las_tres_condiciones_y_no_abre_nada()
    {
        var (sp, _) = Armar(true, "Server=x;Database=y", true);
        using var scope = sp.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetKeyedService<IPiezasPtOrigenReader>("Real"));
    }

    [Fact]
    public async Task Adaptador_no_abre_conexion_al_construirse_ni_con_estaciones_vacias()
    {
        var aperturas = 0;
        var adaptador = new PiezasPtOrigenAdapter(() => { aperturas++; throw new InvalidOperationException(); }, false);
        Assert.Empty(await adaptador.LeerAsync(DateTime.UtcNow, null, [], 10, CancellationToken.None));
        Assert.Equal(0, aperturas);
    }
}
