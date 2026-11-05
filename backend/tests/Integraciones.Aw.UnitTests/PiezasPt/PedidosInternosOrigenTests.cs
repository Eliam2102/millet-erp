using Millet.Integraciones.Aw.Infrastructure.Taller;

namespace Millet.Integraciones.Aw.UnitTests.PiezasPt;

/// <summary>PT5.10: el lector de pedidos internos Serie 9000 solo lee y no toca la cola de pedidos facturables.</summary>
public sealed class PedidosInternosOrigenTests
{
    [Fact]
    public void Sql_es_solo_lectura_y_solo_toma_los_folios_90000000_a_99999999()
    {
        var sql = PedidosInternosOrigenAdapter.Sql;
        Assert.StartsWith("SELECT", sql);
        foreach (var prohibida in new[] { "INSERT", "UPDATE", "DELETE", "EXEC", "MERGE", "DROP" })
            Assert.DoesNotContain(prohibida, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("c.numero_pedido >= '90000000' AND c.numero_pedido <= '99999999'", sql);
        Assert.Contains("LEN(c.numero_pedido) = 8", sql);
    }

    [Fact]
    public void Sql_no_usa_la_cola_de_solicitudes_asi_que_un_pedido_9000_nunca_llega_a_la_bandeja_de_facturables()
    {
        Assert.DoesNotContain("aw_solicitud_pedido", PedidosInternosOrigenAdapter.Sql);
        Assert.Contains("dbo.vw_erp_pedido_cabecera", PedidosInternosOrigenAdapter.Sql);
    }
}
