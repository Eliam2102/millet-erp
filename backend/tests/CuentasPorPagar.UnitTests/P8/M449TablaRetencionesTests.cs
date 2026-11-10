using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Millet.CuentasPorPagar.Application.Catalogos.Retenciones;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.UnitTests.P8;

/// <summary>
/// M-449 (CA1–CA3 y CA5): la tabla de retenciones de Contabilidad del 5-nov contra los conceptos sembrados en el ERP. Las tasas salen del
/// modelo de EF (datos semilla), no de la prueba, así que un cambio de tasa en la semilla rompe esta prueba.
/// </summary>
public sealed class M449TablaRetencionesTests
{
    private static async Task<IReadOnlyList<RetencionCfdi>> ProponerAsync(string concepto, decimal baseNeta)
    {
        await using var a = new P8Fixture();
        var semilla = a.Db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RetencionConcepto))!.GetSeedData()
            .Where(d => (string)d["Concepto"]! == concepto)
            .Select(d => RetencionConcepto.Crear(concepto, (string)d["Descripcion"]!, (string)d["Impuesto"]!, (decimal)d["Tasa"]!, "FIX fuente", "FIX semilla"))
            .ToList();
        return ComparadorRetenciones.Proponer(baseNeta, semilla);
    }

    [Fact]
    public async Task CA1_RESICO_con_servicios_10000_retiene_ISR_125_e_IVA_1066_67()
    {
        var p = await ProponerAsync("RESICO_PF_SERVICIOS", 10_000m);
        p.Select(r => (r.Impuesto, r.Importe)).Should().BeEquivalentTo([("001", 125.00m), ("002", 1_066.67m)]);
    }

    [Fact]
    public async Task CA2_servicios_de_personal_50000_retiene_IVA_3000()
    {
        var p = await ProponerAsync("SERVICIOS", 50_000m);
        p.Select(r => (r.Impuesto, r.Importe)).Should().BeEquivalentTo([("002", 3_000.00m)]);
    }

    [Fact]
    public async Task CA3_arrendamiento_de_persona_fisica_20000_retiene_ISR_2000_e_IVA_2133_33()
    {
        var p = await ProponerAsync("ARRENDAMIENTO_PF", 20_000m);
        p.Select(r => (r.Impuesto, r.Importe)).Should().BeEquivalentTo([("001", 2_000.00m), ("002", 2_133.33m)]);
    }

    /// <summary>CA5: cada renglón de la tabla con su concepto y tasa (fletes, honorarios y RESICO de bienes).</summary>
    [Theory]
    [InlineData("HONORARIOS_PF", 1_000, 206.67)]
    [InlineData("FLETES", 1_000, 40.00)]
    [InlineData("RESICO_PF", 1_000, 12.50)]
    public async Task CA5_conceptos_de_la_tabla_con_sus_tasas(string concepto, decimal baseNeta, decimal total)
    {
        (await ProponerAsync(concepto, baseNeta)).Sum(r => r.Importe).Should().Be(total);
    }
}
