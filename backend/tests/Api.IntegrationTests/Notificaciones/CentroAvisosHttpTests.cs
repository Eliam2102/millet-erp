using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Notificaciones;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Notificaciones;

[Collection(ApiCompartida.Nombre)]
public class CentroAvisosHttpTests(WebApplicationFactory<Program> factory)
{
    private const string Base = "/api/v1/notificaciones";

    [Fact]
    public async Task Pagina_no_limita_totales_lectura_no_resuelve_y_usuario_ajeno_no_puede_leer()
    {
        var fuente = new Fuente("compras");
        await using var f = ConFuentes(fuente);
        using var usuario = await ContabTestKit.ClienteConPermisosAsync(f);
        using var ajeno = await ContabTestKit.ClienteConPermisosAsync(f);
        var uid = ContabTestKit.UsuarioIdDe(usuario);
        fuente.UsuarioId = uid;
        fuente.Datos = Enumerable.Range(1, 60).Select(n => Aviso(n)).ToArray();
        try
        {
            var r = await Sync(usuario, new { pageSize = 10 });
            Assert.Equal(60, r.GetProperty("totalSinLeerConocido").GetInt32());
            Assert.Equal(10, r.GetProperty("items").GetArrayLength());
            Assert.False(r.GetProperty("novedades").GetProperty("hayNovedades").GetBoolean());
            var cursor = r.GetProperty("novedades").GetProperty("hastaSecSeguro").GetString();
            var pagina2 = await Sync(usuario, new { pageSize = 10, continuacion = r.GetProperty("siguienteContinuacion").GetString() });
            var claves = r.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("aviso").GetProperty("claveOcurrencia").GetString());
            Assert.Empty(claves.Intersect(pagina2.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("aviso").GetProperty("claveOcurrencia").GetString())));
            fuente.Datos = Enumerable.Range(0, 60).Select(n => Aviso(n)).ToArray();
            var nuevo = await Sync(usuario, new { pageSize = 10, cursorDeteccion = cursor });
            Assert.Equal(60, nuevo.GetProperty("totalSinLeerConocido").GetInt32());
            Assert.Equal(1, nuevo.GetProperty("novedades").GetProperty("cantidadConocida").GetInt32());
            var clave = Aviso(0).ClaveOcurrencia;
            using var lecturaAjena = await ajeno.PostAsJsonAsync($"{Base}/{clave}/leer", new { });
            Assert.Equal(HttpStatusCode.NotFound, lecturaAjena.StatusCode);
            using var leido = await usuario.PostAsJsonAsync($"{Base}/{clave}/leer", new { });
            Assert.Equal(HttpStatusCode.NoContent, leido.StatusCode);
            var despues = await Sync(usuario, new { });
            Assert.Equal(60, despues.GetProperty("totalAccionesConocidas").GetInt32());
            Assert.Equal(59, despues.GetProperty("totalSinLeerConocido").GetInt32());
            fuente.Datos = [];
            Assert.Equal(0, (await Sync(usuario, new { })).GetProperty("totalAccionesConocidas").GetInt32());
        }
        finally { await Limpiar(f, usuario); await Limpiar(f, ajeno); }
    }

    [Fact]
    public async Task Sincronizaciones_concurrentes_conservan_una_fila_por_ocurrencia_y_cursor_confirmado()
    {
        var fuente = new Fuente("compras");
        await using var f = ConFuentes(fuente);
        using var usuario = await ContabTestKit.ClienteConPermisosAsync(f);
        fuente.UsuarioId = ContabTestKit.UsuarioIdDe(usuario);
        fuente.Datos = [Aviso(1)];
        try
        {
            var inicial = await Sync(usuario, new { });
            var cursor = inicial.GetProperty("novedades").GetProperty("hastaSecSeguro").GetString();
            fuente.Datos = Enumerable.Range(1, 62).Select(n => Aviso(n)).ToArray();
            var respuestas = await Task.WhenAll(Sync(usuario, new { cursorDeteccion = cursor }), Sync(usuario, new { cursorDeteccion = cursor }));
            foreach (var r in respuestas) Assert.Equal(61, r.GetProperty("novedades").GetProperty("cantidadConocida").GetInt32());
            using var scope = f.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var filas = await db.AvisosLectura.Where(x => x.UsuarioId == fuente.UsuarioId).ToListAsync();
            Assert.Equal(62, filas.Count);
            Assert.Equal(62, filas.Select(x => x.PrimeraDeteccionSec).Distinct().Count());
            var corte = respuestas.Max(x => long.Parse(x.GetProperty("novedades").GetProperty("hastaSecSeguro").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(filas.Max(x => x.PrimeraDeteccionSec), corte);
            var final = await Sync(usuario, new { cursorDeteccion = corte.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            Assert.False(final.GetProperty("novedades").GetProperty("hayNovedades").GetBoolean());
        }
        finally { await Limpiar(f, usuario); }
    }

    [Fact]
    public async Task Bloqueo_cubre_snapshot_de_fuentes_y_no_solo_la_insercion_de_recibos()
    {
        var fuente = new Fuente("compras");
        await using var f = ConFuentes(fuente);
        using var usuario = await ContabTestKit.ClienteConPermisosAsync(f);
        fuente.UsuarioId = ContabTestKit.UsuarioIdDe(usuario);
        var entroPrimera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberarPrimera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entroSegunda = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var llamadas = 0;
        fuente.Consultar = async ct =>
        {
            if (Interlocked.Increment(ref llamadas) == 1)
            {
                entroPrimera.TrySetResult();
                await liberarPrimera.Task.WaitAsync(ct);
                return new[] { Aviso(1) };
            }
            entroSegunda.TrySetResult();
            return new[] { Aviso(1), Aviso(2) };
        };
        try
        {
            var a = Sync(usuario, new { });
            await entroPrimera.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var b = Sync(usuario, new { cursorDeteccion = "0" });
            var antes = await Task.WhenAny(entroSegunda.Task, Task.Delay(200));
            Assert.NotSame(entroSegunda.Task, antes);
            liberarPrimera.TrySetResult();
            var rA = await a;
            var rB = await b;
            var secA = rA.GetProperty("novedades").GetProperty("hastaSecSeguro").GetString();
            var secB = rB.GetProperty("novedades").GetProperty("hastaSecSeguro").GetString();
            Assert.True(long.Parse(secB!, System.Globalization.CultureInfo.InvariantCulture) > long.Parse(secA!, System.Globalization.CultureInfo.InvariantCulture));
            var despues = await Sync(usuario, new { cursorDeteccion = secA });
            Assert.Equal(1, despues.GetProperty("novedades").GetProperty("cantidadConocida").GetInt32());
        }
        finally { liberarPrimera.TrySetResult(); await Limpiar(f, usuario); }
    }

    [Fact]
    public async Task Fuente_caida_no_persiste_avisos_ni_oculta_error_y_la_recuperacion_genera_secuencia_posterior()
    {
        var sana = new Fuente("compras");
        var falla = new Fuente("almacen") { Fallar = true };
        await using var f = ConFuentes(sana, falla);
        using var usuario = await ContabTestKit.ClienteConPermisosAsync(f);
        sana.UsuarioId = falla.UsuarioId = ContabTestKit.UsuarioIdDe(usuario);
        sana.Datos = [Aviso(1)];
        falla.Datos = [Aviso(2)];
        try
        {
            var primero = await Sync(usuario, new { });
            Assert.False(primero.GetProperty("coberturaCompleta").GetBoolean());
            Assert.Equal(1, primero.GetProperty("totalSinLeerConocido").GetInt32());
            var cursor = primero.GetProperty("novedades").GetProperty("hastaSecSeguro").GetString();
            falla.Fallar = false;
            var recuperado = await Sync(usuario, new { cursorDeteccion = cursor });
            Assert.True(recuperado.GetProperty("coberturaCompleta").GetBoolean());
            Assert.Equal(1, recuperado.GetProperty("novedades").GetProperty("cantidadConocida").GetInt32());
            Assert.Equal("almacen", recuperado.GetProperty("novedades").GetProperty("porFuente")[0].GetProperty("id").GetString());
        }
        finally { await Limpiar(f, usuario); }
    }

    [Fact]
    public async Task Preferencia_solo_del_usuario_entradas_ajenas_rechazadas_y_sucursal_inaccesible_no_filtra_datos()
    {
        await using var f = ConFuentes(new Fuente("compras"));
        using var usuario = await ContabTestKit.ClienteConPermisosAsync(f);
        using var ajeno = await ContabTestKit.ClienteConPermisosAsync(f);
        using var anonimo = f.CreateClient();
        try
        {
            using var noAuth = await anonimo.PostAsJsonAsync($"{Base}/sincronizar", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);
            using var inicial = await usuario.GetAsync($"{Base}/preferencia-sonido");
            Assert.False((await ContabTestKit.Json(inicial)).GetProperty("habilitado").GetBoolean());
            using var cambio = await usuario.PutAsJsonAsync($"{Base}/preferencia-sonido", new { habilitado = true });
            Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);
            using var propia = await usuario.GetAsync($"{Base}/preferencia-sonido");
            Assert.True((await ContabTestKit.Json(propia)).GetProperty("habilitado").GetBoolean());
            using var otra = await ajeno.GetAsync($"{Base}/preferencia-sonido");
            Assert.False((await ContabTestKit.Json(otra)).GetProperty("habilitado").GetBoolean());
            using var intento = await usuario.PutAsJsonAsync($"{Base}/preferencia-sonido", new { habilitado = true, usuarioId = ContabTestKit.UsuarioIdDe(ajeno) });
            Assert.Equal(HttpStatusCode.BadRequest, intento.StatusCode);
            using var scopeInvalido = await usuario.PostAsJsonAsync($"{Base}/sincronizar", new { sucursalId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Forbidden, scopeInvalido.StatusCode);
            using var paginaInvalida = await usuario.PostAsJsonAsync($"{Base}/sincronizar", new { pageSize = 51 });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, paginaInvalida.StatusCode);
        }
        finally { await Limpiar(f, usuario); await Limpiar(f, ajeno); }
    }

    private WebApplicationFactory<Program> ConFuentes(params Fuente[] fuentes) => factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.RemoveAll<IFuenteAvisosUsuario>();
        foreach (var fuente in fuentes) s.AddSingleton<IFuenteAvisosUsuario>(fuente);
    }));

    private static async Task<JsonElement> Sync(HttpClient client, object body)
    {
        using var r = await client.PostAsJsonAsync($"{Base}/sincronizar", body);
        var texto = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {texto}");
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.Clone();
    }

    private static AvisoUsuario Aviso(int n) => new(ClaveAviso.Crear(ContabTestKit.EmpresaBootstrapId, "prueba", "oc", Guid.Empty,
        "firmar", n.ToString(System.Globalization.CultureInfo.InvariantCulture)), "compras", "oc", Guid.Empty, $"OC-FICTICIA-{n}",
        "Autorizar OC ficticia", "Muestra de integración", DateTimeOffset.UnixEpoch.AddMinutes(n), "Accion", "/compras/ordenes");

    private static async Task Limpiar(WebApplicationFactory<Program> f, HttpClient client)
    {
        var uid = ContabTestKit.UsuarioIdDe(client);
        using var scope = f.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        await scope.ServiceProvider.GetRequiredService<CoreDbContext>().AvisosLectura.Where(x => x.UsuarioId == uid).ExecuteDeleteAsync();
        await ContabTestKit.LimpiarClienteAsync(client);
    }

    private sealed class Fuente(string nombre) : IFuenteAvisosUsuario
    {
        public string Nombre => nombre;
        public Guid UsuarioId { get; set; }
        public IReadOnlyList<AvisoUsuario> Datos { get; set; } = [];
        public bool Fallar { get; set; }
        public Func<CancellationToken, Task<IReadOnlyList<AvisoUsuario>>>? Consultar { get; set; }
        public Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct)
        {
            if (Fallar) throw new InvalidOperationException("Origen no disponible (prueba)");
            if (contexto.UsuarioId == UsuarioId && Consultar is not null) return Consultar(ct);
            return Task.FromResult(contexto.UsuarioId == UsuarioId ? Datos : []);
        }
    }
}
