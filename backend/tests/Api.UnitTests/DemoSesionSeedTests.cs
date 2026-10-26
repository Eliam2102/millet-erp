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
    [InlineData("Mantenimiento", "Ingeniería de manufactura", false)]
    [InlineData("Mantenimiento", "Mantenimiento", true)]
    [InlineData("Ingeniería", "Ingenieria de manufactura", true)]
    public void Equivalencia_demo_no_asigna_areas_de_otro_departamento(string departamento, string area, bool coincide) =>
        Assert.Equal(coincide, Millet.CentrosCosto.Infrastructure.Seed.ADM08EquivalenciasDemoHostedService.CorrespondeAlDepartamento(departamento, area));

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
        {
            ["Seed:DemoSesion:Habilitado"] = habilitado.ToString(),
            ["Seed:DemoSesion:ReemplazarAsignaciones"] = "true",
        }).Build();
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
    [InlineData("Jefe Compras y CxP", "Jefe Compras", "CxP", true, false, "cuentas_por_pagar.")]
    [InlineData("Dirección y Tesorería", "Dirección", "Tesorería", false, true, "tesoreria.")]
    public void RolCombinado_UnePermisos_SinBypassNiFirmaAjena(string rol, string perfil1, string perfil2,
        bool n1, bool n2, string modulo)
    {
        var permisos = PermisosCanonicos.Todos.Where(p => DemoSesionSeedHostedService.PermisoDelRol(rol, p.Codigo)).ToArray();
        Assert.All(PermisosCanonicos.Todos, p => Assert.Equal(
            DemoSesionSeedHostedService.PermisoDelRol(perfil1, p.Codigo) || DemoSesionSeedHostedService.PermisoDelRol(perfil2, p.Codigo),
            DemoSesionSeedHostedService.PermisoDelRol(rol, p.Codigo)));
        Assert.Contains(permisos, p => p.Codigo == "compras.ordenes.leer");
        Assert.Contains(permisos, p => p.Codigo.StartsWith(modulo, StringComparison.Ordinal));
        Assert.Equal(n1, DemoSesionSeedHostedService.PermisoDelRol(rol, PermisosCanonicos.ComprasOrdenesAutorizarNivel1));
        Assert.Equal(n2, DemoSesionSeedHostedService.PermisoDelRol(rol, PermisosCanonicos.ComprasOrdenesAutorizarNivel2));
        Assert.DoesNotContain(permisos, p => p.Codigo.Contains("todas-sucursales", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("Compras", new[] { "MID" }, true)]
    [InlineData("Jefe Compras", new[] { "MID" }, true)]
    [InlineData("Dirección", new[] { "MID" }, true)]
    [InlineData("Jefe Compras y CxP", new[] { "MID" }, true)]
    [InlineData("Dirección y Tesorería", new[] { "MID" }, true)]
    [InlineData("CxP", new[] { "MID" }, true)]
    [InlineData("Tesorería", new[] { "MID" }, true)]
    [InlineData("Facturación", new[] { "MID" }, true)]
    [InlineData("Caja", new[] { "MID" }, true)]
    [InlineData("Contabilidad", new[] { "MID" }, true)]
    [InlineData("DAF", new[] { "MID" }, true)]
    [InlineData("Administrador", new[] { "MID" }, true)]
    [InlineData("Compras", new[] { "MID", "MTY" }, false)]
    [InlineData("Compras", new[] { "MTY" }, false)]
    [InlineData("Jefe Compras + CxP", new[] { "MID" }, false)]
    [InlineData("Otro", new[] { "MID" }, false)]
    [InlineData("", new[] { "MID" }, false)]
    [InlineData("CxP", new string[0], false)]
    [InlineData("CxP", new[] { "MID", "MID" }, false)]
    public void Configuracion_ValidaUnRolPorCuentaYSucursales(string rol, string[] sucursales, bool valido)
    {
        var resultado = new DemoSesionUsuarioValidator().Validate(new DemoSesionUsuario
            { Correo = "demo@example.invalid", Rol = rol, Sucursales = sucursales });
        Assert.Equal(valido, resultado.IsValid);
    }

    [Fact]
    public void Configuracion_EnlazaUnSoloRolPorCuenta()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Usuarios:0:Correo"] = "demo@example.invalid",
            ["Usuarios:0:Rol"] = "Jefe Compras y CxP",
            ["Usuarios:0:Sucursales:0"] = "MID",
            ["Usuarios:1:Rol"] = "Compras",
        }).Build();
        var usuarios = config.GetSection("Usuarios").Get<DemoSesionUsuario[]>()!;
        Assert.Equal("Jefe Compras y CxP", usuarios[0].Rol);
        Assert.True(new DemoSesionUsuarioValidator().Validate(usuarios[0]).IsValid);
        Assert.Equal("Compras", usuarios[1].Rol);
    }

    [Fact]
    public void Version_IdentificaCompilacionYAmbiente()
    {
        var version = Millet.Api.VersionInfo.Actual("Development");
        Assert.Equal("Development", version.Ambiente);
        Assert.Matches("^[0-9a-f]{7,40}$", version.Commit!);
        Assert.True(DateTimeOffset.TryParse(version.FechaCompilacion, out var fecha));
        Assert.Equal(TimeSpan.Zero, fecha.Offset);
    }

    [Fact]
    public void CatalogoDemo_PasaImportador_ConTituloControlYPendiente()
    {
        var opciones = new CatalogoOpciones();
        opciones.AplicarDefaults();
        var r = DemoSesionSeedHostedService.CatalogoDemo();
        var resultado = new ImportadorCatalogo(new FormatoCatalogo(opciones)).Analizar(LectorTabla.Leer(r), r.Fuente, ExistenteCatalogo.Vacio);
        Assert.True(resultado.PuedeAplicar, string.Join(";", resultado.Hallazgos.Select(h => h.Mensaje)));
        Assert.Equal(7, resultado.Filas.Count);
        Assert.Equal(3, resultado.Filas.Count(f => f.NoAfectableManual));
        Assert.Contains(resultado.Filas, f => f.Control == CuentaControl.Clientes && f.Tipo == TipoCuenta.Afectable);
        Assert.Contains(resultado.Filas, f => f.Naturaleza is null);
        Assert.All(resultado.Filas, f => Assert.StartsWith("DEMO-", f.CodigoOrigen));
    }

    [Fact]
    public void P9_DAF_autoriza_y_contador_solo_prepara()
    {
        Assert.True(DemoSesionSeedHostedService.PermisoDelRol("DAF", PermisosCanonicos.ContabilidadCatalogoAutorizar));
        Assert.True(DemoSesionSeedHostedService.PermisoDelRol("DAF", PermisosCanonicos.ContabilidadCatalogoLeer));
        Assert.False(DemoSesionSeedHostedService.PermisoDelRol("Contabilidad", PermisosCanonicos.ContabilidadCatalogoAutorizar));
        Assert.True(DemoSesionSeedHostedService.PermisoDelRol("Contabilidad", PermisosCanonicos.ContabilidadCatalogoAdministrar));
    }

    [Fact]
    public void Pedidos_credito_permiso_existe_y_lo_tienen_Facturacion_y_Contabilidad()
    {
        // 86e3tfdw2: el espejo de Facturación coincide con el catálogo canónico.
        Assert.Contains(PermisosCanonicos.Todos, p => p.Codigo == PermisosCanonicos.FacturacionPedidosLeerCredito
            && p.Id == Guid.Parse("00000009-0001-0000-0000-000000000005"));
        Assert.Equal(PermisosCanonicos.FacturacionPedidosLeerCredito,
            Millet.Facturacion.Application.Pedidos.Queries.PermisoPedidoCredito.PermisoLeerCredito);
        Assert.True(DemoSesionSeedHostedService.PermisoDelRol("Facturación", PermisosCanonicos.FacturacionPedidosLeerCredito));
        Assert.True(DemoSesionSeedHostedService.PermisoDelRol("Contabilidad", PermisosCanonicos.FacturacionPedidosLeerCredito));
        Assert.False(DemoSesionSeedHostedService.PermisoDelRol("Compras", PermisosCanonicos.FacturacionPedidosLeerCredito));
    }

    [Fact]
    public void Rol_Caja_opera_como_Facturacion_pero_no_ve_pedidos_a_credito()
    {
        // 86e3tfdw2 (supuesto por confirmar con Eliam): la cajera de mostrador no ve pedidos a crédito.
        Assert.False(DemoSesionSeedHostedService.PermisoDelRol("Caja", PermisosCanonicos.FacturacionPedidosLeerCredito));
        Assert.True(DemoSesionSeedHostedService.PermisoDelRol("Caja", PermisosCanonicos.FacturacionFacturasLeer));
        Assert.True(DemoSesionSeedHostedService.PermisoDelRol("Caja", PermisosCanonicos.FacturacionPedidosCapturar));
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
