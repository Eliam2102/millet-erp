using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using RqFolio = Millet.Compras.Domain.Folio;
using OcFolio = Millet.Compras.Domain.Oc.Folio;

namespace Millet.Api.IntegrationTests.Notificaciones;

/// <summary>HTTP y PostgreSQL reales; sólo la matriz de estas RQ ficticias es determinista, sin stock externo.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ComprasAvisosHttpTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = ContabTestKit.EmpresaBootstrapId;
    private static readonly Guid Sucursal = TestComprasFixtures.SucursalMid;
    private static readonly Guid Articulo = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid CentroCosto = Guid.Parse("0000000c-0005-0000-0000-000000000001");
    private static readonly DateTimeOffset Fecha = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rq_firma_parcial_retira_n1_avisa_n2_y_excluye_firmante_y_creador()
    {
        var matriz = new MatrizPrueba();
        await using var f = ConMatriz(matriz);
        using var n1 = await ClienteRq(f, PermisosCanonicos.ComprasRequisicionesAutorizarNivel1, PermisosCanonicos.ComprasRequisicionesAutorizarNivel2);
        using var n2 = await ClienteRq(f, PermisosCanonicos.ComprasRequisicionesAutorizarNivel2);
        using var creador = await ClienteRq(f, PermisosCanonicos.ComprasRequisicionesAutorizarNivel1);
        var ids = new List<Guid>();
        try
        {
            var rq = CrearRq(ContabTestKit.UsuarioIdDe(creador)); ids.Add(rq.Id);
            matriz.Asignaciones.Add(rq.Id, (ContabTestKit.UsuarioIdDe(n1), ContabTestKit.UsuarioIdDe(n2)));
            await Guardar(f, db => { db.Requisiciones.Add(rq); return Task.CompletedTask; });
            Assert.Single(await Avisos(n1), a => Documento(a) == rq.Id);
            Assert.DoesNotContain(await Avisos(n2), a => Documento(a) == rq.Id);
            Assert.DoesNotContain(await Avisos(creador), a => Documento(a) == rq.Id);
            await Guardar(f, async db =>
            {
                var actual = await db.Requisiciones.Include(r => r.Autorizaciones).SingleAsync(r => r.Id == rq.Id);
                actual.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, ContabTestKit.UsuarioIdDe(n1), Fecha.AddMinutes(1), RequiereNivel.N1YN2);
            });
            Assert.DoesNotContain(await Avisos(n1), a => Documento(a) == rq.Id);
            var segundo = Assert.Single(await Avisos(n2), a => Documento(a) == rq.Id);
            Assert.Contains("Almacén", segundo.GetProperty("aviso").GetProperty("motivo").GetString());
            Assert.Equal(Fecha.AddMinutes(1), segundo.GetProperty("aviso").GetProperty("fechaOcurrencia").GetDateTimeOffset());
        }
        finally { await Limpiar(f, ids, [], n1, n2, creador); }
    }

    [Fact]
    public async Task Rq_devolucion_cambia_actor_y_reenvio_no_recicla_recibo_leido()
    {
        var matriz = new MatrizPrueba();
        await using var f = ConMatriz(matriz);
        using var firmante = await ClienteRq(f, PermisosCanonicos.ComprasRequisicionesAutorizarNivel1);
        using var creador = await ClienteRq(f, PermisosCanonicos.ComprasRequisicionesEditar);
        var ids = new List<Guid>();
        try
        {
            var rq = CrearRq(ContabTestKit.UsuarioIdDe(creador)); ids.Add(rq.Id);
            matriz.Asignaciones.Add(rq.Id, (ContabTestKit.UsuarioIdDe(firmante), Guid.NewGuid()));
            await Guardar(f, db => { db.Requisiciones.Add(rq); return Task.CompletedTask; });
            var inicial = Assert.Single(await Avisos(firmante), a => Documento(a) == rq.Id);
            var claveInicial = inicial.GetProperty("aviso").GetProperty("claveOcurrencia").GetString();
            using (var leer = await firmante.PostAsJsonAsync($"/api/v1/notificaciones/{claveInicial}/leer", new { SucursalId = Sucursal }))
                Assert.Equal(HttpStatusCode.NoContent, leer.StatusCode);
            Assert.True(Assert.Single(await Avisos(firmante), a => Documento(a) == rq.Id).GetProperty("leido").GetBoolean());
            await Guardar(f, async db =>
            {
                var actual = await db.Requisiciones.SingleAsync(r => r.Id == rq.Id);
                actual.Devolver(ContabTestKit.UsuarioIdDe(firmante), "Completar datos ficticios", Fecha.AddMinutes(2));
            });
            Assert.DoesNotContain(await Avisos(firmante), a => Documento(a) == rq.Id);
            var corregir = Assert.Single(await Avisos(creador), a => Documento(a) == rq.Id);
            Assert.Equal("Completar datos ficticios", corregir.GetProperty("aviso").GetProperty("motivo").GetString());
            await Guardar(f, async db =>
            {
                var actual = await db.Requisiciones.Include(r => r.Lineas).SingleAsync(r => r.Id == rq.Id);
                actual.EnviarAAutorizacion(Fecha.AddMinutes(3));
            });
            Assert.DoesNotContain(await Avisos(creador), a => Documento(a) == rq.Id);
            var reenviado = Assert.Single(await Avisos(firmante), a => Documento(a) == rq.Id);
            Assert.NotEqual(claveInicial, reenviado.GetProperty("aviso").GetProperty("claveOcurrencia").GetString());
            Assert.False(reenviado.GetProperty("leido").GetBoolean());
        }
        finally { await Limpiar(f, ids, [], firmante, creador); }
    }

    [Fact]
    public async Task Oc_niveles_y_cancelacion_seleccionan_actor_distinto_y_retirada_al_resolver()
    {
        using var n1 = await ClienteOc(factory, PermisosCanonicos.ComprasOrdenesAutorizarNivel1, PermisosCanonicos.ComprasOrdenesAutorizarNivel2);
        using var n2 = await ClienteOc(factory, PermisosCanonicos.ComprasOrdenesAutorizarNivel2);
        using var comprador = await ClienteOc(factory, PermisosCanonicos.ComprasOrdenesAutorizarNivel1);
        var ids = new List<Guid>();
        try
        {
            var oc = CrearOc(ContabTestKit.UsuarioIdDe(comprador)); ids.Add(oc.Id);
            await Guardar(factory, db => { db.OrdenesCompra.Add(oc); return Task.CompletedTask; });
            Assert.Single(await Avisos(n1), a => Documento(a) == oc.Id);
            Assert.DoesNotContain(await Avisos(comprador), a => Documento(a) == oc.Id);
            Assert.DoesNotContain(await Avisos(n2), a => Documento(a) == oc.Id);
            await Guardar(factory, async db =>
            {
                var actual = await db.OrdenesCompra.Include(o => o.Autorizaciones).SingleAsync(o => o.Id == oc.Id);
                actual.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, ContabTestKit.UsuarioIdDe(n1), Fecha.AddMinutes(1));
            });
            Assert.DoesNotContain(await Avisos(n1), a => Documento(a) == oc.Id);
            Assert.Single(await Avisos(n2), a => Documento(a) == oc.Id);
            await Guardar(factory, async db =>
            {
                var actual = await db.OrdenesCompra.Include(o => o.Autorizaciones).Include(o => o.Lineas).SingleAsync(o => o.Id == oc.Id);
                actual.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, ContabTestKit.UsuarioIdDe(n2), Fecha.AddMinutes(2));
                actual.RegistrarRecepcionLinea(actual.Lineas.Single().Id, 1, Fecha.AddMinutes(3));
                actual.SolicitarCancelacionConRecepciones(ContabTestKit.UsuarioIdDe(n2), Fecha.AddMinutes(4), Guid.NewGuid(), "Cancelar prueba ficticia");
            });
            Assert.DoesNotContain(await Avisos(n2), a => Documento(a) == oc.Id);
            // Quien solicitó la cancelación no puede ser su segunda firma; otro usuario N2 sí.
            using var resolver = await ClienteOc(factory, PermisosCanonicos.ComprasOrdenesAutorizarNivel2);
            try
            {
                var cancelacion = Assert.Single(await Avisos(resolver), a => Documento(a) == oc.Id);
                Assert.Equal("Cancelación de orden por resolver", cancelacion.GetProperty("aviso").GetProperty("titulo").GetString());
                await Guardar(factory, async db =>
                {
                    var actual = await db.OrdenesCompra.Include(o => o.SolicitudesCancelacion).SingleAsync(o => o.Id == oc.Id);
                    actual.RechazarCancelacion(ContabTestKit.UsuarioIdDe(resolver), Fecha.AddMinutes(5), "Conservar prueba");
                });
                Assert.DoesNotContain(await Avisos(resolver), a => Documento(a) == oc.Id);
            }
            finally { await Limpiar(factory, [], [], resolver); }
        }
        finally { await Limpiar(factory, [], ids, n1, n2, comprador); }
    }

    private WebApplicationFactory<Program> ConMatriz(MatrizPrueba matriz) => factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.RemoveAll<IResolverAutorizadorPort>(); s.AddScoped<IResolverAutorizadorPort>(_ => matriz);
        s.RemoveAll<IRequiereNivelEvaluator>(); s.AddScoped<IRequiereNivelEvaluator, NivelPrueba>();
    }));
    private static Task<HttpClient> ClienteRq(WebApplicationFactory<Program> f, params string[] acciones) =>
        ContabTestKit.ClienteConPermisosAsync(f, [PermisosCanonicos.ComprasRequisicionesLeer, .. acciones,
            PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales, PermisosCanonicos.ComprasRequisicionesGestionarTodasSucursales]);
    private static Task<HttpClient> ClienteOc(WebApplicationFactory<Program> f, params string[] acciones) =>
        ContabTestKit.ClienteConPermisosAsync(f, [PermisosCanonicos.ComprasOrdenesLeer, .. acciones,
            PermisosCanonicos.ComprasOrdenesLeerTodasSucursales, "compras.ordenes.gestionar-todas-sucursales"]);
    private static async Task<List<JsonElement>> Avisos(HttpClient client)
    {
        var items = new List<JsonElement>(); string? siguiente = null;
        do
        {
            using var response = await client.PostAsJsonAsync("/api/v1/notificaciones/sincronizar", new
            { SucursalId = Sucursal, Modulo = "compras", PageSize = 50, Continuacion = siguiente });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var json = await ContabTestKit.Json(response);
            Assert.Equal("Complete", Assert.Single(json.GetProperty("fuentes").EnumerateArray(), a => a.GetProperty("id").GetString() == "compras").GetProperty("estado").GetString());
            items.AddRange(json.GetProperty("items").EnumerateArray().Select(a => a.Clone()));
            siguiente = json.GetProperty("siguienteContinuacion").GetString();
        } while (siguiente is not null);
        return items;
    }
    private static Guid Documento(JsonElement item) => item.GetProperty("aviso").GetProperty("documentoId").GetGuid();
    private static async Task Guardar(WebApplicationFactory<Program> f, Func<ComprasDbContext, Task> accion)
    {
        using var scope = f.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await accion(db); await db.SaveChangesAsync();
    }
    private static async Task Limpiar(WebApplicationFactory<Program> f, IReadOnlyList<Guid> rqs, IReadOnlyList<Guid> ocs, params HttpClient[] clientes)
    {
        using (var scope = f.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            await db.Requisiciones.Where(r => rqs.Contains(r.Id)).ExecuteDeleteAsync();
            await db.OrdenesCompra.Where(o => ocs.Contains(o.Id)).ExecuteDeleteAsync();
            foreach (var cliente in clientes)
            {
                var uid = ContabTestKit.UsuarioIdDe(cliente);
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.aviso_lectura WHERE usuario_id = {uid}");
            }
        }
        foreach (var cliente in clientes) await ContabTestKit.LimpiarClienteAsync(cliente);
    }
    private static Requisicion CrearRq(Guid creador)
    {
        var rq = new Requisicion(Guid.NewGuid(), Empresa, RqFolio.Parse(FolioPrueba("")), 2026,
            Clasificacion.MateriaPrima, Sucursal, Guid.NewGuid(), null, creador, creador, Prioridad.Normal, Fecha);
        rq.AgregarLinea(Guid.NewGuid(), Articulo, 10, "PZA", Money.Mxn(10), centroCostoId: CentroCosto);
        rq.EnviarAAutorizacion(Fecha); return rq;
    }
    private static OrdenCompra CrearOc(Guid comprador)
    {
        var oc = new OrdenCompra(Guid.NewGuid(), Empresa, OcFolio.Parse(FolioPrueba("OC-")), 2026, Guid.NewGuid(), Sucursal,
            Guid.NewGuid(), Guid.NewGuid(), comprador, Guid.NewGuid(), new DateOnly(2026, 11, 11),
            sinRequisicionPrevia: true, motivoSinRequisicion: "Prueba ficticia campana", usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
        oc.AgregarLineaManual(Guid.NewGuid(), Articulo, 10, "PZA", 10, Guid.NewGuid(), centroCostoId: CentroCosto);
        oc.EnviarAAutorizacion(Fecha, requiereNivel2: true); return oc;
    }
    private static string FolioPrueba(string prefijo) => $"{prefijo}AV{(char)Random.Shared.Next('A', 'Z' + 1)}{(char)Random.Shared.Next('A', 'Z' + 1)}2026-{Random.Shared.Next(100000, 999999)}";
    private sealed class MatrizPrueba : IResolverAutorizadorPort
    {
        public Dictionary<Guid, (Guid N1, Guid N2)> Asignaciones { get; } = [];
        private readonly Guid otro = Guid.NewGuid();
        public Task<Guid?> ResolverN1Async(Requisicion r, CancellationToken ct = default) => Task.FromResult<Guid?>(Asignaciones.TryGetValue(r.Id, out var a) ? a.N1 : otro);
        public Task<Guid?> ResolverN2Async(Requisicion r, CancellationToken ct = default) => Task.FromResult<Guid?>(Asignaciones.TryGetValue(r.Id, out var a) ? a.N2 : otro);
    }
    private sealed class NivelPrueba : IRequiereNivelEvaluator
    {
        public Task<RequiereNivel> EvaluarAsync(Requisicion r, CancellationToken ct = default) => Task.FromResult(RequiereNivel.N1YN2);
    }
}
