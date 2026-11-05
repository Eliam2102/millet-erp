using Millet.Integraciones.Aw.Infrastructure.NotasCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

public sealed class AwNotasCreditoLectorTests
{
    [Fact]
    public void El_SQL_aplica_los_cuatro_filtros_de_TI_y_une_al_pedido_original()
    {
        var sql = AwNotasCreditoLectorAdapter.ConstruirSql("AH_HAUPT_AUFTR", 2);
        Assert.Contains("k.STATUS = @estatus", sql);                                  // estatus 95
        Assert.Contains("k.AH_HAUPT_AUFTR <> 0", sql);                                // solo con pedido original
        Assert.Contains("JOIN SYSADM.BW_AUFTR_KOPF a ON a.ID = k.AH_HAUPT_AUFTR", sql);
        Assert.Contains("h.ID = (SELECT MAX(x.ID) FROM SYSADM.BW_GUTSCH_HIST x", sql); // último registro de la transición
        Assert.Contains("k.AH_IDENT NOT IN (@alm0, @alm1)", sql);                     // sin clientes internos de almacén
    }

    [Fact]
    public void Sin_clientes_de_almacen_configurados_no_excluye_ninguno()
        => Assert.DoesNotContain("NOT IN", AwNotasCreditoLectorAdapter.ConstruirSql("AH_HAUPT_AUFTR", 0));

    [Fact]
    public void Una_columna_con_inyeccion_se_rechaza()
        => Assert.Throws<ArgumentException>(() => AwNotasCreditoLectorAdapter.ConstruirSql("ID; DROP TABLE x", 0));

    [Fact]
    public async Task Apagada_o_sin_columna_no_lee_nada()
    {
        var apagada = new AwNotasCreditoLectorAdapter(() => throw new InvalidOperationException("no debe abrir"), new());
        await Assert.ThrowsAsync<BusinessRuleException>(() => apagada.LeerAsync(DateTime.UtcNow, default));
        var sinColumna = new AwNotasCreditoLectorAdapter(() => throw new InvalidOperationException("no debe abrir"), new() { Habilitada = true });
        await Assert.ThrowsAsync<BusinessRuleException>(() => sinColumna.LeerAsync(DateTime.UtcNow, default));
    }
}
