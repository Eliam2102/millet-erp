using Millet.Compras.Infrastructure.PublicAdapters;

namespace Millet.Compras.UnitTests.Almacen;

/// <summary>La app de Almacenes nombra la sucursal por clave del ERP, alias de A+W o (si nada coincide) nombre único.</summary>
public sealed class SucursalPorClaveAppTests
{
    private static readonly SucursalPorClaveApp.Candidata Conkal =
        new(Guid.NewGuid(), "101", "CONKAL,PERIFERICO,OBRAS", "Conkal");
    private static readonly SucursalPorClaveApp.Candidata Otra = new(Guid.NewGuid(), "CON", null, "Otra planta");

    private static Guid? Id(IReadOnlyList<SucursalPorClaveApp.Candidata> c, string codigo) =>
        SucursalPorClaveApp.Elegir(c, codigo) is { Encontrada: true } r ? r.Id : null;

    [Theory]
    [InlineData("101")]
    [InlineData("CONKAL")]
    [InlineData("conkal")]
    [InlineData(" Periferico ")]
    [InlineData("OBRAS")]
    public void Clave_alias_o_nombre_de_la_sucursal_la_resuelven(string codigo) => Id([Conkal, Otra], codigo).Should().Be(Conkal.Id);

    [Fact]
    public void La_clave_gana_sobre_un_nombre_igual_de_otra_sucursal()
    {
        var conNombreIgualACLave = new SucursalPorClaveApp.Candidata(Guid.NewGuid(), "ZZZ", null, "CON");
        Id([conNombreIgualACLave, Otra], "CON").Should().Be(Otra.Id);
    }

    [Fact]
    public void El_nombre_solo_vale_si_es_unico()
    {
        var a = new SucursalPorClaveApp.Candidata(Guid.NewGuid(), "A1", null, "Planta Norte");
        var b = new SucursalPorClaveApp.Candidata(Guid.NewGuid(), "B1", null, "planta norte");
        Id([a], "PLANTA NORTE").Should().Be(a.Id);
        Id([a, b], "Planta Norte").Should().BeNull("dos sucursales con ese nombre: ambiguo, no se adivina");
        var r = SucursalPorClaveApp.Elegir([a, b], "Planta Norte");
        r.Ambigua.Should().BeTrue();
        r.Mensaje("Planta Norte").Should().Contain("más de una sucursal").And.Contain("clave");
        SucursalPorClaveApp.Elegir([a], "NOEXISTE").Mensaje("NOEXISTE").Should().Be("No existe la sucursal 'NOEXISTE'.");
    }

    [Fact]
    public void Un_codigo_desconocido_no_resuelve() => Id([Conkal, Otra], "NOEXISTE").Should().BeNull();

    [Fact]
    public void Un_alias_parcial_no_cuenta() => Id([Conkal], "CONK").Should().BeNull();
}
