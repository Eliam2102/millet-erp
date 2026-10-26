using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// 86e3t97tx: dos reintentos de timbrado simultáneos del mismo comprobante
/// deben llamar al PAC una sola vez (un solo UUID, mismo folio interno, una
/// sola fila nueva de bitácora). El perdedor recibe un código claro.
/// </summary>
public sealed class ReintentarTimbradoConcurrenciaTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Caso B: Idempotency-Key distintas. Ambos requests cargan el comprobante
    /// en TimbradoFallido (barrera en la validación de periodo) antes de que
    /// cualquiera guarde; solo uno puede llegar al PAC.
    /// </summary>
    [Fact]
    public async Task Dos_reintentos_con_keys_distintas_llaman_al_PAC_una_sola_vez()
    {
        var pac = new PacControlado(bloquearPrimeraLlamada: false);
        var periodo = new PeriodoConBarrera(participantes: 2);
        await using var app = CrearApp(pac, periodo);
        using var client = await ClienteAutenticadoAsync(app);
        var (id, folio) = await CrearFacturaFallidaAsync(app);

        var r1 = EnviarReintentoAsync(client, id, Guid.NewGuid().ToString("D"));
        var r2 = EnviarReintentoAsync(client, id, Guid.NewGuid().ToString("D"));
        var respuestas = await Task.WhenAll(r1, r2);

        Assert.Equal(1, pac.Llamadas);
        var ganador = Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        var perdedor = Assert.Single(respuestas, r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, perdedor.StatusCode);
        Assert.Equal("REINTENTO_EN_CURSO", await CodigoAsync(perdedor));

        await AfirmarUnSoloTimbreAsync(app, id, folio, pac, ganador);
    }

    /// <summary>
    /// Caso B': el segundo reintento llega mientras el primero está esperando
    /// al PAC. El estado «en proceso» ya está guardado, así que el segundo se
    /// rechaza sin llamar al PAC.
    /// </summary>
    [Fact]
    public async Task Reintento_mientras_otro_espera_al_PAC_es_rechazado_sin_PAC()
    {
        var pac = new PacControlado(bloquearPrimeraLlamada: true);
        await using var app = CrearApp(pac, periodo: null);
        using var client = await ClienteAutenticadoAsync(app);
        var (id, folio) = await CrearFacturaFallidaAsync(app);

        var primero = EnviarReintentoAsync(client, id, Guid.NewGuid().ToString("D"));
        await pac.PrimeraLlamadaRecibida.Task.WaitAsync(Espera);
        var segundo = await EnviarReintentoAsync(client, id, Guid.NewGuid().ToString("D"));
        pac.Liberar();
        var ganador = await primero;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, segundo.StatusCode);
        Assert.Equal("COMPROBANTE_NO_REINTENTABLE", await CodigoAsync(segundo));
        Assert.Equal(HttpStatusCode.OK, ganador.StatusCode);
        await AfirmarUnSoloTimbreAsync(app, id, folio, pac, ganador);
    }

    /// <summary>
    /// Caso A: misma Idempotency-Key (doble clic / reintento de red). El
    /// middleware de idempotencia (ADR-0020) rechaza el segundo mientras el
    /// primero sigue en vuelo.
    /// </summary>
    [Fact]
    public async Task Dos_reintentos_con_la_misma_key_llaman_al_PAC_una_sola_vez()
    {
        var pac = new PacControlado(bloquearPrimeraLlamada: true);
        await using var app = CrearApp(pac, periodo: null);
        using var client = await ClienteAutenticadoAsync(app);
        var (id, folio) = await CrearFacturaFallidaAsync(app);
        var key = Guid.NewGuid().ToString("D");

        var primero = EnviarReintentoAsync(client, id, key);
        await pac.PrimeraLlamadaRecibida.Task.WaitAsync(Espera);
        var segundo = await EnviarReintentoAsync(client, id, key);
        pac.Liberar();
        var ganador = await primero;

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal("IDEMPOTENCY_IN_PROGRESS", await CodigoAsync(segundo));
        Assert.Equal(HttpStatusCode.OK, ganador.StatusCode);
        await AfirmarUnSoloTimbreAsync(app, id, folio, pac, ganador);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private WebApplicationFactory<Program> CrearApp(PacControlado pac, PeriodoConBarrera? periodo) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>();
            services.RemoveAll<IEmpresaFiscalReadPort>();
            services.RemoveAll<IPeriodoContablePort>();
            services.AddSingleton<ICfdiTimbradoPort>(pac);
            services.AddSingleton<IEmpresaFiscalReadPort>(new EmisorPrueba());
            services.AddSingleton<IPeriodoContablePort>(periodo ?? new PeriodoConBarrera(participantes: 1));
        }));

    private static async Task<HttpClient> ClienteAutenticadoAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId,
        });
        login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", loginJson.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static Task<HttpResponseMessage> EnviarReintentoAsync(HttpClient client, Guid id, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/facturacion/comprobantes/{id}/reintentar-timbrado")
        {
            Content = JsonContent.Create(new { confirmarNoDuplicado = false }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static async Task<string?> CodigoAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Factura persistida en TimbradoFallido con rechazo limpio (CFDI40179).</summary>
    private static async Task<(Guid Id, string Folio)> CrearFacturaFallidaAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var sucursalId = await compartido.Sucursales.Select(s => s.Id).FirstAsync();
        var folio = "RT-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var hoy = DateTimeOffset.UtcNow;

        var fv = FacturaVenta.CrearBorrador(EmpresaId, folio, 1, sucursalId, null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "CLIENTE FICTICIO REINTENTO", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet prueba", "601", "97000"),
            "PUE", "01", "MXN", null, hoy.Year, hoy.Month,
            (short)1, ComportamientoFiscal.Administrativa, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Prueba reintento", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbradoFallido("CFDI40179", "rechazo limpio de prueba");
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return (fv.Id, folio);
    }

    private static async Task AfirmarUnSoloTimbreAsync(
        WebApplicationFactory<Program> app, Guid id, string folio, PacControlado pac, HttpResponseMessage ganador)
    {
        Assert.Equal(1, pac.Llamadas);
        using var json = JsonDocument.Parse(await ganador.Content.ReadAsStringAsync());
        Assert.Equal("Timbrado", json.RootElement.GetProperty("estado").GetString());
        Assert.Equal(folio, json.RootElement.GetProperty("folio").GetString());
        var uuid = json.RootElement.GetProperty("uuid").GetString();
        Assert.Equal(pac.UuidsEmitidos.Single(), uuid);

        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var comprobante = await db.Comprobantes.AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.Equal(EstadoTimbrado.Timbrado, comprobante.Estado);
        Assert.Equal(uuid, comprobante.Uuid);
        Assert.Equal(folio, comprobante.Folio);
        Assert.Equal(1, await db.BitacorasIntentoTimbrado.CountAsync(b => b.ComprobanteId == id));
    }

    /// <summary>PAC falso: cuenta llamadas, emite un UUID nuevo por llamada y puede retener la primera.</summary>
    private sealed class PacControlado(bool bloquearPrimeraLlamada) : ICfdiTimbradoPort
    {
        private readonly TaskCompletionSource _liberar = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _llamadas;
        public int Llamadas => Volatile.Read(ref _llamadas);
        public System.Collections.Concurrent.ConcurrentQueue<string> UuidsEmitidos { get; } = new();
        public TaskCompletionSource PrimeraLlamadaRecibida { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Liberar() => _liberar.TrySetResult();

        public async Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _llamadas);
            if (n == 1)
            {
                PrimeraLlamadaRecibida.TrySetResult();
                if (bloquearPrimeraLlamada)
                    await _liberar.Task.WaitAsync(Espera, ct);
            }
            var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
            UuidsEmitidos.Enqueue(uuid);
            return new TimbradoResultado(
                TimbradoEstado.Timbrado, uuid, "SELLO_CFDI", "SELLO_SAT", "00000000000000000000",
                DateTimeOffset.UtcNow, "SAT970701NN3",
                $"<cfdi folio=\"{emision.Folio}\" uuid=\"{uuid}\" fake=\"true\" />", null, null);
        }

        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Periodo abierto que retiene a cada llamador hasta que llegan
    /// <paramref name="participantes"/>: fuerza que dos reintentos lean el
    /// comprobante antes de que cualquiera guarde (carrera real).
    /// </summary>
    private sealed class PeriodoConBarrera(int participantes) : IPeriodoContablePort
    {
        private readonly TaskCompletionSource _todos = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _llegados;

        public async Task<bool> EstaAbiertoAsync(int anio, int mes, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _llegados) >= participantes)
                _todos.TrySetResult();
            await _todos.Task.WaitAsync(Espera, ct);
            return true;
        }
    }

    private sealed class EmisorPrueba : IEmpresaFiscalReadPort
    {
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken ct) =>
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "MIL010101AAA", "Millet prueba", "601", 0.16m, "97000"));
    }
}
