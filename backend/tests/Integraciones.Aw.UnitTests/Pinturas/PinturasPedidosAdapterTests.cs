using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Stubs;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.Integraciones.Aw.Application.Pinturas;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

/// <summary>I13.9 · lector de <c>dbo.pinturas_pedido</c>: mapeo al puerto, SQL del contrato 16 v2, candados y registro.</summary>
public sealed class PinturasPedidosAdapterTests
{
    private static readonly Guid Sucursal = Guid.NewGuid();

    private static FilaPinturasPedido Fila(
        string? obraOrigen = null, string? obraRef = null, string? servicioNombre = "Pintura blanco mate", decimal iva = 16m) => new(
        FolioOrden: " OT-1 ", ClienteClave: 900123, ClienteNombre: " Aluminios SA ", ObraClave: "OB-7", ObraNombre: "Torre",
        Comentarios: "  ", ServicioClave: " SER95224 ", ServicioNombre: servicioNombre, M2: 125.5m, PrecioM2: 85.25m,
        IvaPorcentaje: iva, Moneda: "mxn", ObraOrigen: obraOrigen, ObraReferenciaAw: obraRef);

    [Fact]
    public void Mapea_la_fila_al_pedido_de_Planta_Pintura()
    {
        var p = PinturasPedidosAdapter.Mapear(Fila(), Sucursal, ComportamientoFiscal.MostradorInmediato);

        p.NumeroPedido.Should().Be("OT-1");
        p.SucursalId.Should().Be(Sucursal);
        p.ClienteRef.Should().Be("900123", "cliente_clave es int y se busca en Cliente.ReferenciaExterna");
        p.ClienteNombre.Should().Be("Aluminios SA");
        p.CanalVenta.Should().Be(CanalesVentaConocidos.PlantaPintura);
        p.ComportamientoFiscal.Should().Be(ComportamientoFiscal.MostradorInmediato);
        p.Moneda.Should().Be("MXN");
        p.VersionOrigen.Should().Be(1, "no hay reenvío: una orden es una sola versión");
        p.EstadoOrigen.Should().BeNull();
        p.Comentarios.Should().BeNull();
        p.ObraId.Should().BeNull();
        p.ObraNombre.Should().Be("Torre");
        var l = p.Lineas.Should().ContainSingle().Subject;
        l.ProductoRef.Should().Be("SER95224");
        l.Descripcion.Should().Be("Pintura blanco mate");
        l.Cantidad.Should().Be(125.5m);
        l.Precio.Should().Be(85.25m);
        l.Descuento.Should().Be(0m);
        l.RequierePedimento.Should().BeFalse();
        l.ClaveProdServSat.Should().BeNull("las claves SAT las pone el maestro del ERP");
        l.TasaIva.Should().Be(0.16m, "iva_porcentaje llega en porcentaje");
        p.PayloadCrudo.Should().Contain("\"FolioOrden\":\" OT-1 \"").And.Contain("\"ClienteClave\":900123");
    }

    [Theory]
    [InlineData("AW", "4512", 4512L)]
    [InlineData("aw", " 4512 ", 4512L)]
    [InlineData("PINTURAS", "4512", null)]
    [InlineData("AW", "no-numerico", null)]
    [InlineData(null, null, null)]
    public void ObraId_solo_cuando_la_obra_viene_de_AW(string? origen, string? referencia, long? esperado) =>
        PinturasPedidosAdapter.Mapear(Fila(origen, referencia), Sucursal, ComportamientoFiscal.MostradorInmediato)
            .ObraId.Should().Be(esperado);

    [Fact]
    public void Sin_nombre_de_servicio_la_descripcion_es_la_clave_y_sin_obra_nombre_usa_la_clave() =>
        PinturasPedidosAdapter.Mapear(Fila(servicioNombre: null) with { ObraNombre = null }, Sucursal, ComportamientoFiscal.Administrativa)
            .Should().Match<PedidoPlantaPintura>(p =>
                p.Lineas[0].Descripcion == "SER95224" && p.ObraNombre == "OB-7" && p.ComportamientoFiscal == ComportamientoFiscal.Administrativa);

    [Theory]
    [InlineData(8, 0.08)]
    [InlineData(0, 0)]
    public void Iva_se_convierte_a_fraccion(decimal porcentaje, decimal fraccion) =>
        PinturasPedidosAdapter.Mapear(Fila(iva: porcentaje), Sucursal, ComportamientoFiscal.MostradorInmediato)
            .Lineas[0].TasaIva.Should().Be(fraccion);

    [Fact]
    public void Sql_de_pendientes_respeta_el_contrato_en_ambos_dialectos()
    {
        var sql = PinturasPedidosAdapter.SqlPendientes(postgres: false);
        sql.Should().StartWith("SELECT TOP (@max) ").And.Contain("FROM dbo.pinturas_pedido p")
            .And.Contain("WHERE p.erp_resultado IS NULL ORDER BY p.creado_at, p.id").And.NotContain("LIMIT");
        var pg = PinturasPedidosAdapter.SqlPendientes(postgres: true);
        pg.Should().NotContain("TOP").And.EndWith("ORDER BY p.creado_at, p.id LIMIT @max");
    }

    [Fact]
    public void Acuse_solo_toca_las_4_columnas_del_ERP_y_solo_filas_pendientes()
    {
        var sql = PinturasPedidosAdapter.SqlAcuse;
        sql.Should().Contain("SET erp_resultado = @resultado, erp_pedido_id = @pedido, erp_error = @error, erp_procesado_at = @fecha ")
            .And.EndWith("WHERE folio_orden = @folio AND erp_resultado IS NULL");
    }

    [Fact]
    public void Error_se_recorta_a_500()
    {
        PinturasPedidosAdapter.Recortar(new string('x', 600)).Should().HaveLength(500);
        PinturasPedidosAdapter.Recortar("corto").Should().Be("corto");
        PinturasPedidosAdapter.Recortar(null).Should().BeNull();
    }

    private sealed class FabricaQueFalla : IIntegracionSqlConnectionFactory
    {
        public DbConnection CreateConnection() => throw new InvalidOperationException("no debía abrir la base");
    }

    private sealed class Sucursales(Guid? id) : IPinturasSucursalResolver
    {
        public string? Pedida { get; private set; }
        public Task<Guid?> ResolverAsync(string clave, CancellationToken ct) { Pedida = clave; return Task.FromResult(id); }
    }

    private static PinturasPedidosAdapter Adaptador(IPinturasSucursalResolver sucursales, string clave) => new(
        new FabricaQueFalla(), postgres: false, sucursales,
        Options.Create(new PinturasPedidosOptions { SucursalClave = clave }), NullLogger<PinturasPedidosAdapter>.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Sin_sucursal_configurada_no_lee_ni_abre_la_base(string clave)
    {
        var sucursales = new Sucursales(Guid.NewGuid());
        (await Adaptador(sucursales, clave).LeerPendientesAsync(50, default)).Should().BeEmpty();
        sucursales.Pedida.Should().BeNull();
    }

    [Fact]
    public async Task Sucursal_inexistente_o_inactiva_no_lee_ni_abre_la_base()
    {
        var sucursales = new Sucursales(null);
        (await Adaptador(sucursales, "PIN").LeerPendientesAsync(50, default)).Should().BeEmpty();
        sucursales.Pedida.Should().Be("PIN");
    }

    private sealed class OrigenReal : IAwOrigenActivo
    {
        public Task<AwOrigenEstado> LeerAsync(CancellationToken ct) => Task.FromResult(
            new AwOrigenEstado("Real", false, false, "Sql", "Simulado", null, null, 1));
    }

    private static IConfiguration Config(string? integracion) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:AwIntegracionDb"] = integracion,
        ["ConnectionStrings:AwOrigenPgDb"] = "Host=localhost;Database=demo;Username=demo",
        ["IntegracionesAw:Pinturas:SucursalClave"] = "PIN",
    }).Build();

    private static ServiceProvider Servicios(bool conIntegracion)
    {
        var config = Config(conIntegracion
            ? "Server=demo.invalid;Database=MILLET_INTEGRACION;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
            : null);
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(config);
        // Mismo orden que Program.cs: Facturación (stub) → flujo 2 (si hay cadena) → origen Real/Demo.
        services.AddScoped<IPlantaPinturaPedidosReader, StubPlantaPinturaPedidosReader>();
        if (conIntegracion) services.AddIntegracionesAwPedidosAdapters(config);
        services.AddIntegracionesAwOrigen(config);
        services.AddScoped<IPinturasSucursalResolver>(_ => new Sucursales(Guid.NewGuid()));
        services.AddScoped<IAwOrigenActivo, OrigenReal>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void Con_AwIntegracionDb_el_real_es_el_lector_SQL_y_el_puerto_pasa_por_el_selector()
    {
        using var sp = Servicios(conIntegracion: true);
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredKeyedService<IPlantaPinturaPedidosReader>("Real").Should().BeOfType<PinturasPedidosAdapter>();
        scope.ServiceProvider.GetRequiredKeyedService<IPlantaPinturaPedidosReader>("Demo").Should().BeOfType<PinturasPedidosAdapter>();
        scope.ServiceProvider.GetRequiredService<IPlantaPinturaPedidosReader>().Should().BeOfType<AwOrigenSelectores>();
        scope.ServiceProvider.GetRequiredService<IOptions<PinturasPedidosOptions>>().Value.SucursalClave.Should().Be("PIN");
    }

    [Fact]
    public void Sin_AwIntegracionDb_el_real_sigue_siendo_el_stub()
    {
        using var sp = Servicios(conIntegracion: false);
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredKeyedService<IPlantaPinturaPedidosReader>("Real").Should().BeOfType<StubPlantaPinturaPedidosReader>();
    }
}
