using Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class MotivoRevisionSeedTests
{
    [Fact]
    public void Seed_tiene_14_motivos()
    {
        MotivoRevisionConfiguration.SeedMotivos.Should().HaveCount(14);
    }

    [Fact]
    public void Seed_tiene_disputa_contractual_con_SLA_15_dias()
    {
        var disputa = MotivoRevisionConfiguration.SeedMotivos
            .Single(m => m.Codigo == "DISPUTA_CONTRACTUAL");
        disputa.SlaDias.Should().Be(15);
    }

    [Fact]
    public void Seed_tiene_indicacion_expresa_sin_SLA()
    {
        var indicacion = MotivoRevisionConfiguration.SeedMotivos
            .Single(m => m.Codigo == "INDICACION_EXPRESA");
        indicacion.SlaDias.Should().BeNull();
    }

    [Fact]
    public void Seed_resto_tiene_SLA_5_dias()
    {
        var resto = MotivoRevisionConfiguration.SeedMotivos
            .Where(m => m.Codigo is not "DISPUTA_CONTRACTUAL" and not "INDICACION_EXPRESA");
        resto.Should().AllSatisfy(m => m.SlaDias.Should().Be(5));
    }

    [Fact]
    public void Seed_codigos_son_unicos()
    {
        var codigos = MotivoRevisionConfiguration.SeedMotivos.Select(m => m.Codigo).ToList();
        codigos.Distinct().Should().HaveCount(codigos.Count);
    }

    [Fact]
    public void Seed_ids_son_deterministas_namespace_00000007_1001()
    {
        var ids = MotivoRevisionConfiguration.SeedMotivos.Select(m => m.Id.ToString()).ToList();
        ids.Should().AllSatisfy(id => id.Should().StartWith("00000007-1001-0000-0000-"));
    }
}
