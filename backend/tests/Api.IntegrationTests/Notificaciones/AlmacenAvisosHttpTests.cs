using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Incidencias;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Application.Notificaciones;
using Millet.SharedKernel.Domain.Incidencias;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Notificaciones;

[Collection(ApiCompartida.Nombre)]
public sealed class AlmacenAvisosHttpTests(WebApplicationFactory<Program> factory)
{
    private const string Base = "/api/v1/notificaciones";

    [Fact]
    public async Task Alcance_de_salidas_permite_seleccionar_sucursal_sin_asociacion()
    {
        using var usuario = await ContabTestKit.ClienteConPermisosAsync(factory,
            PermisosCanonicos.AlmacenSalidasLeerTodas,
            PermisosCanonicos.AlmacenSalidasLeerTodasSucursales,
            PermisosCanonicos.AlmacenSalidasPorVale);
        try
        {
            var uid = ContabTestKit.UsuarioIdDe(usuario);
            var mid = await SucursalMidAsync(factory);
            using (var scope = factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
                Assert.False(await identidad.UsuarioSucursales.AnyAsync(x => x.UsuarioId == uid && x.SucursalId == mid));
                Assert.False(await identidad.UsuarioEmpresaAlcances.AnyAsync(x => x.UsuarioId == uid && x.EmpresaId == ContabTestKit.EmpresaBootstrapId && x.EsCorporativo));
            }

            using var respuesta = await usuario.PostAsJsonAsync($"{Base}/sincronizar", new { sucursalId = mid });
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var json = await ContabTestKit.Json(respuesta);
            Assert.True(json.GetProperty("coberturaCompleta").GetBoolean());
            Assert.Equal(mid, json.GetProperty("contexto").GetProperty("sucursalId").GetGuid());
        }
        finally { await LimpiarAsync(factory, usuario); }
    }

    [Fact]
    public async Task Incidencia_real_del_vale_y_aviso_equivalente_comparten_una_sola_ocurrencia()
    {
        var fuenteAlmacen = new AvisoEquivalenteAlmacen();
        await using var f = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            // Conserva las fuentes reales; la muestra añade solo el aviso equivalente de Almacén.
            s.AddSingleton<IFuenteAvisosUsuario>(fuenteAlmacen);
        }));
        using var usuario = await ContabTestKit.ClienteConPermisosAsync(f);
        var uid = ContabTestKit.UsuarioIdDe(usuario);
        Guid? incidenciaId = null;
        try
        {
            var mid = await SucursalMidAsync(f);
            var valeId = Guid.NewGuid();
            var solicitud = IncidenciasAlmacen.ValeSinRegularizar(valeId, "VALE-FICTICIO-AVISOS", mid, uid,
                "El vale ficticio venció sin requisición regularizadora.", "VALE_SIN_REGULARIZAR",
                "Vincula la requisición autorizada del vale ficticio.");
            var incidencia = Incidencia.Abrir(new Incidencia.Datos(ContabTestKit.EmpresaBootstrapId,
                solicitud.Modulo, solicitud.TipoDocumento, solicitud.DocumentoId, solicitud.DocumentoEtiqueta,
                solicitud.SucursalId, solicitud.Paso, solicitud.CodigoMotivo, solicitud.Motivo, solicitud.Detalle,
                solicitud.DetalleClave, uid, "Usuario de prueba de avisos", solicitud.FechaCompromiso,
                solicitud.PasoReanudacion, solicitud.RutaReanudacion), null, null, DateTimeOffset.UtcNow);
            incidenciaId = incidencia.Id;
            Assert.Equal(PasosIncidencia.AlmValeRectificar, incidencia.Paso);
            Assert.Equal(PasosIncidencia.AlmValeRegularizar, incidencia.PasoReanudacion);
            using (var scope = f.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                db.Incidencias.Add(incidencia);
                await db.SaveChangesAsync();
            }
            var clave = ClaveAviso.Crear(ContabTestKit.EmpresaBootstrapId, "almacen", "movimiento", valeId, "regularizar-vale", "1");

            // Primero acredita la clave producida por IncidenciasFuenteAvisos REAL, sin la muestra equivalente.
            var primero = await SincronizarAsync(usuario);
            Assert.Equal(1, primero.GetProperty("totalAccionesConocidas").GetInt32());
            var items = primero.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(clave, items[0].GetProperty("aviso").GetProperty("claveOcurrencia").GetString());

            fuenteAlmacen.UsuarioId = uid;
            fuenteAlmacen.Aviso = new AvisoUsuario(clave, "almacen", "movimiento", valeId,
                "VALE-FICTICIO-AVISOS", "Regulariza el vale ficticio", solicitud.Motivo, DateTimeOffset.UtcNow,
                "Accion", $"/almacen/salidas/{valeId}", mid);
            var fusionado = await SincronizarAsync(usuario);
            Assert.Equal(1, fusionado.GetProperty("totalAccionesConocidas").GetInt32());
            Assert.Equal(1, fusionado.GetProperty("totalSinLeerConocido").GetInt32());
            Assert.Equal(1, fusionado.GetProperty("items").GetArrayLength());
            Assert.Equal(clave, fusionado.GetProperty("items")[0].GetProperty("aviso").GetProperty("claveOcurrencia").GetString());
            using var lectura = await usuario.PostAsJsonAsync($"{Base}/{clave}/leer", new { });
            Assert.Equal(HttpStatusCode.NoContent, lectura.StatusCode);
            var leido = await SincronizarAsync(usuario);
            Assert.Equal(0, leido.GetProperty("totalSinLeerConocido").GetInt32());
            Assert.Equal(1, leido.GetProperty("totalAccionesConocidas").GetInt32());
            using var verificar = f.Services.CreateScope();
            using var verBypass = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            Assert.Equal(EstadoIncidencia.Abierta, (await verificar.ServiceProvider.GetRequiredService<CompartidoDbContext>()
                .Incidencias.AsNoTracking().SingleAsync(x => x.Id == incidencia.Id)).Estado);
        }
        finally
        {
            if (incidenciaId is Guid id)
            {
                using var scope = f.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Incidencias.Where(x => x.Id == id).ExecuteDeleteAsync();
            }
            await LimpiarAsync(f, usuario);
        }
    }

    private static async Task<Guid> SucursalMidAsync(WebApplicationFactory<Program> f)
    {
        using var scope = f.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Sucursales
            .Where(x => x.EmpresaId == ContabTestKit.EmpresaBootstrapId && x.Clave == "MID").Select(x => x.Id).SingleAsync();
    }

    private static async Task<JsonElement> SincronizarAsync(HttpClient usuario)
    {
        using var respuesta = await usuario.PostAsJsonAsync($"{Base}/sincronizar", new { });
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.True(respuesta.IsSuccessStatusCode, $"{respuesta.StatusCode}: {texto}");
        using var json = JsonDocument.Parse(texto);
        var resultado = json.RootElement.Clone();
        Assert.True(resultado.GetProperty("coberturaCompleta").GetBoolean());
        return resultado;
    }

    private static async Task LimpiarAsync(WebApplicationFactory<Program> f, HttpClient usuario)
    {
        var uid = ContabTestKit.UsuarioIdDe(usuario);
        using (var scope = f.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"DELETE FROM core.aviso_lectura WHERE usuario_id = {uid}");
        }
        await ContabTestKit.LimpiarClienteAsync(usuario);
    }

    private sealed class AvisoEquivalenteAlmacen : IFuenteAvisosUsuario
    {
        public string Nombre => "almacen-prueba";
        public Guid UsuarioId { get; set; }
        public AvisoUsuario? Aviso { get; set; }
        public Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AvisoUsuario>>(contexto.UsuarioId == UsuarioId
                && contexto.EmpresaId == ContabTestKit.EmpresaBootstrapId && Aviso is not null ? [Aviso] : []);
    }
}
