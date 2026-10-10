using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Npgsql;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// Flujo 2 (pedidos → Facturación) sobre el origen de DEMO en PostgreSQL: los adapters REALES
/// (<see cref="AwSolicitudesSqlReader"/>, readers de masters y <see cref="AwWriteBackSqlAdapter"/>) con el mismo SQL
/// que corre contra MILLET_INTEGRACION, leyendo las tablas de <c>tools/aw-origen-demo</c> (schema + seed + pedidos).
/// </summary>
[Collection("AwClientesSync")]
public class AwOrigenPgPedidosTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly Guid Sucursal = Guid.NewGuid();
    private readonly AwPedidosOptions _opts = new() { SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15 };
    private string _cs = "";

    public async Task InitializeAsync()
    {
        _cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        var dir = AwOrigenPgTests.DirectorioOrigenDemo();
        foreach (var archivo in new[] { "schema.sql", "seed.sql", "pedidos.sql" })
            await EjecutarAsync(await File.ReadAllTextAsync(Path.Combine(dir, archivo)));
    }

    public Task DisposeAsync() => EjecutarAsync("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE");

    private async Task EjecutarAsync(string sql)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private AwSolicitudesSqlReader Lector(string? canalConClave = "Ventas Circuito") => new(
        new AwOrigenPg.Fabrica(_cs),
        new Sucursales(),
        new Canales(canalConClave),
        Options.Create(_opts),
        NullLogger<AwSolicitudesSqlReader>.Instance);

    private sealed class Sucursales : ISucursalPorClaveAwResolver
    {
        public Task<Guid?> ResolverAsync(string claveAw, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(claveAw == "CIRCUITO" ? Sucursal : null);
    }

    private sealed class Canales(string? conClave) : ICanalVentaPorClaveAwResolver
    {
        public Task<short?> ResolverAsync(string claveAw, CancellationToken cancellationToken) =>
            Task.FromResult<short?>(claveAw == conClave ? (short)2 : null);
    }

    [Fact]
    public async Task Lee_la_cola_pendiente_en_orden_y_respeta_el_maximo()
    {
        var todas = await Lector().LeerPendientesAsync(50, CancellationToken.None);
        todas.Select(s => s.NumeroPedido).Should().Equal("900101", "900102", "900103");
        todas.Should().OnlyContain(s => s.Operacion == OperacionAw.Alta && s.Version == 1 && s.SolicitudId != Guid.Empty);

        (await Lector().LeerPendientesAsync(2, CancellationToken.None)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Lee_el_pedido_con_lineas_netas_de_iva()
    {
        var lectura = await Lector().LeerDatosPedidoAsync("900101", CancellationToken.None);

        lectura.Datos.Should().NotBeNull(lectura.Detalle);
        var d = lectura.Datos!;
        d.SucursalId.Should().Be(Sucursal);
        d.CanalVenta.Should().Be(2);
        d.ComportamientoFiscal.Should().Be(ComportamientoFiscal.MostradorInmediato);
        d.Moneda.Should().Be("MXN");
        d.Lineas.Should().HaveCount(2);
        d.Lineas[0].Cantidad.Should().Be(4);
        d.Lineas[0].Precio.Should().Be(1000m);   // 1160 bruto / 1.16
        d.Lineas[0].TasaIva.Should().Be(0.16m);
        d.Lineas[1].Precio.Should().Be(10m);
        d.PayloadCrudo.Should().Contain("900101");
    }

    [Fact]
    public async Task Sucursal_o_canal_sin_clave_aw_quedan_en_espera_de_configuracion()
    {
        var sinSucursal = await Lector().LeerDatosPedidoAsync("900102", CancellationToken.None);   // CANCUN sin clave
        sinSucursal.Datos.Should().BeNull();
        sinSucursal.EsperaConfiguracion.Should().BeTrue();
        sinSucursal.Motivo.Should().Be(MotivoExcepcion.SucursalSinClaveAw);

        var sinCanal = await Lector(canalConClave: null).LeerDatosPedidoAsync("900101", CancellationToken.None);
        sinCanal.EsperaConfiguracion.Should().BeTrue();
        sinCanal.Motivo.Should().Be(MotivoExcepcion.CanalVentaSinClaveAw);

        var inexistente = await Lector().LeerDatosPedidoAsync("NO-EXISTE", CancellationToken.None);
        inexistente.Datos.Should().BeNull();
        inexistente.EsperaConfiguracion.Should().BeFalse();
    }

    [Fact]
    public async Task Masters_leen_cliente_y_articulo_de_lo_sincronizado()
    {
        var clienteRef = await EscalarAsync<string>("SELECT cliente_ref FROM dbo.vw_erp_pedido_cabecera WHERE numero_pedido = '900101'");
        var productoRef = await EscalarAsync<string>("SELECT producto_ref FROM dbo.vw_erp_pedido_linea WHERE numero_pedido = '900101' AND numero_posicion = 1");

        var cliente = await new AwClientesSqlReader(new AwOrigenPg.Fabrica(_cs), Options.Create(_opts),
            NullLogger<AwClientesSqlReader>.Instance).LeerClienteAsync(clienteRef, CancellationToken.None);
        cliente.Should().NotBeNull();
        cliente!.RazonSocial.Should().NotBeNullOrWhiteSpace();
        cliente.Rfc.Should().MatchRegex("^[A-Z0-9]+$");

        var articulos = new AwArticulosSqlReader(new AwOrigenPg.Fabrica(_cs), Options.Create(_opts),
            NullLogger<AwArticulosSqlReader>.Instance);
        var articulo = await articulos.LeerArticuloAsync(productoRef, CancellationToken.None);
        articulo!.UnidadMedida.Should().Be("M2");
        (await articulos.LeerArticuloAsync("999999999", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Write_back_marca_la_solicitud_y_luego_el_uuid_del_pedido_sin_borrar_el_claim()
    {
        var solicitud = (await Lector().LeerPendientesAsync(1, CancellationToken.None)).Single();
        var erpId = Guid.NewGuid();
        var adapter = new AwWriteBackSqlAdapter(new AwOrigenPg.Fabrica(_cs), Options.Create(_opts),
            NullLogger<AwWriteBackSqlAdapter>.Instance);

        await adapter.EscribirResultadoAsync(new AwWriteBack(solicitud.SolicitudId, solicitud.NumeroPedido, erpId,
            "SinFacturar", null, ResultadoSolicitudAw.Aplicada, null), CancellationToken.None);
        await adapter.EscribirResultadoAsync(new AwWriteBack(Guid.Empty, solicitud.NumeroPedido, null,
            "Facturado", "11111111-2222-3333-4444-555555555555", ResultadoSolicitudAw.Aplicada, null), CancellationToken.None);

        var fila = await EscalarAsync<string>($"""
            SELECT concat_ws('|', erp_pedido_id, estado_facturacion, uuid, resultado, (procesada_at IS NOT NULL)::text)
              FROM dbo.aw_solicitud_pedido WHERE solicitud_id = '{solicitud.SolicitudId}'
            """);
        fila.Should().Be($"{erpId}|Facturado|11111111-2222-3333-4444-555555555555|1|true");

        // Aplicada sale de la cola de pendientes.
        (await Lector().LeerPendientesAsync(50, CancellationToken.None)).Select(s => s.NumeroPedido)
            .Should().NotContain(solicitud.NumeroPedido);
    }
}
