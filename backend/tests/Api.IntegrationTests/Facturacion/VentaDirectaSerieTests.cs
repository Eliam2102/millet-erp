using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Administracion.Domain;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// F6.1 — venta directa (activo, almacenable, servicio) y emergencia con serie
/// propia por sucursal; no se informa a A+W. HTTP + handlers + PostgreSQL
/// desechable con la reserva de folio REAL (<c>compartido.series</c>); sólo
/// PAC, repositorio CFDI, período, emisor y maestro de clientes son dobles.
/// Cada prueba usa una sucursal sintética propia para no chocar con otras series.
/// </summary>
public sealed class VentaDirectaSerieTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string Pedidos = "/api/v1/facturacion/pedidos-facturables";
    private const string Facturas = "/api/v1/facturacion/facturas";
    private const string Series = "/api/v1/admin/series";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public VentaDirectaSerieTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>(); services.AddSingleton<ICfdiTimbradoPort, Pac>();
            services.RemoveAll<ICfdiRepositorioPort>(); services.AddSingleton<ICfdiRepositorioPort, Repositorio>();
            services.RemoveAll<IPeriodoContablePort>(); services.AddSingleton<IPeriodoContablePort, Periodo>();
            services.RemoveAll<IEmpresaFiscalReadPort>(); services.AddSingleton<IEmpresaFiscalReadPort, Emisor>();
            services.RemoveAll<IClientesReadPort>(); services.AddSingleton<IClientesReadPort, Clientes>();
        }));

    // ----- Series -----

    [Fact]
    public async Task Serie_VD_sin_sucursal_se_rechaza_y_segunda_activa_misma_sucursal_choca()
    {
        var client = await SuperAdminAsync();
        var global = await client.PostAsJsonAsync(Series, SerieBody(null, (int)TipoDocumentoSerie.VentaDirecta, Prefijo()));
        await AssertProblemAsync(global, HttpStatusCode.UnprocessableEntity, "SERIE_VD_REQUIERE_SUCURSAL");

        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.VentaDirecta);
        var segunda = await client.PostAsJsonAsync(Series, SerieBody(sucursal, (int)TipoDocumentoSerie.VentaDirecta, Prefijo()));
        await AssertProblemAsync(segunda, HttpStatusCode.Conflict, "SERIE_ACTIVA_DUPLICADA");
    }

    [Fact]
    public async Task Reservar_VD_sin_serie_de_la_sucursal_no_cae_a_otra()
    {
        var client = await SuperAdminAsync();
        // Hay serie VD en OTRA sucursal y una Cfdi en la sucursal pedida: ninguna aplica.
        await CrearSerieAsync(client, Guid.NewGuid(), TipoDocumentoSerie.VentaDirecta);
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.Cfdi);

        var reserva = await client.PostAsJsonAsync($"{Series}/reservar", new
        {
            EmpresaId = Empresa, SucursalId = sucursal,
            TipoDocumento = (int)TipoDocumentoSerie.VentaDirecta, FechaReferencia = "2026-10-19",
        });
        await AssertProblemAsync(reserva, HttpStatusCode.UnprocessableEntity, "SERIE_VENTA_DIRECTA_NO_CONFIGURADA");
    }

    // ----- Captura -----

    [Fact]
    public async Task Crear_pedido_manual_sin_clase_devuelve_400()
    {
        var client = await SuperAdminAsync();
        var r = await client.PostAsJsonAsync(Pedidos, PedidoBody(Guid.NewGuid(), clase: null, subtipo: null));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("PEDIDO_MANUAL_CLASE_REQUERIDA", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Crear_emergencia_sin_permiso_devuelve_403_y_venta_directa_si_pasa()
    {
        var client = await UsuarioConPermisosAsync(PermisosCanonicos.FacturacionPedidosCapturar);
        var sucursal = Guid.NewGuid();

        var emergencia = await client.PostAsJsonAsync(Pedidos,
            PedidoBody(sucursal, ClasePedidoManual.Emergencia, null, motivo: "A+W fuera de servicio"));
        await AssertProblemAsync(emergencia, HttpStatusCode.Forbidden, "PEDIDO_EMERGENCIA_SIN_PERMISO");

        var vd = await client.PostAsJsonAsync(Pedidos, PedidoBody(sucursal, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio));
        Assert.Equal(HttpStatusCode.Created, vd.StatusCode);
    }

    // ----- Emisión -----

    [Fact]
    public async Task Emitir_VD_servicio_usa_serie_VD_y_no_informa_a_AW_CA6_15()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        var prefijo = await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.VentaDirecta);
        await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.Cfdi);
        var pedidoId = await CrearPedidoAsync(client, sucursal, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio);

        var r = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Timbrado", body.GetProperty("estado").GetString());
        Assert.StartsWith(prefijo + "-", body.GetProperty("folio").GetString());

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var pedido = await db.PedidosFacturables.AsNoTracking().SingleAsync(p => p.Id == pedidoId);
        Assert.Equal(EstadoPedidoFacturable.Facturado, pedido.Estado);
        Assert.False(await db.IngestaControles.AnyAsync(c => c.PedidoFacturableId == pedidoId),
            "la venta directa no crea control de ingesta ni write-back hacia A+W");
    }

    [Fact]
    public async Task Emitir_VD_sin_serie_de_la_sucursal_devuelve_422()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.Cfdi);
        var pedidoId = await CrearPedidoAsync(client, sucursal, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Almacenable);

        var r = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId));
        await AssertProblemAsync(r, HttpStatusCode.UnprocessableEntity, "SERIE_VENTA_DIRECTA_NO_CONFIGURADA");
    }

    [Fact]
    public async Task Emitir_VD_activo_exige_autorizacion_y_la_consume_CA6_12()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        var prefijo = await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.VentaDirecta);
        var pedidoId = await CrearPedidoAsync(client, sucursal, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo,
            comportamiento: ComportamientoFiscal.VentaActivoFijo);

        // Sin cambiar el comportamiento: el pedido de activo lo fuerza.
        var mostrador = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId));
        await AssertProblemAsync(mostrador, HttpStatusCode.UnprocessableEntity, "FAC_PEDIDO_ACTIVO_COMPORTAMIENTO");

        var sinAutorizacion = await client.PostAsJsonAsync(Facturas,
            EmitirBody(sucursal, pedidoId, ComportamientoFiscal.VentaActivoFijo));
        await AssertProblemAsync(sinAutorizacion, HttpStatusCode.UnprocessableEntity, "AUTORIZACION_REQUERIDA");

        Guid autorizacionId;
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var a = AutorizacionVentaActivo.Crear(Empresa, $"FIX-ACT-{Guid.NewGuid():N}", "FIX activo ficticio",
                250000m, 180000m, false, 85000m, Guid.NewGuid(), DateTimeOffset.UtcNow);
            db.AutorizacionesVentaActivo.Add(a);
            await db.SaveChangesAsync();
            autorizacionId = a.Id;
        }

        var r = await client.PostAsJsonAsync(Facturas,
            EmitirBody(sucursal, pedidoId, ComportamientoFiscal.VentaActivoFijo, autorizacionId));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith(prefijo + "-", body.GetProperty("folio").GetString());

        using var verificar = _factory.Services.CreateScope();
        using var bypass2 = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var autorizacion = await verificar.ServiceProvider.GetRequiredService<FacturacionDbContext>()
            .AutorizacionesVentaActivo.AsNoTracking().SingleAsync(a => a.Id == autorizacionId);
        Assert.Equal(EstadoAutorizacionActivo.Usada, autorizacion.Estado);
        Assert.Equal(body.GetProperty("id").GetGuid(), autorizacion.FacturaVentaId);
    }

    [Fact]
    public async Task Emitir_emergencia_usa_serie_VD_y_la_bandeja_la_marca_F6_1_a()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        var prefijo = await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.VentaDirecta);
        var pedidoId = await CrearPedidoAsync(client, sucursal, ClasePedidoManual.Emergencia, null, motivo: "A+W fuera de servicio");

        var r = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var emitida = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith(prefijo + "-", emitida.GetProperty("folio").GetString());
        var facturaId = emitida.GetProperty("id").GetGuid();

        var bandeja = await client.GetFromJsonAsync<JsonElement>($"{Facturas}?clase=Emergencia&limit=200");
        var item = bandeja.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == facturaId);
        Assert.True(item.GetProperty("esEmergencia").GetBoolean());
        Assert.Equal("Emergencia", item.GetProperty("claseManual").GetString());

        var detalle = await client.GetFromJsonAsync<JsonElement>($"{Pedidos}/{pedidoId}");
        Assert.True(detalle.GetProperty("esEmergencia").GetBoolean());
        Assert.Equal("A+W fuera de servicio", detalle.GetProperty("motivoEmergencia").GetString());
    }

    [Fact]
    public async Task Emitir_pedido_manual_legacy_sin_clase_se_rechaza()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.VentaDirecta);
        var pedidoId = await CrearPedidoAsync(client, sucursal, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio);
        using (var scope = _factory.Services.CreateScope())
        {
            // Simula un pedido capturado antes de F6.1.
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE facturacion.pedido_facturable SET clase_manual = NULL, subtipo_venta_directa = NULL WHERE id = {pedidoId}");
        }

        var r = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId));
        await AssertProblemAsync(r, HttpStatusCode.UnprocessableEntity, "PEDIDO_MANUAL_SIN_CLASIFICAR");
    }

    [Fact]
    public async Task Regresion_pedido_AW_sigue_usando_serie_Cfdi()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        var prefijoCfdi = await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.Cfdi);
        await CrearSerieAsync(client, sucursal, TipoDocumentoSerie.VentaDirecta);
        Guid pedidoId;
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var p = PedidoFacturable.ImportarDesdeAw(Empresa, $"FIX-AW-{Guid.NewGuid():N}"[..30], sucursal, Guid.NewGuid(),
                "Cliente ficticio F6.1", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
            p.AgregarLinea(null, "FIX vidrio ficticio", "01010101", "H87", 1, 100, 0, false, tasaIva: .16m);
            p.RecalcularTotal();
            db.PedidosFacturables.Add(p);
            await db.SaveChangesAsync();
            pedidoId = p.Id;
        }

        var r = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.StartsWith(prefijoCfdi + "-", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("folio").GetString());
    }

    // ----- Helpers -----

    private static string Prefijo() => "T" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    private static object SerieBody(Guid? sucursal, int tipo, string prefijo) => new
    {
        Id = Guid.Empty, EmpresaId = Empresa, SucursalId = sucursal, TipoDocumento = tipo,
        Prefijo = prefijo, Sufijo = (string?)null, ReinicioPeriodo = 0,
    };

    private static async Task<string> CrearSerieAsync(HttpClient client, Guid sucursal, TipoDocumentoSerie tipo)
    {
        var prefijo = Prefijo();
        var r = await client.PostAsJsonAsync(Series, SerieBody(sucursal, (int)tipo, prefijo));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return prefijo;
    }

    private static object PedidoBody(Guid sucursal, ClasePedidoManual? clase, SubtipoVentaDirecta? subtipo,
        string? motivo = null, ComportamientoFiscal comportamiento = ComportamientoFiscal.MostradorInmediato) => new
    {
        numeroPedido = (string?)null,
        sucursalId = sucursal,
        clienteId = Guid.NewGuid(),
        clienteNombre = "Cliente ficticio F6.1",
        canalVenta = 1,
        comportamientoFiscal = (int)comportamiento,
        moneda = "MXN",
        obraId = (long?)null,
        obraNombre = (string?)null,
        comentarios = (string?)null,
        lineas = new[]
        {
            new
            {
                productoId = (Guid?)null, productoDescripcion = "FIX servicio ficticio", claveProdServSat = "01010101",
                claveUnidadSat = "H87", cantidad = 1m, precio = 100m, descuento = 0m, requierePedimento = false,
            },
        },
        claseManual = (int?)clase,
        subtipoVentaDirecta = (int?)subtipo,
        motivoEmergencia = motivo,
        referenciaPedidoAw = (string?)null,
    };

    private static async Task<Guid> CrearPedidoAsync(HttpClient client, Guid sucursal, ClasePedidoManual clase,
        SubtipoVentaDirecta? subtipo, string? motivo = null, ComportamientoFiscal comportamiento = ComportamientoFiscal.MostradorInmediato)
    {
        var r = await client.PostAsJsonAsync(Pedidos, PedidoBody(sucursal, clase, subtipo, motivo, comportamiento));
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object EmitirBody(Guid sucursal, Guid pedidoId,
        ComportamientoFiscal comportamiento = ComportamientoFiscal.MostradorInmediato, Guid? autorizacionId = null) => new
    {
        sucursalId = sucursal,
        receptorRfc = "AAA010101AAA",
        receptorNombre = "Cliente ficticio F6.1",
        receptorRegimenFiscal = "601",
        receptorCodigoPostal = "97000",
        receptorUsoCfdi = "G03",
        receptorPais = "MEX",
        rfcEmisor = "BBB010101BBB",
        regimenFiscalEmisor = "601",
        metodoPago = "PUE",
        formaPago = "01",
        moneda = "MXN",
        tipoCambio = (decimal?)null,
        canalVenta = 1,
        comportamientoFiscal = (int)comportamiento,
        obraId = (long?)null,
        obraNombre = (string?)null,
        facturaAgrupada = false,
        lineas = new[]
        {
            new
            {
                productoId = (Guid?)null, claveProdServSat = "01010101", descripcion = "FIX servicio ficticio",
                claveUnidadSat = "H87", cantidad = 1m, valorUnitario = 100m, descuento = 0m, objetoImp = "02",
                tasaIvaTraslado = 0.16m, tasaRetencionIva = (decimal?)null, tasaRetencionIsr = (decimal?)null,
            },
        },
        autorizacionId,
        pedidoFacturableId = pedidoId,
    };

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        var contenido = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode}: {contenido}");
        using var doc = JsonDocument.Parse(contenido);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    private async Task<HttpClient> SuperAdminAsync() => await LoginAsync(SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");

    private async Task<HttpClient> UsuarioConPermisosAsync(params string[] permisos)
    {
        var oid = $"test-f61-{Guid.NewGuid():N}"[..20];
        var usuarioId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rolId = Guid.CreateVersion7();
            db.Roles.Add(new Rol(rolId, oid, "Rol de prueba F6.1"));
            foreach (var permisoId in await db.Permisos.AsNoTracking().Where(p => permisos.Contains(p.Codigo)).Select(p => p.Id).ToListAsync())
                db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            db.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario F6.1"));
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, rolId, null));
            await db.SaveChangesAsync();
        }
        return await LoginAsync(oid, $"{oid}@test.local", "Usuario F6.1");
    }

    private async Task<HttpClient> LoginAsync(string oid, string email, string nombre)
    {
        var client = _factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = email, Nombre = nombre, EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class Periodo : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Emisor : IEmpresaFiscalReadPort
    {
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken ct) =>
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "BBB010101BBB", "FIX emisor F6.1", "601", 0.16m, "97000"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
    }

    private sealed class Clientes : IClientesReadPort
    {
        public Task<ClienteFiscalLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ClienteFiscalLectura?>(new(id, "AAA010101AAA", "Cliente ficticio F6.1", "601", "97000", "G03", "01", "PUE", "MXN", false));
        public Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(string referencia, CancellationToken ct) => Task.FromResult<ClienteFiscalLectura?>(null);
        public Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(string? rfc, string? nombre, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteBusquedaItem>>([]);
    }

    private sealed class Pac : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => Task.FromResult(new TimbradoResultado(
            TimbradoEstado.Timbrado, Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, "<prueba-ficticia/>", null, null));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Repositorio : ICfdiRepositorioPort
    {
        public Task<Guid> GuardarAsync(CfdiArchivoNuevo a, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<CfdiArchivoLeido?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<CfdiArchivoLeido?>(null);
        public Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken ct) => Task.FromResult<CfdiArchivoLeido?>(null);
    }
}
