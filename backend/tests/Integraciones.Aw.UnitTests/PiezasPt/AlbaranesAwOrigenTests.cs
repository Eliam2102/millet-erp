using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt.Workers;

namespace Millet.Integraciones.Aw.UnitTests.PiezasPt;

/// <summary>Albaranes de A+W para descontar PT: el lector solo lee la vista del script 20 y el worker manda una orden por posición.</summary>
public sealed class AlbaranesAwOrigenTests
{
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Fact]
    public void Sql_es_solo_lectura_sobre_la_vista_de_albaranes()
    {
        var sql = AlbaranesAwOrigenAdapter.Sql;
        Assert.StartsWith("SELECT", sql);
        foreach (var prohibida in new[] { "INSERT", "UPDATE", "DELETE", "EXEC", "MERGE", "DROP" })
            Assert.DoesNotContain(prohibida, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dbo.vw_erp_albaran_pos", sql);
        Assert.DoesNotContain("SYSADM", sql);
    }

    [Fact]
    public void Sql_trae_todos_los_albaranes_del_pedido_aunque_alguno_sea_anterior_a_la_ventana()
    {
        // Sin esto, una entrega parcial vieja quedaría fuera de la suma y PT no descontaría el albarán nuevo.
        Assert.Contains("a.numero_pedido IN (SELECT n.numero_pedido FROM dbo.vw_erp_albaran_pos n WHERE n.albaran_fecha >= @desde)", AlbaranesAwOrigenAdapter.Sql);
    }

    [Fact]
    public void Agrupar_da_una_orden_por_pedido_y_posicion_con_sus_albaranes()
    {
        var hoy = new DateOnly(2026, 11, 10);
        var ordenes = AlbaranesAwWorker.Agrupar(Empresa,
        [
            new("80269432", 1, 80119397, hoy, 27), new("80269432", 1, 80119398, hoy, 28),
            new(" 80269432 ", 2, 80119397, hoy, 27), new("10425068", 5, 10884488, hoy, 2.5m),
            // Dos posiciones de un pedido hijo que apuntan a la misma posición de origen con el mismo folio se suman.
            new("10425068", 5, 10884488, hoy, 1), new("", 1, 1, hoy, 1),
        ]).ToList();

        Assert.Equal(3, ordenes.Count);
        var p1 = ordenes.Single(o => o.NumeroPedido == "80269432" && o.Posicion == 1);
        Assert.Equal([(80119397, 27), (80119398, 28)], p1.Albaranes.Select(a => (a.Folio, a.Piezas)).ToList());
        Assert.Single(ordenes, o => o.NumeroPedido == "80269432" && o.Posicion == 2);
        var p5 = ordenes.Single(o => o.NumeroPedido == "10425068").Albaranes.Single();
        Assert.Equal(3, p5.Piezas);
        Assert.All(ordenes, o => Assert.Equal(Empresa, o.EmpresaId));
    }

    [Fact]
    public async Task Apagado_por_omision_no_resuelve_servicios_ni_abre_conexion()
    {
        var worker = new AlbaranesAwWorker(null!, Options.Create(new PiezasPtOptions { EmpresaId = Empresa }), NullLogger<AlbaranesAwWorker>.Instance);
        Assert.Equal(0, await worker.CorrerUnaVezAsync(CancellationToken.None));
        Assert.False(new PiezasPtOptions().AlbaranAwHabilitado);
    }
}
