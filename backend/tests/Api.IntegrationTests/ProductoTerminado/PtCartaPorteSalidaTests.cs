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
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>
/// CA5.5 (PT5.5): una salida foránea emite su Carta Porte con vehículo, operador y mercancía y deja los pedidos en ruta.
/// Host real con PostgreSQL desechable; solo el PAC, el repositorio CFDI, el período y el emisor son dobles (nunca se llama a un PAC). Datos ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class PtCartaPorteSalidaTests : IClassFixture<PtCartaPorteSalidaTests.Servidor>
{
    private const string Base = "/api/v1/producto-terminado";
    private static readonly string[] Operativo = [PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoExcepcionesGestionar];

    private readonly WebApplicationFactory<Program> _factory;
    private readonly PtDatos _d;

    public PtCartaPorteSalidaTests(Servidor fixture)
    {
        _factory = fixture.Factory;
        _d = new PtDatos(_factory);
    }

    public sealed class Servidor : IDisposable
    {
        private readonly WebApplicationFactory<Program> _raiz = new();
        public WebApplicationFactory<Program> Factory { get; }
        public Servidor() => Factory = _raiz.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>(); services.AddSingleton<ICfdiTimbradoPort, Pac>();
            services.RemoveAll<ICfdiRepositorioPort>(); services.AddSingleton<ICfdiRepositorioPort, Repositorio>();
            services.RemoveAll<IPeriodoContablePort>(); services.AddSingleton<IPeriodoContablePort, Periodo>();
            services.RemoveAll<IEmpresaFiscalReadPort>(); services.AddSingleton<IEmpresaFiscalReadPort, Emisor>();
        }));
        public void Dispose() => _raiz.Dispose();
    }

    [Fact]
    public async Task CA5_5_salida_foranea_emite_la_Carta_Porte_con_vehiculo_operador_y_mercancia_y_pasa_el_pedido_a_ruta()
    {
        var (a, _) = await _d.SucursalesAsync();
        var (numero, facturable) = await _d.PedidoLiberadoAsync(a, 2);
        var etiquetas = new[] { await _d.PiezaAsync(numero, a, 1), await _d.PiezaAsync(numero, a, 2) };
        await CrearSerieCfdiAsync(a);
        var (vehiculo, operador) = await CatalogoAsync();
        using var cliente = await _d.ClienteAsync(Operativo, a);

        var creada = await cliente.PostAsJsonAsync($"{Base}/hojas-salida", new
        {
            sucursalId = a, destino = "Ruta Mérida a Valladolid", foranea = true, vehiculoId = vehiculo, operadorId = operador, distanciaKm = 160m, pesoKg = 1200m,
            origenCodigoPostal = "97302", origenEstado = "YUC", destinoCodigoPostal = "97780", destinoEstado = "YUC",
            pedidos = new[] { new { numeroPedido = numero, etiquetas } },
        });
        Assert.Equal(HttpStatusCode.OK, creada.StatusCode);
        var id = (await Json(creada)).GetProperty("id").GetGuid();

        var emitida = await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null);
        Assert.Equal(HttpStatusCode.OK, emitida.StatusCode);
        var hoja = await Json(emitida);
        Assert.Equal("Emitida", hoja.GetProperty("estado").GetString());
        var folioCp = hoja.GetProperty("cartaPorteFolio").GetString();
        Assert.False(string.IsNullOrEmpty(folioCp));

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cp = await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().CartasPorte.Include(x => x.Mercancias).AsNoTracking().SingleAsync(x => x.Folio == folioCp);
        Assert.Equal((vehiculo, operador, "Traslado"), (cp.VehiculoId, cp.OperadorId, cp.Tipo.ToString()));
        Assert.Single(cp.Mercancias);
        Assert.Equal(1200m, cp.Mercancias.Single().PesoEnKg);
        Assert.Equal([2m], await _d.SalidasFacturacionAsync(facturable));

        // Emitir de nuevo no emite otra Carta Porte ni otra salida.
        Assert.True((await Json(await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null))).GetProperty("yaEmitida").GetBoolean());
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().CartasPorte.CountAsync(x => x.Folio == folioCp));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<(Guid Vehiculo, Guid Operador)> CatalogoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var v = Vehiculo.Crear(PtDatos.Empresa, "PT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "C2", 2021, "TPAF01", "SCT-FIX-1", "Seguros ficticios", "POL-FIX-1", 8.5m);
        var o = Operador.Crear(PtDatos.Empresa, "XAXX010101000", "Operador ficticio PT5.5", "LIC-FIX-1");
        db.Vehiculos.Add(v);
        db.Operadores.Add(o);
        await db.SaveChangesAsync();
        return (v.Id, o.Id);
    }

    private async Task CrearSerieCfdiAsync(Guid sucursal)
    {
        using var client = _factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev", EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString());
        var r = await client.PostAsJsonAsync("/api/v1/admin/series", new
        {
            Id = Guid.Empty, EmpresaId = PtDatos.Empresa, SucursalId = (Guid?)sucursal, TipoDocumento = (int)TipoDocumentoSerie.Cfdi,
            Prefijo = "P" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), Sufijo = (string?)null, ReinicioPeriodo = 0,
        });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
    }

    private sealed class Periodo : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Emisor : IEmpresaFiscalReadPort
    {
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken ct) =>
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "BBB010101BBB", "FIX emisor PT5.5", "601", 0.16m, "97302"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
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
