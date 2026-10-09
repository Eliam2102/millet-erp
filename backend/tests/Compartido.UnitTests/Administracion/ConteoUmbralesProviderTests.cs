using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Parametros;
using Millet.Almacen.Domain.Conteos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Administracion;

public sealed class ConteoUmbralesProviderTests
{
    [Fact]
    public void Modelo_relacional_coincide_con_las_semillas_de_la_migracion()
    {
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseNpgsql("Host=localhost;Database=design_only").UseSnakeCaseNamingConvention().Options,
            new Empresa());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Lee_semillas_globales_decimales_y_cambios_sin_cache()
    {
        await using var db = new CompartidoDbContext(
            new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Empresa());
        await db.Database.EnsureCreatedAsync();
        var provider = new ConteoUmbralesProvider(db);
        var anterior = await provider.ObtenerAsync(default);
        Assert.Equal(new ConteoUmbrales(5, 1000, 1000, 10000), anterior);
        var row = await db.ParametrosGlobales.SingleAsync(p => p.Clave == ParametrosUmbralesConteo.Nivel2);
        Assert.Equal("almacen", row.Modulo);
        row.ActualizarValor("15000.50");
        await db.SaveChangesAsync();
        Assert.Equal(15000.50m, (await provider.ObtenerAsync(default)).UmbralNivel2Maximo);
        Assert.Equal(10000m, anterior.UmbralNivel2Maximo);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1,000")]
    [InlineData("abc")]
    [InlineData("")]
    public void Rechaza_valores_invalidos(string valor)
    {
        var valores = Valores();
        valores[ParametrosUmbralesConteo.VariacionPct] = valor;
        Assert.Throws<BusinessRuleException>(() => ParametrosUmbralesConteo.Leer(valores));
    }

    [Fact]
    public void Rechaza_configuracion_incompleta_y_niveles_invertidos()
    {
        var valores = Valores();
        valores[ParametrosUmbralesConteo.Nivel1] = "10000";
        Assert.Throws<BusinessRuleException>(() => ParametrosUmbralesConteo.Leer(valores));
        valores.Remove(ParametrosUmbralesConteo.Nivel1);
        Assert.Throws<BusinessRuleException>(() => ParametrosUmbralesConteo.Leer(valores));
    }

    private static Dictionary<string, string> Valores() => new()
    {
        [ParametrosUmbralesConteo.VariacionPct] = "5",
        [ParametrosUmbralesConteo.VariacionValor] = "1000",
        [ParametrosUmbralesConteo.Nivel1] = "1000",
        [ParametrosUmbralesConteo.Nivel2] = "10000",
    };
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
