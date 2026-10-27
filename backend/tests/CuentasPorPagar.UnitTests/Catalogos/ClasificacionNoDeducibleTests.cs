using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Catalogos;

/// <summary>P8.6: IVA no acreditable calculado y catálogo DEMO sembrado.</summary>
public sealed class ClasificacionNoDeducibleTests
{
    private static Domain.FacturaProveedor.FacturaProveedor Factura() =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
            Guid.NewGuid(), null, null, Guid.NewGuid(), Guid.NewGuid(), "F-1", "A",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            "MXN", null, 1000m, 0m, 160m, 0m, 1160m, "P8.6", DateTimeOffset.UtcNow);

    [Fact]
    public void Factura_deducible_no_tiene_iva_no_acreditable()
    {
        var f = Factura();
        f.ClasificacionNoDeducible.Should().BeNull();
        f.IvaNoAcreditable.Should().Be(0m);
    }

    [Fact]
    public void MarcarNoDeducible_manda_todo_el_iva_a_no_acreditable()
    {
        var f = Factura();
        f.MarcarNoDeducible(" comidas ");
        f.ClasificacionNoDeducible.Should().Be("COMIDAS");
        f.IvaNoAcreditable.Should().Be(160m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ESTA_CLAVE_ES_DEMASIADO_LARGA_PARA_EL_CAMPO_DE_40")]
    public void MarcarNoDeducible_rechaza_clave_invalida(string clave)
    {
        var act = () => Factura().MarcarNoDeducible(clave);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CXP_CLASIFICACION_NO_DEDUCIBLE_INVALIDA");
    }

    [Fact]
    public void Modelo_siembra_seis_clasificaciones_DEMO_y_no_persiste_iva_no_acreditable()
    {
        using var db = new CuentasPorPagarDbContext(
            new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase($"cxp_{Guid.NewGuid()}").Options,
            new FakeEmpresa(), new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());
        Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(db).Model.FindEntityType(typeof(ClasificacionNoDeducible))!.GetSeedData().Should().HaveCount(6)
            .And.OnlyContain(s => ((string)s["Descripcion"]!).StartsWith("DEMO · "));
        db.Model.FindEntityType(typeof(Domain.FacturaProveedor.FacturaProveedor))!
            .FindProperty("IvaNoAcreditable").Should().BeNull();
    }

    private sealed class FakeEmpresa : Millet.SharedKernel.Application.ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
