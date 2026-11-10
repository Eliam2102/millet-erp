using System.Data;
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
            new("80269432", 1, 80119397, hoy, 27, "80269432"), new("80269432", 1, 80119398, hoy, 28, "80269433"),
            new(" 80269432 ", 2, 80119397, hoy, 27, "80269432"), new("10425068", 5, 10884488, hoy, 2.5m, "10425068"),
            // Dos posiciones de un pedido hijo que apuntan a la misma posición de origen con el mismo folio se suman.
            new("10425068", 5, 10884488, hoy, 1, " 10425068"), new("", 1, 1, hoy, 1, "1"),
            // Mismo folio en otro pedido de A+W: es otro albarán.
            new("10425068", 6, 10884488, hoy, 1, "10425068"), new("10425068", 6, 10884488, hoy, 2, "10425999"),
        ]).ToList();

        Assert.Equal(4, ordenes.Count);
        var p1 = ordenes.Single(o => o.NumeroPedido == "80269432" && o.Posicion == 1);
        Assert.Equal([("80269432", 80119397, 27), ("80269433", 80119398, 28)], p1.Albaranes.Select(a => (a.PedidoAlbaran, a.Folio, a.Piezas)).ToList());
        Assert.Single(ordenes, o => o.NumeroPedido == "80269432" && o.Posicion == 2);
        var p5 = ordenes.Single(o => o.NumeroPedido == "10425068" && o.Posicion == 5).Albaranes.Single();
        Assert.Equal(3, p5.Piezas);
        var p6 = ordenes.Single(o => o.NumeroPedido == "10425068" && o.Posicion == 6).Albaranes;
        Assert.Equal([("10425068", 1), ("10425999", 2)], p6.Select(a => (a.PedidoAlbaran, a.Piezas)).ToList());
        Assert.All(ordenes, o => Assert.Equal(Empresa, o.EmpresaId));
    }

    [Fact]
    public void Sql_lee_el_pedido_que_lleva_el_albaran()
    {
        Assert.Contains("a.pedido_albaran", AlbaranesAwOrigenAdapter.Sql);
    }

    [Fact]
    public void Una_fila_con_nulos_se_omite_sin_lanzar_y_las_buenas_se_leen()
    {
        var t = new DataTable();
        t.Columns.Add("numero_pedido", typeof(string)); t.Columns.Add("numero_posicion", typeof(int));
        t.Columns.Add("albaran_folio", typeof(int)); t.Columns.Add("albaran_fecha", typeof(DateTime));
        t.Columns.Add("piezas", typeof(decimal)); t.Columns.Add("pedido_albaran", typeof(string));
        var fecha = new DateTime(2026, 11, 10);
        t.Rows.Add(" 10425068 ", 5, 10884488, fecha, 2m, "10425068 ");
        t.Rows.Add("10425068", 5, 10884489, DBNull.Value, 2m, "10425068");   // fecha nula: Convert.ToDateTime lanzaba
        t.Rows.Add("10425068", 5, DBNull.Value, fecha, 2m, "10425068");      // folio nulo
        t.Rows.Add(DBNull.Value, 5, 1, fecha, 2m, "10425068");               // pedido nulo
        t.Rows.Add("10425068", DBNull.Value, 1, fecha, 2m, "10425068");      // posición nula
        t.Rows.Add("10425068", 5, 1, fecha, 2m, DBNull.Value);               // pedido del albarán nulo
        t.Rows.Add("10425068", 6, 1, fecha, DBNull.Value, "10425068");       // piezas nulas cuentan como 0

        var filas = new List<AlbaranPosicionOrigen?>();
        using (var r = t.CreateDataReader())
            while (r.Read()) filas.Add(AlbaranesAwOrigenAdapter.Mapear(r));

        Assert.Equal(new AlbaranPosicionOrigen("10425068", 5, 10884488, new DateOnly(2026, 11, 10), 2m, "10425068"), filas[0]);
        Assert.All(filas.Skip(1).Take(5), Assert.Null);
        Assert.Equal(0m, filas[6]!.Piezas);
    }

    [Fact]
    public async Task Apagado_por_omision_no_resuelve_servicios_ni_abre_conexion()
    {
        var worker = new AlbaranesAwWorker(null!, Options.Create(new PiezasPtOptions { EmpresaId = Empresa }), NullLogger<AlbaranesAwWorker>.Instance);
        Assert.Equal(0, await worker.CorrerUnaVezAsync(CancellationToken.None));
        Assert.False(new PiezasPtOptions().AlbaranAwHabilitado);
    }
}
