using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Millet.Api.Auth;
using Millet.Api.Seed;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Infrastructure;
using Millet.Compras.Domain.Oc;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Infrastructure.HealthChecks;
using Npgsql;

namespace Millet.Api.IntegrationTests.ComercioExterior;
/// <summary>JWT del API firmado por el emisor real; middleware y carga de permisos de producción.</summary>
public sealed class ExpedientesHttpTests
{
    private static readonly string[] ClientesDemo = ["FIX-CE cliente"];
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    [Fact]
    public async Task Abrir_desde_oc_repetir_concurrencia_y_sucursal()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var admin = await escenario.ClienteAsync(true);
        var creado = await admin.PostAsJsonAsync(Base, escenario.Importacion());
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var dto = await Json(creado);
        var id = dto.GetProperty("expediente").GetProperty("id").GetGuid();
        var etag = creado.Headers.ETag!.Tag;
        Assert.Equal("EXW", dto.GetProperty("incotermCodigo").GetString());
        Assert.Equal("USD", dto.GetProperty("expediente").GetProperty("moneda").GetString());
        var repetido = await admin.PostAsJsonAsync(Base, escenario.Importacion());
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("CE_OC_CON_EXPEDIENTE", (await Json(repetido)).GetProperty("code").GetString());
        using var ninguno = await escenario.ClienteAsync(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await ninguno.GetAsync($"{Base}/{id}")).StatusCode);
        using var otraSucursal = await escenario.ClienteAsync(true, asociar: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await otraSucursal.GetAsync($"{Base}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otraSucursal.PostAsJsonAsync(Base, escenario.Importacion())).StatusCode);
        var body = new
        {
            referencia = "FIX-CE edición",
            incotermId = escenario.Incoterm,
            moneda = "USD",
            responsableUsuarioId = escenario.Responsable
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(otraSucursal, HttpMethod.Patch, $"{Base}/{id}", body, etag)).StatusCode);
        var listadoAjeno = await otraSucursal.GetAsync(Base);
        listadoAjeno.EnsureSuccessStatusCode();
        Assert.Empty((await Json(listadoAjeno)).GetProperty("items").EnumerateArray());
        var buscado = await admin.GetAsync($"{Base}?q=fix-ce");
        buscado.EnsureSuccessStatusCode();
        Assert.Contains((await Json(buscado)).GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        var editado = await Send(admin, HttpMethod.Patch, $"{Base}/{id}", body, etag);
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        var viejo = await Send(admin, HttpMethod.Patch, $"{Base}/{id}", body, etag);
        Assert.Equal(HttpStatusCode.Conflict, viejo.StatusCode);
        Assert.Equal("CONCURRENCY_CONFLICT", (await Json(viejo)).GetProperty("code").GetString());
        Assert.Equal((HttpStatusCode)428, (await Send(admin, HttpMethod.Patch, $"{Base}/{id}", body, null)).StatusCode);
    }

    [Fact]
    public async Task Versiones_conservan_pedimento_anterior_y_faltantes_son_estructurados()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        var etag = creado.Headers.ETag!.Tag;
        var incompleto = await Send(client, HttpMethod.Post, $"{Base}/{id}/revisar-documentos", null, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incompleto.StatusCode);
        var faltantes = await Json(incompleto);
        Assert.Equal("CE_DOCUMENTOS_FALTANTES", faltantes.GetProperty("code").GetString());
        Assert.True(faltantes.GetProperty("faltantes").GetArrayLength() > 0);
        var pedimento = await escenario.TipoAsync("IMP_PEDIMENTO");
        var a = await escenario.AdjuntoAsync(id, pedimento);
        var primera = await Send(client, HttpMethod.Post, $"{Base}/{id}/documentos", new { tipoDocumentoId = pedimento, adjuntoId = a, noAplica = false }, etag);
        primera.EnsureSuccessStatusCode();
        etag = primera.Headers.ETag!.Tag;
        var b = await escenario.AdjuntoAsync(id, pedimento);
        var segunda = await Send(client, HttpMethod.Post, $"{Base}/{id}/documentos", new { tipoDocumentoId = pedimento, adjuntoId = b, noAplica = false, motivo = "FIX-CE rectificación de pedimento" }, etag);
        segunda.EnsureSuccessStatusCode();
        var versiones = (await Json(segunda)).GetProperty("documentos").EnumerateArray().Where(x => x.GetProperty("tipoDocumentoId").GetGuid() == pedimento).ToArray();
        Assert.Equal(2, versiones.Length);
        Assert.Equal(2, versiones[0].GetProperty("estado").GetInt32());
        Assert.Equal(a, versiones[0].GetProperty("adjuntoId").GetGuid());
        Assert.Equal(2, versiones[1].GetProperty("numeroVersion").GetInt32());
        using var soloLeer = await escenario.ClienteAsync(true, soloLeer: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(soloLeer, HttpMethod.Post, $"{Base}/{id}/revisar-documentos", null, segunda.Headers.ETag!.Tag)).StatusCode);
    }

    [Theory]
    [InlineData(EstadoOrdenCompra.Borrador)]
    [InlineData(EstadoOrdenCompra.EnAutorizacionJefeCompras)]
    [InlineData(EstadoOrdenCompra.EnAutorizacionDireccion)]
    [InlineData(EstadoOrdenCompra.Rechazada)]
    [InlineData(EstadoOrdenCompra.Cancelada)]
    [InlineData(EstadoOrdenCompra.Cerrada)]
    [InlineData(EstadoOrdenCompra.CancelacionSolicitada)]
    public async Task Alta_y_selector_rechazan_oc_no_vigente(EstadoOrdenCompra estado)
    {
        await using var escenario = await Escenario.CrearAsync();
        await escenario.EstadoOcAsync(estado);
        using var client = await escenario.ClienteAsync(true);
        var respuesta = await client.PostAsJsonAsync(Base, escenario.Importacion());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal("CE_OC_NO_VIGENTE", (await Json(respuesta)).GetProperty("code").GetString());
        var lista = await client.GetAsync($"{Base}/ordenes-importacion");
        lista.EnsureSuccessStatusCode();
        Assert.Empty((await Json(lista)).EnumerateArray());
    }

    [Fact]
    public async Task Selector_excluye_oc_con_expediente_y_respeta_alcance_de_gestion()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        var lista = await client.GetAsync($"{Base}/ordenes-importacion");
        lista.EnsureSuccessStatusCode();
        Assert.Single((await Json(lista)).EnumerateArray());
        using var lecturaGlobal = await escenario.ClienteAsync(true, asociar: false, leerTodas: true);
        Assert.Empty((await Json(await lecturaGlobal.GetAsync($"{Base}/ordenes-importacion"))).EnumerateArray());
        using var gestionGlobal = await escenario.ClienteAsync(true, asociar: false, gestionarTodas: true);
        Assert.Single((await Json(await gestionGlobal.GetAsync($"{Base}/ordenes-importacion"))).EnumerateArray());
        var creado = await client.PostAsJsonAsync(Base, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        Assert.Empty((await Json(await client.GetAsync($"{Base}/ordenes-importacion"))).EnumerateArray());
    }

    [Fact]
    public async Task Baja_de_adjunto_vigente_marca_revision_pendiente_y_limpia_revisor()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var dto = await Json(creado);
        var id = dto.GetProperty("expediente").GetProperty("id").GetGuid();
        var etag = creado.Headers.ETag!.Tag;
        var adjuntos = new List<Guid>();
        foreach (var requisito in dto.GetProperty("requisitos").EnumerateArray().Where(x => x.GetProperty("obligatorio").GetBoolean()))
        {
            var tipo = requisito.GetProperty("id").GetGuid();
            var adjunto = await escenario.AdjuntoAsync(id, tipo);
            adjuntos.Add(adjunto);
            var registrado = await Send(client, HttpMethod.Post, $"{Base}/{id}/documentos", new { tipoDocumentoId = tipo, adjuntoId = adjunto, noAplica = false }, etag);
            registrado.EnsureSuccessStatusCode();
            etag = registrado.Headers.ETag!.Tag;
        }
        var revisado = await Send(client, HttpMethod.Post, $"{Base}/{id}/revisar-documentos", null, etag);
        revisado.EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.String, (await Json(revisado)).GetProperty("revisadoPorId").ValueKind);
        var baja = await Send(client, HttpMethod.Delete, $"{Base}/{id}/adjuntos/{adjuntos[0]}", new { motivo = "FIX-CE baja del documento revisado" }, null);
        baja.EnsureSuccessStatusCode();
        var detalle = await client.GetAsync($"{Base}/{id}");
        detalle.EnsureSuccessStatusCode();
        var actualizado = await Json(detalle);
        Assert.True(actualizado.GetProperty("expediente").GetProperty("revisionPendiente").GetBoolean());
        Assert.Equal(JsonValueKind.Null, actualizado.GetProperty("revisadoPorId").ValueKind);
        Assert.Single(actualizado.GetProperty("faltantes").EnumerateArray());
        Assert.NotEqual(revisado.Headers.ETag!.Tag, detalle.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Exportacion_sin_ddp_devuelve_error_de_negocio()
    {
        await using var escenario = await Escenario.CrearAsync();
        await escenario.QuitarDdpAsync();
        using var client = await escenario.ClienteAsync(true);
        var respuesta = await client.PostAsJsonAsync(Base, new { tipo = 2, sucursalId = escenario.SucursalId, referencia = "FIX-CE sin DDP", moneda = "USD", responsableUsuarioId = escenario.Responsable, clientes = ClientesDemo });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal("CE_INCOTERM_REQUERIDO", (await Json(respuesta)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Semilla_demo_dos_arranques_conserva_dos_expedientes_y_tres_de_cinco()
    {
        await using var escenario = await Escenario.CrearAsync();
        var primero = await escenario.SnapshotAsync();
        await escenario.SemillaAsync();
        var segundo = await escenario.SnapshotAsync();
        Assert.Equal(primero, segundo);
        Assert.Equal((2, 3, 1, 30, 1), segundo);
    }

    [Fact]
    public async Task Exportacion_propone_ddp_y_cancelacion_conserva_documentos()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        var r = await client.PostAsJsonAsync(Base, new { tipo = 2, sucursalId = escenario.SucursalId, referencia = "FIX-CE solicitud contenedor", moneda = "USD", responsableUsuarioId = escenario.Responsable, clientes = ClientesDemo });
        r.EnsureSuccessStatusCode();
        var dto = await Json(r);
        var id = dto.GetProperty("expediente").GetProperty("id").GetGuid();
        Assert.Equal("DDP", dto.GetProperty("incotermCodigo").GetString());
        var participante = Assert.Single(dto.GetProperty("participantes").EnumerateArray());
        Assert.False(participante.TryGetProperty("empresaId", out _));
        Assert.False(participante.TryGetProperty("createdAt", out _));
        Assert.False(participante.TryGetProperty("aggregateRootId", out _));
        Assert.False(dto.GetProperty("requisitos").EnumerateArray().Single(x => x.GetProperty("codigo").GetString() == "EXP_POD").GetProperty("obligatorio").GetBoolean());
        var isf = await escenario.TipoAsync("EXP_ISF");
        var noAplica = await Send(client, HttpMethod.Post, $"{Base}/{id}/documentos", new { tipoDocumentoId = isf, noAplica = true, motivo = "FIX-CE ruta sin ISF" }, r.Headers.ETag!.Tag);
        noAplica.EnsureSuccessStatusCode();
        var motivoCorto = await Send(client, HttpMethod.Post, $"{Base}/{id}/cancelar", new { motivo = "no" }, noAplica.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, motivoCorto.StatusCode);
        var cancelado = await Send(client, HttpMethod.Post, $"{Base}/{id}/cancelar", new { motivo = "FIX-CE cancelación de prueba" }, noAplica.Headers.ETag!.Tag);
        cancelado.EnsureSuccessStatusCode();
        Assert.Single((await Json(cancelado)).GetProperty("documentos").EnumerateArray());
        Assert.Equal(2, (await Json(cancelado)).GetProperty("expediente").GetProperty("estado").GetInt32());
    }

    [Fact]
    public async Task Hitos_validan_permisos_sucursal_etag_secuencia_documentos_y_cierre_financiero()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var dto = await Json(creado);
        Assert.Equal(15, dto.GetProperty("hitos").GetArrayLength());
        Assert.Equal("solicitado", dto.GetProperty("hitos")[0].GetProperty("clave").GetString());
        Assert.Equal("2026-10-15", dto.GetProperty("hitos")[0].GetProperty("fechaReal").GetString());
        var id = dto.GetProperty("expediente").GetProperty("id").GetGuid();
        var etag = creado.Headers.ETag!.Tag;
        var hoy = FechaContable.HoyLocal(escenario.Ahora);
        var datos = new { clave = "planeado", fechaCompromiso = hoy.AddDays(-1), responsableUsuarioId = escenario.Responsable };
        using var lectura = await escenario.ClienteAsync(true, soloLeer: true);
        using var ajeno = await escenario.ClienteAsync(true, asociar: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(lectura, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/compromiso", datos, etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(ajeno, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/compromiso", datos, etag)).StatusCode);
        var compromiso = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/compromiso", datos, etag);
        compromiso.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/compromiso", datos, etag)).StatusCode);
        etag = compromiso.Headers.ETag!.Tag;
        var sinMotivo = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/compromiso", new { clave = "planeado", fechaCompromiso = hoy, responsableUsuarioId = escenario.Responsable }, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinMotivo.StatusCode);
        Assert.Equal("CE_MOTIVO_INVALIDO", (await Json(sinMotivo)).GetProperty("code").GetString());
        var salto = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/booking-confirmado/real", new { clave = "booking-confirmado", fechaReal = hoy, responsableUsuarioId = escenario.Responsable }, etag);
        Assert.Equal("CE_HITO_ANTERIOR_PENDIENTE", (await Json(salto)).GetProperty("code").GetString());
        foreach (var clave in new[] { "planeado", "booking-confirmado", "contenedor-asignado", "documentacion-en-proceso" })
        {
            var real = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/{clave}/real", new { clave, fechaReal = hoy, responsableUsuarioId = escenario.Responsable }, etag);
            real.EnsureSuccessStatusCode();
            etag = real.Headers.ETag!.Tag;
            if (clave == "planeado")
            {
                var h = (await Json(real)).GetProperty("hitos")[1];
                Assert.Equal(hoy.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), h.GetProperty("fechaCompromiso").GetString());
                Assert.False(h.GetProperty("atrasado").GetBoolean());
            }
        }
        var incompleto = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/documentacion-completa/real", new { clave = "documentacion-completa", fechaReal = hoy, responsableUsuarioId = escenario.Responsable }, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incompleto.StatusCode);
        Assert.Equal("CE_DOCUMENTOS_FALTANTES", (await Json(incompleto)).GetProperty("code").GetString());
        Assert.NotEmpty((await Json(incompleto)).GetProperty("faltantes").EnumerateArray());
        var omitir = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/documentacion-completa/omitir", new { clave = "documentacion-completa", responsableUsuarioId = escenario.Responsable, motivo = "FIX-CE faltan documentos" }, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, omitir.StatusCode);
        Assert.Equal("CE_HITO_NO_OMITIBLE", (await Json(omitir)).GetProperty("code").GetString());
        using (var pendienteScope = escenario.Scope())
        {
            using var bypass = pendienteScope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ce = pendienteScope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var pendiente = await ce.Expedientes.SingleAsync(x => x.Id == id);
            pendiente.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "FIX-CE pendiente", new(100, 0, 0), hoy, null);
            await ce.SaveChangesAsync();
        }
        etag = (await client.GetAsync($"{Base}/{id}")).Headers.ETag!.Tag;
        foreach (var ruta in new[] { "cerrar", "hitos/cerrado/real", "hitos/cerrado/omitir" })
        {
            var cierre = await Send(client, HttpMethod.Post, $"{Base}/{id}/{ruta}", new { clave = "cerrado", fechaReal = hoy, responsableUsuarioId = escenario.Responsable, motivo = "FIX-CE no aplica" }, etag);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, cierre.StatusCode);
            Assert.Equal(ruta.EndsWith("omitir", StringComparison.Ordinal) ? "CE_HITO_NO_OMITIBLE" : "CE_CIERRE_FINANCIERO_PENDIENTE", (await Json(cierre)).GetProperty("code").GetString());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(lectura, HttpMethod.Post, $"{Base}/{id}/cerrar", null, etag)).StatusCode);
        Assert.Equal(4, await escenario.EventosHitoAsync(id));
    }

    [Fact]
    public async Task Trabajo_diario_idempotente_y_nuevo_aviso_al_reprogramar_vencido()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        var ayer = FechaContable.HoyLocal(escenario.Ahora).AddDays(-1);
        var compromiso = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/compromiso", new { clave = "planeado", fechaCompromiso = ayer, responsableUsuarioId = escenario.Responsable }, creado.Headers.ETag!.Tag);
        compromiso.EnsureSuccessStatusCode();
        var etagAnterior = compromiso.Headers.ETag!.Tag;
        await escenario.CorrerAvisosAsync();
        await escenario.CorrerAvisosAsync();
        var detalle = await client.GetAsync($"{Base}/{id}");
        Assert.Single((await Json(detalle)).GetProperty("avisos").EnumerateArray());
        Assert.Equal(etagAnterior, detalle.Headers.ETag!.Tag);
        var patch = await Send(client, HttpMethod.Patch, $"{Base}/{id}", new { referencia = "FIX-CE edición tras aviso", incotermId = escenario.Incoterm, moneda = "USD", responsableUsuarioId = escenario.Responsable }, etagAnterior);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var listado = await Json(await client.GetAsync($"{Base}?atraso=true"));
        Assert.Contains(listado.GetProperty("items").EnumerateArray(), e => e.GetProperty("id").GetGuid() == id && e.GetProperty("avisos").GetInt32() == 1 && e.GetProperty("hitosAtrasados").GetInt32() == 1);
        var nuevo = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/compromiso", new { clave = "planeado", fechaCompromiso = ayer.AddDays(-1), responsableUsuarioId = escenario.Responsable, motivo = "FIX-CE nueva fecha compromiso" }, patch.Headers.ETag!.Tag);
        nuevo.EnsureSuccessStatusCode();
        await escenario.CorrerAvisosAsync();
        await escenario.CorrerAvisosAsync();
        Assert.Equal(2, (await Json(await client.GetAsync($"{Base}/{id}"))).GetProperty("avisos").GetArrayLength());
    }

    [Fact]
    public async Task Hito_se_registra_con_incoterm_inactivo_y_valida_responsable_activo_con_sucursal()
    {
        await using var escenario = await Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        var etag = creado.Headers.ETag!.Tag;
        await escenario.DesactivarIncotermAsync();
        var hoy = FechaContable.HoyLocal(escenario.Ahora);
        var sinSucursal = await escenario.ResponsablePruebaAsync(activo: true, asociar: false);
        var inactivo = await escenario.ResponsablePruebaAsync(activo: false, asociar: true);
        foreach (var responsable in new[] { sinSucursal, inactivo })
        {
            var rechazado = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/real", new { clave = "planeado", fechaReal = hoy, responsableUsuarioId = responsable }, etag);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazado.StatusCode);
            Assert.Equal("CE_RESPONSABLE_REQUERIDO", (await Json(rechazado)).GetProperty("code").GetString());
        }
        var real = await Send(client, HttpMethod.Post, $"{Base}/{id}/hitos/planeado/real", new { clave = "planeado", fechaReal = hoy, responsableUsuarioId = escenario.Responsable }, etag);
        Assert.Equal(HttpStatusCode.OK, real.StatusCode);
        Assert.NotNull((await Json(real)).GetProperty("hitos")[1].GetProperty("fechaReal").GetString());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, object? body, string? etag)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        if (etag is not null)
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request);
    }

    internal sealed class Escenario : IAsyncDisposable
    {
        private static readonly SemaphoreSlim PlantillaMutex = new(1, 1);
        private static Task<string>? _plantilla;
        private bool _conservarBase;
        private readonly string? _authAnterior;
        private readonly string? _conexionAnterior;
        private readonly string _conexionAdmin;
        private readonly string _baseDatos;
        private readonly string _blobs = Path.Combine(Path.GetTempPath(), "ce-blobs-" + Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<Program> _raiz = new();
        private readonly WebApplicationFactory<Program> _factory;
        public DateTimeOffset Ahora => _factory.Services.GetRequiredService<IClock>().UtcNow;
        public Guid Incoterm { get; private set; }
        public Guid Responsable { get; } = DemoSesionSeedHostedService.CapturistaComprasDemoId;
        private Guid Sucursal { get; set; }
        public Guid SucursalId => Sucursal;
        private Guid Oc { get; set; }

        private Escenario(string conexion, string conexionAdmin, string baseDatos, Action<IServiceCollection>? servicios)
        {
            // Program lee Auth:Mode al construir el host. No sustituye esquemas ni handlers de autenticación.
            _conexionAnterior = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
            _conexionAdmin = conexionAdmin;
            _baseDatos = baseDatos;
            _authAnterior = Environment.GetEnvironmentVariable("Auth__Mode");
            _factory = _raiz.WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Mode"] = "EntraId", ["Seed:DemoSesion:Habilitado"] = "true", ["Entra:Proveedor"] = "Graph", ["ConnectionStrings:Postgres"] = conexion, ["Adjuntos:BlobStorage:ConnectionString"] = "", ["Compras:Oc:BlobStorage:ConnectionString"] = "", }));
                b.ConfigureTestServices(s =>
                {
                    s.RemoveAll<IHostedService>();
                    servicios?.Invoke(s);
                    s.AddSingleton<Millet.SharedKernel.Application.Blob.IBlobStoragePort>(new BlobsCe(_blobs));
                    s.AddSingleton<Millet.Compras.Domain.Ports.Blob.IAlmacenarBlobPort>(new BlobsCe(_blobs));
                });
            });
            Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", conexion);
            Environment.SetEnvironmentVariable("Auth__Mode", "EntraId");
        }

        public static async Task<Escenario> CrearAsync(Action<IServiceCollection>? servicios = null)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")))
                throw new InvalidOperationException("Se requiere PostgreSQL aislado.");
            var gate = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")!;
            var nombre = "ce_integration_" + Guid.NewGuid().ToString("N");
            var admin = new NpgsqlConnectionStringBuilder(gate)
            {
                Database = "postgres",
                Pooling = false
            };
            var dbConexion = new NpgsqlConnectionStringBuilder(gate)
            {
                Database = nombre,
                Pooling = false
            };
            var plantilla = await ObtenerPlantillaAsync(gate, admin.ConnectionString);
            await using (var conn = new NpgsqlConnection(admin.ConnectionString))
            {
                await conn.OpenAsync();
                using var identificadores = new NpgsqlCommandBuilder();
                for (var intento = 0; ; intento++)
                {
                    try
                    {
                        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {identificadores.QuoteIdentifier(nombre)} TEMPLATE {identificadores.QuoteIdentifier(plantilla)}", conn);
                        await cmd.ExecuteNonQueryAsync();
                        break;
                    }
                    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ObjectInUse && intento < 3)
                    {
                        await Task.Delay(500);
                    }
                }
            }

            Escenario? e = null;
            try
            {
                e = new Escenario(dbConexion.ConnectionString, admin.ConnectionString, nombre, servicios);
                await e.SemillaAsync();
                using var scope = e._factory.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                e.Sucursal = (await db.Sucursales.SingleAsync(x => x.EmpresaId == DemoSesionSeedHostedService.EmpresaId && x.Clave == "MID")).Id;
                e.Incoterm = (await db.Incoterms.SingleAsync(x => x.Codigo == "EXW")).Id;
                var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var origen = await compras.OrdenesCompra.SingleAsync(x => x.Id == DemoSesionSeedHostedService.Id("DEMO-CE-OC-IMPORTACION"));
                // Otro origen DEMO independiente del expediente que la semilla ya abrió.
                var id = Guid.NewGuid();
                var oc = new Millet.Compras.Domain.Oc.OrdenCompra(id, origen.EmpresaId, Millet.Compras.Domain.Oc.Folio.Parse("OC-FIXC2026-" + Random.Shared.Next(100000, 999999)), 2026, origen.ProveedorId, origen.SucursalDestinoId, origen.CondicionesPagoId, origen.UsoPrincipalId, e.Responsable, e.Responsable, new DateOnly(2026, 10, 15), moneda: "USD", tipoCambio: 18, sinRequisicionPrevia: true, esImportacion: true, motivoSinRequisicion: "FIX-CE prueba aislada");
                oc.ActualizarInformacionImportacion(origen.InformacionImportacion);
                compras.OrdenesCompra.Add(oc);
                await compras.SaveChangesAsync();
                e.Oc = id;
                await e.EstadoOcAsync(EstadoOrdenCompra.Autorizada);
                return e;
            }
            catch
            {
                if (e is not null) await e.DisposeAsync();
                else await BorrarBaseAsync(admin.ConnectionString, nombre);
                throw;
            }
        }

        private static async Task<string> ObtenerPlantillaAsync(string gate, string admin)
        {
            await PlantillaMutex.WaitAsync();
            try
            {
                _plantilla ??= CrearPlantillaAsync(gate, admin);
                return await _plantilla;
            }
            catch
            {
                _plantilla = null; // Una construcción fallida no impide reintentar en la siguiente prueba.
                throw;
            }
            finally
            {
                PlantillaMutex.Release();
            }
        }

        private static async Task<string> CrearPlantillaAsync(string gate, string admin)
        {
            var conexion = new NpgsqlConnectionStringBuilder(gate) { Pooling = false };
            var nombre = "ce_tpl_" + conexion.Database;
            nombre = nombre[..Math.Min(nombre.Length, 63)];
            conexion.Database = nombre;
            var creada = false;
            try
            {
                await using (var conn = new NpgsqlConnection(admin))
                {
                    await conn.OpenAsync();
                    using var identificadores = new NpgsqlCommandBuilder();
                    await using var cmd = new NpgsqlCommand($"CREATE DATABASE {identificadores.QuoteIdentifier(nombre)}", conn);
                    await cmd.ExecuteNonQueryAsync();
                    creada = true;
                }
                await using var escenario = new Escenario(conexion.ConnectionString, admin, nombre, null);
                using (var migraciones = escenario._factory.Services.CreateScope())
                {
                    foreach (var type in escenario._factory.Services.GetRequiredService<MigrationsHealthCheckOptions>().ContextTypes)
                        await ((DbContext)migraciones.ServiceProvider.GetRequiredService(type)).Database.MigrateAsync();
                }
                var catalogos = ActivatorUtilities.CreateInstance<Millet.Compartido.Infrastructure.Seed.CatalogosTestSeedHostedService>(escenario._factory.Services);
                await catalogos.StartAsync(CancellationToken.None);
                var almacen = ActivatorUtilities.CreateInstance<Millet.Almacen.Infrastructure.Seed.AlmacenSeedHostedService>(escenario._factory.Services);
                await almacen.StartAsync(CancellationToken.None);
                escenario._conservarBase = true;
                return nombre;
            }
            catch (Exception ex)
            {
                if (creada)
                {
                    try
                    {
                        await BorrarBaseAsync(admin, nombre);
                    }
                    catch (Exception limpieza)
                    {
                        throw new InvalidOperationException($"No se pudo crear la plantilla CE {nombre} ni limpiar la base fallida.", new AggregateException(ex, limpieza));
                    }
                }
                throw new InvalidOperationException($"No se pudo crear la plantilla CE {nombre}.", ex);
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();
            }
        }

        public IServiceScope Scope() => _factory.Services.CreateScope();

        public object Importacion() => new
        {
            tipo = 1,
            ocId = Oc,
            referencia = "FIX-CE importación",
            responsableUsuarioId = Responsable,
            clientes = Array.Empty<string>()
        };
        public async Task<HttpClient> ClienteAsync(bool permisos, bool asociar = true, bool soloLeer = false, bool leerTodas = false, bool gestionarTodas = false, bool facturacion = false, bool permisosCe = true, bool permisosAf = false)
        {
            using var scope = _factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var id = Guid.NewGuid();
            var usuario = new Usuario(id, id.ToString(), $"ce-{id:N}@example.invalid", "FIX-CE usuario");
            var rol = new Rol(Guid.NewGuid(), "FIX-CE-" + id.ToString("N")[..8], "FIX-CE rol");
            db.Usuarios.Add(usuario);
            db.Roles.Add(rol);
            if (permisos)
                foreach (var p in PermisosCanonicos.Todos.Where(p =>
                    (permisosAf && p.Codigo.StartsWith("activos_fijos.", StringComparison.Ordinal) && !p.Codigo.Contains("todas-sucursales", StringComparison.Ordinal))
                    || (permisosCe && p.Codigo.StartsWith("comercio_exterior.", StringComparison.Ordinal) && !p.Codigo.Contains("todas-sucursales", StringComparison.Ordinal) && (!soloLeer || p.Codigo == PermisosCanonicos.ComercioExteriorExpedientesLeer))
                    || (facturacion && p.Codigo.StartsWith("facturacion.", StringComparison.Ordinal) && !p.Codigo.Contains("todas-sucursales", StringComparison.Ordinal))))
                    db.RolPermisos.Add(new RolPermiso(Guid.NewGuid(), rol.Id, p.Id));
            foreach (var codigo in new[] { leerTodas ? PermisosCanonicos.ComercioExteriorDocumentosLeerTodasSucursales : null, gestionarTodas ? PermisosCanonicos.ComercioExteriorDocumentosGestionarTodasSucursales : null }.OfType<string>())
                db.RolPermisos.Add(new RolPermiso(Guid.NewGuid(), rol.Id, PermisosCanonicos.Todos.Single(p => p.Codigo == codigo).Id));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), usuario.Id, DemoSesionSeedHostedService.EmpresaId, rol.Id));
            if (asociar)
                db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), usuario.Id, Sucursal, DemoSesionSeedHostedService.EmpresaId));
            else
            {
                var otra = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Sucursales.SingleAsync(x => x.EmpresaId == DemoSesionSeedHostedService.EmpresaId && x.Clave == "MTY");
                db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), usuario.Id, otra.Id, DemoSesionSeedHostedService.EmpresaId));
            }
            await db.SaveChangesAsync();
            var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(usuario, DemoSesionSeedHostedService.EmpresaId);
            var client = _factory.CreateClientWithIdempotency();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            return client;
        }

        public async Task EstadoOcAsync(EstadoOrdenCompra estado)
        {
            using var s = _factory.Services.CreateScope();
            using var bypass = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = s.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(x => x.Id == Oc);
            db.Entry(oc).Property(x => x.Estado).CurrentValue = estado;
            await db.SaveChangesAsync();
        }

        public async Task DesactivarIncotermAsync()
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            (await db.Incoterms.SingleAsync(x => x.Id == Incoterm)).CambiarEstatus(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo);
            await db.SaveChangesAsync();
        }

        public async Task<Guid> ResponsablePruebaAsync(bool activo, bool asociar)
        {
            using var s = _factory.Services.CreateScope();
            using var bypass = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = s.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var id = Guid.NewGuid();
            var usuario = new Usuario(id, id.ToString(), $"ce-{id:N}@example.invalid", "FIX-CE responsable");
            if (!activo) usuario.Desactivar();
            var rol = new Rol(Guid.NewGuid(), "FIX-CE-" + id.ToString("N")[..8], "FIX-CE rol");
            db.Usuarios.Add(usuario);
            db.Roles.Add(rol);
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.NewGuid(), id, DemoSesionSeedHostedService.EmpresaId, rol.Id));
            if (asociar) db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), id, Sucursal, DemoSesionSeedHostedService.EmpresaId));
            await db.SaveChangesAsync();
            return id;
        }

        public async Task QuitarDdpAsync()
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var ddp = await db.Incoterms.SingleAsync(x => x.Codigo == "DDP");
            db.Entry(ddp).Property(x => x.Codigo).CurrentValue = "FIX";
            await db.SaveChangesAsync();
        }

        public async Task<Guid> TipoAsync(string codigo)
        {
            using var s = _factory.Services.CreateScope();
            return await s.ServiceProvider.GetRequiredService<CompartidoDbContext>().AdjuntoTiposDocumento.Where(x => x.TipoEntidad == "expediente_ce" && x.Codigo == codigo).Select(x => x.Id).SingleAsync();
        }

        public async Task<Guid> AdjuntoAsync(Guid expediente, Guid tipo)
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var id = Guid.NewGuid();
            db.Adjuntos.Add(new Adjunto(id, "expediente_ce", expediente, DemoSesionSeedHostedService.EmpresaId, tipo, "FIX-CE.pdf", "application/pdf", 10, new string ('a', 64), $"FIX-CE/{id}", null, Responsable, new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero)));
            await db.SaveChangesAsync();
            return id;
        }

        public Task<int> CorrerAvisosAsync() => ActivatorUtilities.CreateInstance<Millet.ComercioExterior.Infrastructure.Workers.AvisosHitosWorker>(_factory.Services).CorrerUnaVezAsync(CancellationToken.None);

        public async Task<int> EventosHitoAsync(Guid expediente)
        {
            using var s = _factory.Services.CreateScope();
            using var bypass = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = s.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var eventos = await db.OutboxEntries.Where(x => x.EventType == "expediente-ce.hito-registrado.v1").ToListAsync();
            return eventos.Count(x => x.Payload.Contains(expediente.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public async Task SemillaAsync()
        {
            var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:DemoSesion:Habilitado"] = "true" }).Build();
            var seed = ActivatorUtilities.CreateInstance<DemoSesionSeedHostedService>(_factory.Services, configuracion);
            await seed.StartAsync(CancellationToken.None);
        }

        public async Task<(int, int, int, int, int)> SnapshotAsync()
        {
            using var s = _factory.Services.CreateScope();
            using var bypass = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = s.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var ids = new[]
            {
                DemoSesionSeedHostedService.Id("DEMO-CE-IMPORTACION"),
                DemoSesionSeedHostedService.Id("DEMO-CE-EXPORTACION")
            };
            return (await db.Expedientes.CountAsync(x => ids.Contains(x.Id)), await db.Documentos.CountAsync(x => x.ExpedienteId == ids[0]), await db.Documentos.CountAsync(x => x.ExpedienteId == ids[1] && x.NoAplica), await db.Hitos.CountAsync(x => ids.Contains(x.ExpedienteId)), await db.Hitos.CountAsync(x => x.ExpedienteId == ids[0] && x.Clave == "planeado" && x.FechaCompromiso == new DateOnly(2026, 10, 14)));
        }

        private static async Task BorrarBaseAsync(string admin, string nombre)
        {
            await using var c = new NpgsqlConnection(admin);
            await c.OpenAsync();
            using var identificadores = new NpgsqlCommandBuilder();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {identificadores.QuoteIdentifier(nombre)} WITH (FORCE)", c);
            await cmd.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                try
                {
                    await _raiz.DisposeAsync();
                }
                finally
                {
                    if (!_conservarBase) await BorrarBaseAsync(_conexionAdmin, _baseDatos);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("Auth__Mode", _authAnterior);
                Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _conexionAnterior);
                if (Directory.Exists(_blobs))
                    Directory.Delete(_blobs, true);
            }
        }
    }

    private sealed class BlobsCe(string carpeta) : Millet.SharedKernel.Application.Blob.IBlobStoragePort, Millet.Compras.Domain.Ports.Blob.IAlmacenarBlobPort
    {
        private string Ruta(string clave) => Path.Combine(carpeta, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clave))));
        public async Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(carpeta);
            await using var f = File.Create(Ruta(clave));
            await contenido.CopyToAsync(f, cancellationToken);
        }

        public Task<Stream> ObtenerStreamAsync(string clave, CancellationToken cancellationToken) => Task.FromResult<Stream>(File.OpenRead(Ruta(clave)));
        public Task EliminarAsync(string clave, CancellationToken cancellationToken)
        {
            File.Delete(Ruta(clave));
            return Task.CompletedTask;
        }

        public async Task<string> SubirAsync(Guid adjuntoId, Stream contenido, string contentType, string nombreArchivo, CancellationToken cancellationToken)
        {
            var clave = adjuntoId.ToString();
            await SubirAsync(clave, contenido, contentType, cancellationToken);
            return clave;
        }
    }
}
