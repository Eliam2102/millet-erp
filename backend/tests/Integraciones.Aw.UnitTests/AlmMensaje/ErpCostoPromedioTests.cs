using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Integraciones.Aw.Application.CostoPromedio;
using Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

namespace Millet.Integraciones.Aw.UnitTests.AlmMensaje;

/// <summary>
/// M-447 (contrato v9): costo promedio del ERP por llave de la app (sucursal + tipo + línea + artículo + medida) hacia
/// <c>dbo.erp_costo_promedio</c>. Solo inserta cuando cambia (versión + 1), conserva el último con existencia en cero y no duplica.
/// </summary>
public sealed class ErpCostoPromedioTests
{
    private static readonly Guid Conkal = Guid.NewGuid(), Circuito = Guid.NewGuid(), Vidrio = Guid.NewGuid(), Medida = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static SaldoCostoApp S(Guid suc, string? tipo, string? linea, decimal cant, decimal costo, int min = 0) =>
        new(suc, tipo, linea, Vidrio, Medida, cant, costo, T0.AddMinutes(min));

    private static ErpCostoPromedioFila F(decimal costo, long version = 0, string tipo = "OBRAS", string linea = "HEGLA 1") =>
        new(4711, "CONKAL", tipo, linea, 2600, 1800, costo, null, T0, version);

    [Fact]
    public void Agrupar_pondera_los_lotes_de_una_llave()
    {
        var r = CostoPromedioAppLector.Agrupar(
            [S(Conkal, "OBRAS", "HEGLA 1", 10, 100), S(Conkal, "OBRAS", "HEGLA 1", 30, 200)], new HashSet<Guid> { Conkal });

        var c = Assert.Single(r);
        Assert.Equal(175m, c.CostoMxn);
        Assert.Equal(("OBRAS", "HEGLA 1"), (c.TipoAlmacen, c.Linea));
    }

    [Fact]
    public void Agrupar_con_existencia_cero_conserva_el_ultimo_promedio()
    {
        var r = CostoPromedioAppLector.Agrupar(
            [S(Conkal, "OBRAS", "HEGLA 1", 0, 100, min: 1), S(Conkal, "OBRAS", "HEGLA 1", 0, 250, min: 5)], new HashSet<Guid> { Conkal });

        Assert.Equal(250m, Assert.Single(r).CostoMxn);
    }

    [Fact]
    public void Agrupar_ignora_ubicaciones_fuera_de_la_lista_y_usa_la_sucursal_sin_lista()
    {
        var r = CostoPromedioAppLector.Agrupar(
            [S(Conkal, null, null, 5, 999), S(Conkal, "EXPORTACION", "BYSTRONIC", 5, 120),
             S(Circuito, null, null, 2, 80), S(Circuito, null, null, 2, 100)],
            new HashSet<Guid> { Conkal });

        Assert.Equal(2, r.Count);
        Assert.Equal(120m, r.Single(x => x.SucursalId == Conkal).CostoMxn);
        var circuito = r.Single(x => x.SucursalId == Circuito);
        Assert.Equal(("", "", 90m), (circuito.TipoAlmacen, circuito.Linea, circuito.CostoMxn));
    }

    [Fact]
    public void Pendientes_llave_nueva_es_version_1_y_sin_cambio_no_escribe()
    {
        Assert.Equal(1, Assert.Single(ErpCostoPromedioPublicador.Pendientes([F(285.4m)], [])).Version);
        Assert.Empty(ErpCostoPromedioPublicador.Pendientes([F(285.4m)], [F(100m, 1), F(285.4m, 2)]));
    }

    [Fact]
    public void Pendientes_costo_distinto_sube_la_version_de_su_llave()
    {
        var r = ErpCostoPromedioPublicador.Pendientes(
            [F(300m), F(50m, tipo: "GENERAL", linea: "")],
            [F(285.4m, 3), F(50m, 1, tipo: "GENERAL", linea: "")]);

        var f = Assert.Single(r);
        Assert.Equal((4L, 300m), (f.Version, f.CostoM2!.Value));
    }

    [Fact]
    public async Task Simulado_no_duplica_llave_y_version_y_lee_la_ultima()
    {
        var d = new SimuladoErpCostoPromedioDestino();
        await d.InsertarAsync(F(100m, 1), default);
        await d.InsertarAsync(F(100m, 1), default);
        await d.InsertarAsync(F(120m, 2), default);

        Assert.Equal(2, d.Filas.Count);
        Assert.Equal(120m, Assert.Single(await d.LeerVigentesAsync(default)).Costo);
    }

    [Fact]
    public void Registro_por_omision_es_simulado_y_apagado_y_destino_invalido_falla()
    {
        var sp = new ServiceCollection().AddErpCostoPromedio(new ConfigurationBuilder().Build()).BuildServiceProvider();
        Assert.IsType<SimuladoErpCostoPromedioDestino>(sp.GetRequiredService<IErpCostoPromedioDestino>());
        Assert.False(new ErpCostoPromedioOptions().Habilitado);

        var malo = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:CostoPromedio:Destino"] = "Http" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddErpCostoPromedio(malo));
    }
}
