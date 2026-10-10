using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Origen;

public sealed class AwOrigenEstadoTests
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
    private sealed class Ambiente(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static CompartidoDbContext Db() => new(new DbContextOptionsBuilder<CompartidoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["IntegracionesAw:OrigenDemo:Permitido"] = "true",
        ["ConnectionStrings:AwOrigenPgDb"] = "Host=localhost;Database=demo;Username=demo",
    }).Build();

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("QA", true)]
    public async Task Production_nunca_permite_demo_aunque_la_configuracion_este_encendida(string ambiente, bool permitido)
    {
        await using var db = Db();
        var estado = await new AwOrigenActivo(db, Config(), new Ambiente(ambiente)).LeerAsync(default);
        estado.Permitido.Should().Be(permitido);
        estado.Origen.Should().Be("Real");
    }
    [Fact]
    public async Task Lee_el_parametro_en_cada_operacion_sin_cachear_el_origen()
    {
        await using var db = Db();
        var p = ParametroGlobal.CrearDeModulo(AwOrigenParametro.Id, AwOrigenParametro.Clave, "Real", TipoParametro.Texto, "integraciones.aw", "Origen A+W");
        db.ParametrosGlobales.Add(p);
        await db.SaveChangesAsync();
        var activo = new AwOrigenActivo(db, Config(), new Ambiente("Development"));
        (await activo.LeerAsync(default)).Origen.Should().Be("Real");
        p.ActualizarValor("Demo");
        await db.SaveChangesAsync();
        (await activo.LeerAsync(default)).Origen.Should().Be("Demo");
    }
}
