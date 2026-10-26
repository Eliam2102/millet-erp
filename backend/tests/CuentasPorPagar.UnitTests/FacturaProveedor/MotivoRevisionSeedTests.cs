using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Catalogos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
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

    [Theory]
    [InlineData("DISCREPANCIA_OC", "Diferencia con la OC (no de monto)", 5, "COMPRAS")]
    [InlineData("DANOS_MERCANCIA", "Daños o defectos en mercancía", 5, "COMPRAS")]
    [InlineData("DIFERENCIA_PRECIO", "Diferencia de precio (solo sin OC)", 5, "COMPRAS")]
    [InlineData("DEVOLUCION_PENDIENTE", "Devolución pendiente", 5, "COMPRAS")]
    [InlineData("NO_CONFORMIDAD", "No conformidad con servicio o producto", 5, "AREA_USUARIA")]
    [InlineData("TIEMPO_RESPUESTA", "Tiempo de respuesta del proveedor", 5, "COMPRAS")]
    [InlineData("CALIDAD_SERVICIO", "Calidad de servicio insuficiente", 5, "AREA_USUARIA")]
    [InlineData("GARANTIA_ABIERTA", "Reclamo de garantía abierto", 5, "COMPRAS")]
    [InlineData("FALTA_REPP", "Falta complemento de pago + 5 días", 5, "COMPRAS")]
    [InlineData("DOCUMENTACION_FISCAL", "Datos fiscales incorrectos", 5, "COMPRAS")]
    [InlineData("DISPUTA_CONTRACTUAL", "Disputa con proveedor", 15, "DIRECCION_F")]
    [InlineData("FIRMA_PENDIENTE", "Falta de firma (solo sin OC y gastos aduanales)", 5, "DIRECCION_F")]
    [InlineData("INDICACION_EXPRESA", "Indicación expresa", null, "CXP")]
    [InlineData("OTROS", "Otros motivos", 5, "CXP")]
    public void Seed_coincide_con_tabla_literal_de_Laura(string codigo, string nombre, int? sla, string area)
    {
        var m = MotivoRevisionConfiguration.SeedMotivos.Single(x => x.Codigo == codigo);
        m.Nombre.Should().Be(nombre);
        m.SlaDias.Should().Be(sla);
        m.Dependencia.Should().Be(area);
    }

    [Theory]
    [InlineData("DISCREPANCIA_OC", "COMPRAS", null)]
    [InlineData("DANOS_MERCANCIA", "COMPRAS", null)]
    [InlineData("DIFERENCIA_PRECIO", "COMPRAS", null)]
    [InlineData("DEVOLUCION_PENDIENTE", "COMPRAS", null)]
    [InlineData("NO_CONFORMIDAD", "AREA_USUARIA", "COMPRAS")]
    [InlineData("TIEMPO_RESPUESTA", "COMPRAS", null)]
    [InlineData("CALIDAD_SERVICIO", "AREA_USUARIA", "COMPRAS")]
    [InlineData("GARANTIA_ABIERTA", "COMPRAS", null)]
    [InlineData("FALTA_REPP", "COMPRAS", null)]
    [InlineData("DOCUMENTACION_FISCAL", "COMPRAS", null)]
    [InlineData("DISPUTA_CONTRACTUAL", "DIRECCION_F", null)]
    [InlineData("FIRMA_PENDIENTE", "DIRECCION_F", null)]
    [InlineData("INDICACION_EXPRESA", "CXP", null)]
    [InlineData("OTROS", "CXP", null)]
    public async Task Catalogo_persistido_y_consulta_respetan_reparto_de_Laura(
        string codigo, string responsable, string? avisada)
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new CuentasPorPagarDbContext(
            options, new BypassedEmpresaContext(), new PeriodoAbiertoStub(), new PeriodoAbiertoStub());
        await db.Database.EnsureCreatedAsync();
        var motivos = await new ListarMotivosRevisionHandler(db)
            .Handle(new ListarMotivosRevisionQuery(), default);
        var motivo = motivos.Single(m => m.Codigo == codigo);
        motivo.DependenciaRevisoraDefaultCodigo.Should().Be(responsable);
        motivo.AreaAvisadaCodigo.Should().Be(avisada);
    }
    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
