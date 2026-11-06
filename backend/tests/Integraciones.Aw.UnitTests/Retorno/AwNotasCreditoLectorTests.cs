using Millet.Integraciones.Aw.Infrastructure.NotasCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

public sealed class AwNotasCreditoLectorTests
{
    [Fact]
    public void El_SQL_aplica_los_cuatro_filtros_de_TI_y_une_al_pedido_original()
    {
        var sql = AwNotasCreditoLectorAdapter.ConstruirSql("AH_HAUPT_AUFTR", true);
        Assert.Contains("k.STATUS = @estatus", sql);                                  // estatus 95
        Assert.Contains("k.AH_HAUPT_AUFTR <> 0", sql);                                // solo con pedido original
        Assert.Contains("JOIN SYSADM.BW_AUFTR_KOPF a ON a.ID = k.AH_HAUPT_AUFTR", sql);
        Assert.Contains("x.DATUM > h.DATUM OR (x.DATUM = h.DATUM AND x.ID > h.ID)", sql); // último registro de la transición
        Assert.Contains("JOIN SYSADM.KU_KUNDEN c ON c.ID = k.AH_IDENT", sql);
        Assert.Contains("c.GRUPPE <> @grupo", sql);                                   // sin clientes internos de almacén
        Assert.Contains("LEFT JOIN SYSADM.KA_FIRMA_MITARB m ON m.ID = k.OR_BEARBEITER", sql);  // vendedor
    }

    [Fact]
    public void Sin_clientes_de_almacen_configurados_no_excluye_ninguno()
        => Assert.DoesNotContain("NOT IN", AwNotasCreditoLectorAdapter.ConstruirSql("AH_HAUPT_AUFTR", false));

    [Fact]
    public void Una_columna_con_inyeccion_se_rechaza()
        => Assert.Throws<ArgumentException>(() => AwNotasCreditoLectorAdapter.ConstruirSql("ID; DROP TABLE x", false));

    [Fact]
    public async Task Apagada_no_lee_nada()
    {
        var apagada = new AwNotasCreditoLectorAdapter(() => throw new InvalidOperationException("no debe abrir"), new());
        await Assert.ThrowsAsync<BusinessRuleException>(() => apagada.LeerAsync(DateTime.UtcNow, default));
    }
}
