using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// U1.8a — proforma del pedido antes del timbrado. HTTP + handlers +
/// PostgreSQL desechable; sólo emisor y maestro de clientes son dobles.
/// POST genera versión (201) sin consumir folio fiscal, GET devuelve la última,
/// comparar reporta diferencias contra el payload de emisión y el permiso de
/// lectura no alcanza para generar.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ProformaPedidoEndpointsTests : IAsyncLifetime
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string Pedidos = "/api/v1/facturacion/pedidos-facturables";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public ProformaPedidoEndpointsTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmpresaFiscalReadPort>(); services.AddSingleton<IEmpresaFiscalReadPort, Emisor>();
            services.RemoveAll<IClientesReadPort>(); services.AddSingleton<IClientesReadPort, Clientes>();
        }));

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Generar_proforma_201_y_ultima_200_con_folio_PF_versionado()
    {
        using var client = await SuperAdminAsync();
        var (pedidoId, numero) = await CrearPedidoAsync();

        var sinProforma = await client.GetAsync($"{Pedidos}/{pedidoId}/proformas/ultima");
        Assert.Equal(HttpStatusCode.NoContent, sinProforma.StatusCode);

        var primera = await client.PostAsync($"{Pedidos}/{pedidoId}/proformas", null);
        Assert.True(primera.StatusCode == HttpStatusCode.Created, await primera.Content.ReadAsStringAsync());
        var segunda = await client.PostAsync($"{Pedidos}/{pedidoId}/proformas", null);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);

        var ultima = await client.GetFromJsonAsync<JsonElement>($"{Pedidos}/{pedidoId}/proformas/ultima");
        Assert.Equal($"PF-{numero}-2", ultima.GetProperty("folio").GetString());
        Assert.Equal(2, ultima.GetProperty("version").GetInt32());
        var contenido = ultima.GetProperty("contenido");
        Assert.Equal("AAA010101AAA", contenido.GetProperty("receptor").GetProperty("rfc").GetString());
        Assert.Equal(116m, contenido.GetProperty("totales").GetProperty("total").GetDecimal());

        // CA-b: no consume folio fiscal ni crea comprobante; el pedido sigue por facturar.
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        Assert.False(await db.FacturasVenta.AnyAsync(f => f.PedidoFacturableId == pedidoId));
        var pedido = await db.PedidosFacturables.AsNoTracking().SingleAsync(p => p.Id == pedidoId);
        Assert.Equal(EstadoPedidoFacturable.Importado, pedido.Estado);
    }

    [Fact]
    public async Task Comparar_con_payload_modificado_devuelve_diferencias_y_sin_cambios_ninguna()
    {
        using var client = await SuperAdminAsync();
        var (pedidoId, _) = await CrearPedidoAsync();
        var generada = await client.PostAsync($"{Pedidos}/{pedidoId}/proformas", null);
        Assert.Equal(HttpStatusCode.Created, generada.StatusCode);

        var igual = await client.PostAsJsonAsync($"{Pedidos}/{pedidoId}/proformas/comparar", EmitirBody(pedidoId, cantidad: 1m));
        Assert.True(igual.StatusCode == HttpStatusCode.OK, await igual.Content.ReadAsStringAsync());
        var sinCambios = await igual.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sinCambios.GetProperty("hayProforma").GetBoolean());
        Assert.Equal(0, sinCambios.GetProperty("diferencias").GetArrayLength());

        var modificado = await client.PostAsJsonAsync($"{Pedidos}/{pedidoId}/proformas/comparar", EmitirBody(pedidoId, cantidad: 2m));
        Assert.Equal(HttpStatusCode.OK, modificado.StatusCode);
        var conCambios = await modificado.Content.ReadFromJsonAsync<JsonElement>();
        var campos = conCambios.GetProperty("diferencias").EnumerateArray()
            .Select(d => d.GetProperty("campo").GetString()).ToList();
        Assert.Contains("Concepto 1 · Cantidad", campos);
        Assert.Contains("Total", campos);
    }

    [Fact]
    public async Task Permiso_de_lectura_no_puede_generar_proforma_pero_si_consultar()
    {
        var (pedidoId, _) = await CrearPedidoAsync();
        var lector = await UsuarioConPermisosAsync(PermisosCanonicos.FacturacionFacturasLeer);

        var post = await lector.PostAsync($"{Pedidos}/{pedidoId}/proformas", null);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

        var comparar = await lector.PostAsJsonAsync($"{Pedidos}/{pedidoId}/proformas/comparar", EmitirBody(pedidoId, 1m));
        Assert.Equal(HttpStatusCode.Forbidden, comparar.StatusCode);

        var get = await lector.GetAsync($"{Pedidos}/{pedidoId}/proformas/ultima");
        Assert.NotEqual(HttpStatusCode.Forbidden, get.StatusCode);
    }

    // ----- Helpers -----

    private async Task<(Guid Id, string Numero)> CrearPedidoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var numero = $"FIX-PF-{Guid.NewGuid():N}"[..20];
        var p = PedidoFacturable.ImportarDesdeAw(Empresa, numero, Guid.NewGuid(), Guid.NewGuid(),
            "Cliente ficticio U1.8a", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        p.AgregarLinea(null, "FIX vidrio ficticio", "01010101", "H87", 1, 100, 0, false, tasaIva: .16m);
        p.RecalcularTotal();
        db.PedidosFacturables.Add(p);
        await db.SaveChangesAsync();
        return (p.Id, numero);
    }

    private static object EmitirBody(Guid pedidoId, decimal cantidad) => new
    {
        sucursalId = Guid.NewGuid(),
        receptorRfc = "AAA010101AAA",
        receptorNombre = "Cliente ficticio U1.8a",
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
        comportamientoFiscal = (int)ComportamientoFiscal.MostradorInmediato,
        obraId = (long?)null,
        obraNombre = (string?)null,
        facturaAgrupada = false,
        lineas = new[]
        {
            new
            {
                productoId = (Guid?)null, claveProdServSat = "01010101", descripcion = "FIX vidrio ficticio",
                claveUnidadSat = "H87", cantidad, valorUnitario = 100m, descuento = 0m, objetoImp = "02",
                tasaIvaTraslado = 0.16m, tasaRetencionIva = (decimal?)null, tasaRetencionIsr = (decimal?)null,
            },
        },
        pedidoFacturableId = pedidoId,
    };

    private async Task<HttpClient> SuperAdminAsync() => await LoginAsync(SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");

    private async Task<HttpClient> UsuarioConPermisosAsync(params string[] permisos)
    {
        var oid = $"test-pf-{Guid.NewGuid():N}"[..20];
        var usuarioId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rolId = Guid.CreateVersion7();
            db.Roles.Add(new Rol(rolId, oid, "Rol de prueba U1.8a"));
            foreach (var permisoId in await db.Permisos.AsNoTracking().Where(p => permisos.Contains(p.Codigo)).Select(p => p.Id).ToListAsync())
                db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            db.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario U1.8a"));
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, rolId, null));
            await db.SaveChangesAsync();
        }
        return await LoginAsync(oid, $"{oid}@test.local", "Usuario U1.8a");
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

    private sealed class Emisor : IEmpresaFiscalReadPort
    {
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken ct) =>
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "BBB010101BBB", "FIX emisor U1.8a", "601", 0.16m, "97000"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
    }

    private sealed class Clientes : IClientesReadPort
    {
        public Task<ClienteFiscalLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ClienteFiscalLectura?>(new(id, "AAA010101AAA", "Cliente ficticio U1.8a", "601", "97000", "G03", "01", "PUE", "MXN", false));
        public Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(string referencia, CancellationToken ct) => Task.FromResult<ClienteFiscalLectura?>(null);
        public Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(string? rfc, string? nombre, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteBusquedaItem>>([]);
    }
}
