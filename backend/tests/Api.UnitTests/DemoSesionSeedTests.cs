using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Api.Seed;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.UnitTests;

public sealed class DemoSesionSeedTests
{
    [Theory]
    [InlineData("Capturista Compras", false, false)]
    [InlineData("Jefe Compras", true, false)]
    [InlineData("Dirección", false, true)]
    public void FirmantesDemo_TienenIdentidadesDistintas_YPermisoDeSuNivel(string rol, bool n1, bool n2)
    {
        Assert.Equal(n1, DemoSesionSeedHostedService.PermisoDelRol(rol, PermisosCanonicos.ComprasOrdenesAutorizarNivel1));
        Assert.Equal(n2, DemoSesionSeedHostedService.PermisoDelRol(rol, PermisosCanonicos.ComprasOrdenesAutorizarNivel2));
        Assert.False(DemoSesionSeedHostedService.PermisoDelRol(rol, PermisosCanonicos.ComprasOrdenesLeerTodasSucursales));
        Assert.Equal(3, new[] { DemoSesionSeedHostedService.CapturistaComprasDemoId,
            DemoSesionSeedHostedService.JefeComprasDemoId, DemoSesionSeedHostedService.DireccionDemoId }.Distinct().Count());
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public async Task Inactivo_NoResuelveNiAbreServicios(string ambiente, bool habilitado)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Seed:DemoSesion:Habilitado"] = habilitado.ToString() }).Build();
        var seed = new DemoSesionSeedHostedService(new ScopeProhibido(), new Ambiente(ambiente), config,
            NullLogger<DemoSesionSeedHostedService>.Instance);
        await seed.StartAsync(CancellationToken.None);
        await seed.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Compras_NoTieneBypassTerritorialNiAdministracionDeIdentidad()
    {
        var permisos = PermisosCanonicos.Todos.Where(p => DemoSesionSeedHostedService.PermisoDelRol("Compras", p.Codigo)).ToArray();
        Assert.Contains(permisos, p => p.Codigo == "compras.ordenes.leer");
        Assert.DoesNotContain(permisos, p => p.Codigo.Contains("todas-sucursales", StringComparison.Ordinal) || p.Codigo.StartsWith("identidad.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Compras", "MID", true)]
    [InlineData("Compras", "MTY", false)]
    [InlineData("Contabilidad", "QRO", true)]
    [InlineData("Desconocido", "MID", false)]
    [InlineData("CxP", "OTRA", false)]
    public void Configuracion_RechazaRolOAlcanceIncorrecto(string rol, string sucursal, bool valido)
    {
        var resultado = new DemoSesionUsuarioValidator().Validate(new DemoSesionUsuario
            { Correo = "demo@example.invalid", Rol = rol, Sucursales = [sucursal] });
        Assert.Equal(valido, resultado.IsValid);
    }

    [Fact]
    public void CatalogoDemo_PasaImportador_ConTituloControlYPendiente()
    {
        var opciones = new CatalogoOpciones();
        opciones.AplicarDefaults();
        var r = DemoSesionSeedHostedService.CatalogoDemo();
        var resultado = new ImportadorCatalogo(new FormatoCatalogo(opciones)).Analizar(LectorTabla.Leer(r), r.Fuente, ExistenteCatalogo.Vacio);
        Assert.True(resultado.PuedeAplicar, string.Join(";", resultado.Hallazgos.Select(h => h.Mensaje)));
        Assert.Equal(5, resultado.Filas.Count);
        Assert.Contains(resultado.Filas, f => f.Control == CuentaControl.Clientes && f.Tipo == TipoCuenta.Afectable);
        Assert.Contains(resultado.Filas, f => f.Naturaleza is null);
        Assert.All(resultado.Filas, f => Assert.StartsWith("DEMO-", f.CodigoOrigen));
    }

    [Fact]
    public void Adjuntos_SonPdfRealesEnMemoria()
    {
        var pdf = DemoSesionSeedHostedService.CrearPdf("DEMO comprobante ficticio");
        Assert.True(pdf.Length > 500);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Contains("%%EOF", System.Text.Encoding.ASCII.GetString(pdf));
    }

    private sealed class ScopeProhibido : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("No debe acceder a infraestructura.");
    }

    private sealed class Ambiente(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "DEMO";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
