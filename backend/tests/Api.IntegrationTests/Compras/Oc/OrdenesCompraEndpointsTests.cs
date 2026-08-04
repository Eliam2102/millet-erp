using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// Tests integration de <c>POST /api/v1/compras/ordenes</c> y
/// <c>GET /api/v1/compras/ordenes/{id}</c> (F1-PR2). Sustituye los
/// tests del smoke endpoint borrado.
///
/// Cubre: 401 sin token, 403 sin permiso, 201 con permiso + folio
/// formateado (<c>OC-MID2026-NNNNNN</c>), 422 con proveedor inactivo,
/// 404 ProblemDetails, ETag presente en GET, folios consecutivos para
/// misma (empresa, sucursal, año).
///
/// El cliente NO envía <c>EmpresaId</c> ni <c>CompradorTitularId</c>:
/// el handler los resuelve del JWT.
///
/// Caveat: cada corrida crea filas en <c>compras.ordenes_compra</c> y
/// <c>compras.folio_secuencias_oc</c>. Sin cleanup. <c>SucursalIdFija</c>
/// es constante para que las corridas reusen la misma fila de
/// folio_secuencias_oc y los folios sean consecutivos.
/// </summary>
public class OrdenesCompraEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EndpointBase = "/api/v1/compras/ordenes";
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";

    // SucursalId fijo. El folio se forma con SucursalCodigo+Anio+secuencial;
    // mantener constante permite folios consecutivos entre corridas.
    private static readonly Guid SucursalIdFija = Guid.Parse("00000003-0002-0000-0000-000000000001");

    // Proveedores del seed compartido (CatalogosTestSeedHostedService).
    private static readonly Guid ProveedorActivoId = Guid.Parse("00000005-0001-0000-0000-000000000001");
    private static readonly Guid ProveedorInactivoId = Guid.Parse("00000005-0001-0000-0000-000000000005");
    private static readonly Guid ProveedorInexistenteId = Guid.Parse("00000005-0001-0000-0000-FFFFFFFFFFFF");

    private readonly WebApplicationFactory<Program> _factory;

    public OrdenesCompraEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --------- POST /api/v1/compras/ordenes ---------

    [Fact]
    public async Task Crear_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Crear_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(EndpointBase, ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Fase E PR3: endpoint ABIERTO de CC-Máquina para la línea manual de OC.
    //    Gate = compras.ordenes.crear-sin-rq. El comportamiento de la query
    //    (todas las activas, sin filtro de alcance) lo cubre PR1
    //    (SelectorAbiertoYReadPortTests); aquí se prueba el GATE + el wiring. ──
    private const string Dim3AbiertoEndpoint = "/api/v1/compras/ordenes/dim3/buscar";

    [Fact]
    public async Task Dim3BuscarAbierto_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(Dim3AbiertoEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dim3BuscarAbierto_Sin_CrearSinRq_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(Dim3AbiertoEndpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dim3BuscarAbierto_Con_CrearSinRq_Retorna_200_ConArray()
    {
        // SuperAdmin tiene crear-sin-rq → 200 + array (activas, sin alcance).
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(Dim3AbiertoEndpoint + "?limit=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task Crear_Con_SuperAdmin_Retorna_201_Con_Folio_Formateado()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(EndpointBase, ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var folio = body.GetProperty("folio").GetString();
        Assert.NotNull(folio);
        Assert.Matches(@"^OC-MID2026-\d{6}$", folio);
        Assert.Equal(0, body.GetProperty("estado").GetInt32()); // Borrador = 0
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Crear_Con_Request_Invalido_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        // SucursalCodigo de 1 letra → validator devuelve 400.
        var body = ValidBody() with { SucursalCodigo = "M" };

        var response = await client.PostAsJsonAsync(EndpointBase, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("application/problem+json",
            response.Content.Headers.ContentType?.MediaType ?? string.Empty);
    }

    [Fact]
    public async Task Crear_Con_Proveedor_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = ValidBody() with { ProveedorId = ProveedorInexistenteId };

        var response = await client.PostAsJsonAsync(EndpointBase, body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("PROVEEDOR_NO_ENCONTRADO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Crear_Con_Proveedor_Inactivo_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = ValidBody() with { ProveedorId = ProveedorInactivoId };

        var response = await client.PostAsJsonAsync(EndpointBase, body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("PROVEEDOR_INACTIVO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Crear_Sin_Rq_Previa_Sin_Motivo_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = null,
        };

        var response = await client.PostAsJsonAsync(EndpointBase, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Crear_Genera_Folios_Consecutivos_Para_Misma_Sucursal_Anio()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = ValidBody();

        var first = await client.PostAsJsonAsync(EndpointBase, body);
        var second = await client.PostAsJsonAsync(EndpointBase, body);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var folioA = (await ReadJsonAsync(first)).GetProperty("folio").GetString()!;
        var folioB = (await ReadJsonAsync(second)).GetProperty("folio").GetString()!;
        Assert.NotEqual(folioA, folioB);
        // Folio: OC-MID2026-NNNNNN. El secuencial es el último segmento.
        var nA = int.Parse(folioA.Split('-')[2]);
        var nB = int.Parse(folioB.Split('-')[2]);
        Assert.Equal(nA + 1, nB);
    }

    [Fact]
    public async Task Crear_Sin_IdempotencyKey_Retorna_400()
    {
        var rawClient = _factory.CreateClient();
        var token = await FakeLoginAsync(rawClient, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        rawClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await rawClient.PostAsJsonAsync(EndpointBase, ValidBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
    }

    // --------- GET /api/v1/compras/ordenes/{id} ---------

    [Fact]
    public async Task Obtener_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{EndpointBase}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Obtener_Id_Inexistente_Retorna_404_Con_ProblemDetails()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync($"{EndpointBase}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("application/problem+json",
            response.Content.Headers.ContentType?.MediaType ?? string.Empty);

        var body = await ReadJsonAsync(response);
        Assert.Equal("ORDEN_COMPRA_NO_ENCONTRADA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Obtener_Tras_Crear_Detail_Incluye_Lineas_Vacias_Array()
    {
        // UF2-PR3-a: el response del detalle DEBE incluir un array
        // `lineas` (vacío después de POST /ordenes — recién creada
        // sin líneas todavía). Este test gate la regresión: si
        // alguien quita el campo del DTO o el .Include() del handler,
        // el frontend del editor (UF2-PR3-b) no tendrá nada que
        // listar.
        var client = await CreateSuperAdminClientAsync();

        var createResp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        createResp.EnsureSuccessStatusCode();
        var createdId = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        var getResp = await client.GetAsync($"{EndpointBase}/{createdId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var body = await ReadJsonAsync(getResp);

        Assert.True(
            body.TryGetProperty("lineas", out var lineas),
            "El detalle de OC debe incluir el campo `lineas` (array, posiblemente vacío). " +
            "Sin este campo, el editor de UF2-PR3-b no puede listar lo que el comprador agrega.");
        Assert.Equal(JsonValueKind.Array, lineas.ValueKind);
        // Recién creada sin líneas: array vacío.
        Assert.Equal(0, lineas.GetArrayLength());
    }

    [Fact]
    public async Task Obtener_Detalle_Resuelve_Folio_De_RQ_State_Agnostic()
    {
        // Fix A (ADR-0042, matiz intra-módulo): una línea de OC heredada de
        // una RQ debe exponer el folio HUMANO de la RQ (p. ej.
        // MID2026-NNNNNN) en `requisicionFolio`, no solo el GUID. Wiring
        // end-to-end: el handler resuelve el folio con query directa
        // intra-Compras a `requisiciones`, STATE-AGNOSTIC (la RQ se siembra
        // Cerrada — estado que la lectura operativa rechaza — y aun así el
        // folio resuelve). Gate de regresión del bug reportado en prod
        // (la línea mostraba "Desde RQ-<guid8>").
        var client = await CreateSuperAdminClientAsync();

        // 1) OC real en la empresa del superadmin (Borrador, sinRequisicionPrevia=false).
        var createResp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        createResp.EnsureSuccessStatusCode();
        var ocId = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        // 2) Sembrar una RQ Cerrada (misma empresa que la OC) y colgar una
        //    línea de OC heredada de ella, vía DbContext con bypass del filtro
        //    de empresa. Folio único para no chocar con el UNIQUE
        //    (empresa, año, folio) en la BD dev compartida sin cleanup.
        var folioRq = $"MID2026-{(uint)Guid.NewGuid().GetHashCode() % 1_000_000:D6}";
        Guid rqId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var oc = await db.OrdenesCompra
                .Include(o => o.Lineas)
                .FirstAsync(o => o.Id == ocId);

            var rq = CrearRqCerrada(oc.EmpresaId, folioRq);
            rqId = rq.Id;
            db.Requisiciones.Add(rq);

            oc.AgregarLineaDesdeRequisicion(
                lineaId: Guid.CreateVersion7(),
                articuloId: Guid.CreateVersion7(),
                cantidad: 10m,
                unidadMedida: "PZA",
                precioUnitario: 50m,
                departamentoSolicitanteId: Guid.CreateVersion7(),
                requisicionId: rq.Id,
                lineaRequisicionId: rq.Lineas.Single().Id);

            await db.SaveChangesAsync();
        }

        // 3) GET detalle → la línea heredada trae el folio resuelto.
        var getResp = await client.GetAsync($"{EndpointBase}/{ocId}");
        var detalle = await ReadJsonAsync(getResp);
        if (getResp.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"GET detalle devolvió {(int)getResp.StatusCode}. Body: {detalle.GetRawText()}");
        }

        var lineas = detalle.GetProperty("lineas");
        Assert.Equal(JsonValueKind.Array, lineas.ValueKind);

        JsonElement? lineaDeRq = null;
        foreach (var l in lineas.EnumerateArray())
        {
            if (l.TryGetProperty("requisicionId", out var rid)
                && rid.ValueKind == JsonValueKind.String
                && Guid.Parse(rid.GetString()!) == rqId)
            {
                lineaDeRq = l;
                break;
            }
        }

        Assert.True(lineaDeRq is not null,
            "El detalle no trae la línea heredada de la RQ sembrada.");
        Assert.Equal(folioRq, lineaDeRq.Value.GetProperty("requisicionFolio").GetString());
    }

    [Fact]
    public async Task Obtener_Detalle_Resuelve_Etiqueta_Articulo_Y_Proveedor()
    {
        // REGRESIÓN PRE-EXISTENTE (ADR-0042 addendum): el detalle debe traer
        // la etiqueta del PROVEEDOR (cabecera) y del ARTÍCULO (por línea)
        // resueltas SERVER-SIDE vía read-port batch (sin tope de página), no
        // el UUID. Gate del bug id→UUID a escala. Wiring end-to-end de los
        // puertos IProveedorReadPort/IArticuloReadPort de Compras.
        var client = await CreateSuperAdminClientAsync();

        // 1) OC real (proveedorId = ProveedorActivoId, sembrado y resoluble).
        var createResp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        createResp.EnsureSuccessStatusCode();
        var ocId = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        // 2) Sembrar un artículo en compartido + colgar una línea de OC que lo
        //    use (heredada de una RQ Cerrada, mismo recipe que el test de folio).
        var articuloId = Guid.CreateVersion7();
        var claveArticulo = $"ZZZ-INT-{(uint)Guid.NewGuid().GetHashCode() % 1_000_000:D6}";
        var folioRq = $"MID2026-{(uint)Guid.NewGuid().GetHashCode() % 1_000_000:D6}";
        using (var scope = _factory.Services.CreateScope())
        {
            var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            compartido.Set<Articulo>().Add(new Articulo(
                articuloId, claveArticulo, "Artículo de integración", "PZA",
                Naturaleza.Estandar));
            await compartido.SaveChangesAsync();

            var oc = await db.OrdenesCompra
                .Include(o => o.Lineas)
                .FirstAsync(o => o.Id == ocId);
            var rq = CrearRqCerrada(oc.EmpresaId, folioRq);
            db.Requisiciones.Add(rq);
            oc.AgregarLineaDesdeRequisicion(
                lineaId: Guid.CreateVersion7(),
                articuloId: articuloId,
                cantidad: 10m,
                unidadMedida: "PZA",
                precioUnitario: 50m,
                departamentoSolicitanteId: Guid.CreateVersion7(),
                requisicionId: rq.Id,
                lineaRequisicionId: rq.Lineas.Single().Id);
            await db.SaveChangesAsync();
        }

        // 3) GET detalle → cabecera con proveedor resuelto + línea con artículo resuelto.
        var getResp = await client.GetAsync($"{EndpointBase}/{ocId}");
        var detalle = await ReadJsonAsync(getResp);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        // Cabecera: proveedor resuelto server-side (no null/UUID).
        Assert.False(
            string.IsNullOrWhiteSpace(detalle.GetProperty("proveedorRazonSocial").GetString()),
            "proveedorRazonSocial debería venir resuelto en el detalle.");

        // Línea: artículo resuelto (clave + nombre exactos del sembrado).
        JsonElement? lineaArt = null;
        foreach (var l in detalle.GetProperty("lineas").EnumerateArray())
        {
            if (l.TryGetProperty("articuloId", out var aid)
                && aid.ValueKind == JsonValueKind.String
                && Guid.Parse(aid.GetString()!) == articuloId)
            {
                lineaArt = l;
                break;
            }
        }
        Assert.True(lineaArt is not null, "El detalle no trae la línea con el artículo sembrado.");
        Assert.Equal(claveArticulo, lineaArt.Value.GetProperty("articuloClave").GetString());
        Assert.Equal("Artículo de integración", lineaArt.Value.GetProperty("articuloNombre").GetString());
    }

    [Fact]
    public async Task Obtener_Tras_Crear_Retorna_200_Con_ETag()
    {
        var client = await CreateSuperAdminClientAsync();

        var createResp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        createResp.EnsureSuccessStatusCode();
        var createdId = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        var getResp = await client.GetAsync($"{EndpointBase}/{createdId}");

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        Assert.NotNull(getResp.Headers.ETag);
        Assert.Matches(@"^""\d+""$", getResp.Headers.ETag!.Tag);

        var body = await ReadJsonAsync(getResp);
        Assert.Equal(createdId, body.GetProperty("id").GetGuid());
        Assert.Equal("MXN", body.GetProperty("moneda").GetString());
        Assert.Equal(0, body.GetProperty("estado").GetInt32());
        // Sub-estados deben venir en 0 (SinX) post-creación.
        Assert.Equal(0, body.GetProperty("subEstadoRecepcion").GetInt32());
        Assert.Equal(0, body.GetProperty("subEstadoFacturacion").GetInt32());
        Assert.Equal(0, body.GetProperty("subEstadoPago").GetInt32());
        // FechaContabilizacion null mientras no esté autorizada.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("fechaContabilizacion").ValueKind);
    }

    [Fact]
    public async Task Crear_Con_Fecha_DateOnly_RoundTrip_Conserva_El_Dia_Exacto()
    {
        // Regresión ADR-0040: la cabecera de OC usa DateOnly/date. El picker
        // del front manda "YYYY-MM-DD". Antes (DateTimeOffset/timestamptz) un
        // date-only tronaba con 500 (Npgsql, offset != 0) en máquinas en hora
        // de México, o devolvía el día anterior en servidores UTC. Este test
        // gatea esa regresión: el día va y vuelve exacto, serializado como
        // date-only puro (sin hora ni zona). Si alguien revierte el tipo a
        // DateTimeOffset, el response trae "2026-06-07T00:00:00+00:00" y estas
        // aserciones fallan.
        var client = await CreateSuperAdminClientAsync();

        var body = ValidBody() with
        {
            FechaDocumento = new DateOnly(2026, 6, 7),
            FechaEntregaEsperada = new DateOnly(2026, 6, 9),
        };

        var createResp = await client.PostAsJsonAsync(EndpointBase, body);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createdId = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        var getResp = await client.GetAsync($"{EndpointBase}/{createdId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var detalle = await ReadJsonAsync(getResp);

        Assert.Equal("2026-06-07", detalle.GetProperty("fechaDocumento").GetString());
        Assert.Equal("2026-06-09", detalle.GetProperty("fechaEntregaEsperada").GetString());
    }

    // --------- GET /api/v1/compras/ordenes (bandeja) ---------

    [Fact]
    public async Task Listar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{EndpointBase}?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Sin_Query_String_Retorna_200_Aplicando_Defaults()
    {
        // Caso del frontend `OrdenesCompraLayout` cuando el master-detail
        // arranca sin search params: el GET llega como
        // `/api/v1/compras/ordenes` SIN `?page=...&pageSize=...`. El
        // endpoint debe responder 200 aplicando los defaults internos
        // (page=1, pageSize=50) — antes truenaba con
        // `BadHttpRequestException: Required parameter "int page" was not
        // provided` porque page/pageSize no tenían default value en la
        // signature de la lambda. Fix: `int page = 0, int pageSize = 0`.
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(EndpointBase);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("items").ValueKind);
        // Defaults aplicados: page=1, pageSize=50.
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Pendientes_Sin_Query_String_Retorna_200_Aplicando_Defaults()
    {
        // Mismo bug en /pendientes-autorizacion: también requiere
        // page/pageSize con default value en la lambda.
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync($"{EndpointBase}/pendientes-autorizacion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PartidasAbiertas_Sin_Query_String_Retorna_200_Aplicando_Defaults()
    {
        // Mismo bug en /partidas-abiertas. El usuario necesita el permiso
        // `compras.ordenes.reportes-partidas-abiertas` (incluido en el
        // SuperAdmin del bootstrap).
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync($"{EndpointBase}/partidas-abiertas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Sin_OCs_Retorna_200_Items_Vacio()
    {
        // Empresa nueva sin OCs creadas todavía: el endpoint debe
        // responder 200 con array vacío y page/pageSize/totalCount
        // del response shape (ListarOrdenesCompraResponse). Este test
        // por sí solo es el gate contra el bug del ORDER BY: aunque
        // no haya filas, EF compila el query (incluye el OrderBy del
        // VO Folio) y truena en client-side translation si la
        // expresión no es traducible.
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync($"{EndpointBase}?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("items").ValueKind);
        Assert.True(body.GetProperty("page").GetInt32() >= 1);
        Assert.True(body.GetProperty("pageSize").GetInt32() >= 1);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 0);
    }

    [Fact]
    public async Task Listar_Tras_Crear_Devuelve_OC_Creada_En_Items()
    {
        // Reproduce el escenario del bug F1-PR2 → F6-PR3: el handler
        // ordena por <c>Folio.Valor</c> que EF Core no puede traducir
        // si Folio es VO con HasConversion. El fix usa
        // <c>EF.Property&lt;string&gt;(o, "Folio")</c>. Este test crea
        // una OC y ejercita el ORDER BY contra la fila real.
        var client = await CreateSuperAdminClientAsync();

        var createResp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        createResp.EnsureSuccessStatusCode();
        var createdId = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        var listResp = await client.GetAsync($"{EndpointBase}?page=1&pageSize=200");

        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var body = await ReadJsonAsync(listResp);
        var items = body.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(items.GetArrayLength() >= 1);
        var encontrada = false;
        foreach (var it in items.EnumerateArray())
        {
            if (it.GetProperty("id").GetGuid() == createdId)
            {
                encontrada = true;
                var folio = it.GetProperty("folio").GetString();
                Assert.NotNull(folio);
                Assert.Matches(@"^OC-MID2026-\d{6}$", folio);
                // Resumen expone sucursalId (aditivo, proyectado de
                // SucursalDestinoId). La captura de factura lo usa para
                // derivar la sucursal al elegir la OC sin pedirla a mano.
                Assert.Equal(SucursalIdFija, it.GetProperty("sucursalId").GetGuid());
                // proveedorNombre resuelto server-side (read-port batch, ADR-0042):
                // la OC se crea con ProveedorActivoId (seed) → debe traer la razón
                // social, no null/vacío. Gate del fix del cap-500 en el OcSelector.
                var provNombre = it.GetProperty("proveedorNombre").GetString();
                Assert.False(
                    string.IsNullOrWhiteSpace(provNombre),
                    "proveedorNombre debería venir resuelto (razón social) en el resumen de la bandeja.");
                break;
            }
        }
        Assert.True(encontrada,
            "La OC recién creada no aparece en la bandeja paginada — " +
            "regresión del ORDER BY (ver bug fix compras/oc-fix-listar-orderby-folio).");
    }

    [Fact]
    public async Task Listar_Con_Filtro_Estado_Borrador_Devuelve_OC_Recien_Creada()
    {
        // Filtra por estado=Borrador (0). La OC recién creada debe
        // aparecer en este bucket. Cubre el path con WHERE sobre
        // estado además del ORDER BY.
        var client = await CreateSuperAdminClientAsync();

        await client.PostAsJsonAsync(EndpointBase, ValidBody());

        var response = await client.GetAsync(
            $"{EndpointBase}?estado=0&page=1&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
        foreach (var it in body.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(0, it.GetProperty("estado").GetInt32());
        }
    }

    [Fact]
    public async Task Listar_Con_SoloConPendienteRecepcion_Excluye_Completa_Conserva_SinRecepcion_Y_Parcial()
    {
        // Fix del selector OC en Nueva recepción: soloConPendienteRecepcion=true
        // lista SOLO OCs con SubEstadoRecepcion != Completa. Sembramos 3 OCs en
        // los 3 sub-estados (vía UPDATE del sub-estado materializado) y
        // verificamos el corte. Aislado por ids; la query de control SIN filtro
        // prueba que la Completa SÍ está en rango → su ausencia con filtro la
        // causa el WHERE, no el tope de página.
        var client = await CreateSuperAdminClientAsync();

        var idSinRecepcion = await CrearOcAsync(client);
        var idParcial = await CrearOcAsync(client);
        var idCompleta = await CrearOcAsync(client);

        await SetSubEstadoRecepcionAsync(idParcial, 1);   // Parcial
        await SetSubEstadoRecepcionAsync(idCompleta, 2);  // Completa
        // idSinRecepcion queda en 0 (SinRecepcion, default).

        // Con filtro: las pendientes (0 y 1) presentes, la Completa (2) ausente.
        var filtrada = await client.GetAsync(
            $"{EndpointBase}?soloConPendienteRecepcion=true&pageSize=200");
        filtrada.EnsureSuccessStatusCode();
        var idsFiltradas = await IdsDeAsync(filtrada);
        Assert.Contains(idSinRecepcion, idsFiltradas);
        Assert.Contains(idParcial, idsFiltradas);
        Assert.DoesNotContain(idCompleta, idsFiltradas);

        // Control sin filtro: la Completa SÍ aparece (está en rango).
        var sinFiltro = await client.GetAsync($"{EndpointBase}?pageSize=200");
        sinFiltro.EnsureSuccessStatusCode();
        var idsSinFiltro = await IdsDeAsync(sinFiltro);
        Assert.Contains(idCompleta, idsSinFiltro);
    }

    private static async Task<Guid> CrearOcAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        resp.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(resp)).GetProperty("id").GetGuid();
    }

    private async Task SetSubEstadoRecepcionAsync(Guid ocId, short subEstado)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE compras.ordenes_compra SET sub_estado_recepcion = {subEstado} WHERE id = {ocId}");
    }

    private static async Task<HashSet<Guid>> IdsDeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        var ids = new HashSet<Guid>();
        foreach (var it in body.GetProperty("items").EnumerateArray())
        {
            ids.Add(it.GetProperty("id").GetGuid());
        }
        return ids;
    }

    // --------- POST /api/v1/compras/ordenes/{id}/lineas (manual) ---------

    [Fact]
    public async Task AgregarLineaManual_En_OC_SinRq_Retorna_201_Y_No_Tira_Concurrency()
    {
        // Repro del bug reportado en sesión: el flujo
        // 1) crear OC con sinRequisicionPrevia=true
        // 2) POST /lineas con body válido
        // arrojaba 500 con DbUpdateConcurrencyException ("expected to
        // affect 1 row(s), but actually affected 0 row(s)") en
        // SaveChangesAsync del handler. El test gate el fix backend.
        var client = await CreateSuperAdminClientAsync();

        // Crear OC sin RQ previa (con motivo).
        var ocBody = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = "Prueba integration agregar línea manual",
        };
        var ocResp = await client.PostAsJsonAsync(EndpointBase, ocBody);
        ocResp.EnsureSuccessStatusCode();
        var ocId = (await ReadJsonAsync(ocResp)).GetProperty("id").GetGuid();

        // Agregar línea manual. Reusamos algunos ids del seed para que
        // los guards de FK no truenen.
        var lineaBody = new
        {
            ArticuloId = Guid.Parse("00000007-0001-0000-0000-000000000001"),
            Cantidad = 1m,
            UnidadMedida = "PZA",
            PrecioUnitario = 100m,
            DepartamentoSolicitanteId = Guid.Parse("00000004-0001-0000-0000-000000000001"),
            DescuentoTipo = (int?)null,
            DescuentoValor = (decimal?)null,
            IndicadorImpuestos = (string?)null,
            DescripcionExtendida = (string?)null,
            FechaEntregaLinea = (DateTimeOffset?)null,
            // Fase E PR3.1: el CC-Máquina es requerido en la línea manual.
            CentroCostoId = Guid.Parse("0c000000-0000-0000-0000-000000000001"),
            TextoAdicional = (string?)null,
        };

        var lineaResp = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/lineas", lineaBody);

        // Si truena el bug, esto será 500. El assert tira mensaje
        // diagnóstico claro con el body para facilitar debug.
        if (lineaResp.StatusCode == HttpStatusCode.InternalServerError)
        {
            var errorJson = await ReadJsonAsync(lineaResp);
            Assert.Fail(
                $"POST /lineas devolvió 500. Body: {errorJson.GetRawText()}. " +
                "Esto es el bug DbUpdateConcurrencyException que se está rastreando.");
        }

        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);
        var lineaJson = await ReadJsonAsync(lineaResp);
        Assert.NotEqual(Guid.Empty, lineaJson.GetProperty("lineaId").GetGuid());
        Assert.Equal(1, lineaJson.GetProperty("posicion").GetInt32());
    }

    // ─── Fase E PR3 (fix #701 API boundary): el CC-Máquina persiste ───
    // Gate del bug: el request DTO del endpoint no cableaba `CentroCostoId`,
    // así que System.Text.Json lo descartaba y centro_costo_id quedaba NULL
    // (tanto en POST /lineas como en PATCH). El valor persiste sin FK ni
    // validación de existencia (ADR-0050: la validez la da el picker).

    [Fact]
    public async Task AgregarLineaManual_ConCentroCosto_Persiste()
    {
        var client = await CreateSuperAdminClientAsync();
        var ccId = Guid.Parse("0c000000-0000-0000-0000-000000000001");

        var ocBody = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = "Integration CC-Máquina en línea manual",
        };
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ocBody)))
            .GetProperty("id").GetGuid();

        var lineaResp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas",
            LineaManualBody(ccId));
        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);

        // GET detalle → la línea manual trae el CC persistido (round-trip).
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        var linea = detalle.GetProperty("lineas").EnumerateArray().Single();
        Assert.Equal(ccId, linea.GetProperty("centroCostoId").GetGuid());
    }

    [Fact]
    public async Task ActualizarLineaManual_CambiaCentroCosto_Persiste()
    {
        var client = await CreateSuperAdminClientAsync();
        var ccInicial = Guid.Parse("0c000000-0000-0000-0000-000000000001");
        var ccNuevo = Guid.Parse("0c000000-0000-0000-0000-000000000002");

        var ocBody = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = "Integration PATCH CC-Máquina",
        };
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ocBody)))
            .GetProperty("id").GetGuid();

        var lineaResp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas",
            LineaManualBody(ccInicial));
        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);
        var lineaId = (await ReadJsonAsync(lineaResp)).GetProperty("lineaId").GetGuid();

        // PATCH cambia solo el CC (parcial). Debe persistir el nuevo valor.
        var patchResp = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas/{lineaId}",
            new { CentroCostoId = ccNuevo });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        var linea = detalle.GetProperty("lineas").EnumerateArray().Single();
        Assert.Equal(ccNuevo, linea.GetProperty("centroCostoId").GetGuid());
    }

    [Fact]
    public async Task ActualizarLineaHeredada_CambiarCentroCosto_Retorna_422()
    {
        // La guarda de dominio LINEA_OC_CC_HEREDADO_INMUTABLE sigue viva: el CC
        // de una línea heredada de RQ (LineaRequisicionId != null) es inmutable.
        // BusinessRuleException → 422 (GlobalExceptionHandler).
        var client = await CreateSuperAdminClientAsync();
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ValidBody())))
            .GetProperty("id").GetGuid();

        var folioRq = $"MID2026-{(uint)Guid.NewGuid().GetHashCode() % 1_000_000:D6}";
        Guid lineaHeredadaId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var oc = await db.OrdenesCompra.Include(o => o.Lineas).FirstAsync(o => o.Id == ocId);
            var rq = CrearRqCerrada(oc.EmpresaId, folioRq);
            db.Requisiciones.Add(rq);
            lineaHeredadaId = Guid.CreateVersion7();
            oc.AgregarLineaDesdeRequisicion(
                lineaId: lineaHeredadaId,
                articuloId: Guid.CreateVersion7(),
                cantidad: 10m,
                unidadMedida: "PZA",
                precioUnitario: 50m,
                departamentoSolicitanteId: Guid.CreateVersion7(),
                requisicionId: rq.Id,
                lineaRequisicionId: rq.Lineas.Single().Id);
            await db.SaveChangesAsync();
        }

        var patchResp = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas/{lineaHeredadaId}",
            new { CentroCostoId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, patchResp.StatusCode);
        var json = await ReadJsonAsync(patchResp);
        Assert.Equal("LINEA_OC_CC_HEREDADO_INMUTABLE", json.GetProperty("code").GetString());
    }

    // ─── Fase E PR3.1: el CC-Máquina es OBLIGATORIO en la línea manual ───

    [Fact]
    public async Task AgregarLineaManual_SinCentroCosto_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();

        var ocBody = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = "Integration CC-Máquina obligatorio PR3.1",
        };
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ocBody)))
            .GetProperty("id").GetGuid();

        // Mismo body válido, pero sin CC-Máquina → lo rechaza el validador.
        var lineaResp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas",
            LineaManualBody(centroCostoId: null));

        Assert.Equal(HttpStatusCode.BadRequest, lineaResp.StatusCode);
        var json = await ReadJsonAsync(lineaResp);
        Assert.Contains(
            "LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO",
            json.GetRawText());
    }

    [Fact]
    public async Task ActualizarLineaManual_PatchParcial_SinMandarCc_Sigue_204()
    {
        // Regresión de PR3.1: la guarda de obligatoriedad es de POST-ESTADO,
        // así que el PATCH parcial (null = "no tocar") no se rompe — cambiar
        // solo el precio de una línea que ya trae CC sigue funcionando.
        var client = await CreateSuperAdminClientAsync();
        var ccId = Guid.Parse("0c000000-0000-0000-0000-000000000001");

        var ocBody = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = "Integration PATCH parcial PR3.1",
        };
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ocBody)))
            .GetProperty("id").GetGuid();

        var lineaResp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas",
            LineaManualBody(ccId));
        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);
        var lineaId = (await ReadJsonAsync(lineaResp)).GetProperty("lineaId").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas/{lineaId}",
            new { PrecioUnitario = 250m });

        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        // El CC original se conserva (no se borró por no mandarlo).
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        var linea = detalle.GetProperty("lineas").EnumerateArray().Single();
        Assert.Equal(ccId, linea.GetProperty("centroCostoId").GetGuid());
    }

    [Fact]
    public async Task DuplicarOc_CopiaElCentroCosto_DeLasLineas()
    {
        // Fase E PR3.1: duplicar convierte todas las líneas en manuales y las
        // crea por dominio, sin pasar por el validador. Si no copiara el CC, la
        // OC duplicada nacería con líneas manuales en null — saltándose el
        // obligatorio y perdiendo el dato del origen en silencio.
        var client = await CreateSuperAdminClientAsync();
        var ccId = Guid.Parse("0c000000-0000-0000-0000-000000000001");

        var ocBody = ValidBody() with
        {
            SinRequisicionPrevia = true,
            MotivoSinRequisicion = "Integration duplicar conserva CC (PR3.1)",
        };
        var ocId = (await ReadJsonAsync(await client.PostAsJsonAsync(EndpointBase, ocBody)))
            .GetProperty("id").GetGuid();

        var lineaResp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{ocId}/lineas",
            LineaManualBody(ccId));
        Assert.Equal(HttpStatusCode.Created, lineaResp.StatusCode);

        // Solo se duplican OCs Canceladas/Rechazadas: llevamos el origen a
        // Cancelada por BD (el flujo de cancelación no es lo que se prueba).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var oc = await db.OrdenesCompra.FirstAsync(o => o.Id == ocId);
            db.Entry(oc).Property(nameof(Millet.Compras.Domain.Oc.OrdenCompra.Estado))
                .CurrentValue = Millet.Compras.Domain.Oc.EstadoOrdenCompra.Cancelada;
            await db.SaveChangesAsync();
        }

        var dupResp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{ocId}/duplicar",
            new
            {
                SucursalCodigo = "MID",
                FolioAnio = (short)2026,
                FechaDocumento = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            });
        Assert.Equal(HttpStatusCode.Created, dupResp.StatusCode);
        var nuevaOcId = (await ReadJsonAsync(dupResp))
            .GetProperty("ordenCompraNuevaId").GetGuid();

        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{nuevaOcId}"));
        var lineaDuplicada = detalle.GetProperty("lineas").EnumerateArray().Single();
        Assert.Equal(ccId, lineaDuplicada.GetProperty("centroCostoId").GetGuid());
    }

    /// <summary>Body de POST /lineas con ids del seed + el CC-Máquina indicado.</summary>
    private static object LineaManualBody(Guid? centroCostoId) => new
    {
        ArticuloId = Guid.Parse("00000007-0001-0000-0000-000000000001"),
        Cantidad = 1m,
        UnidadMedida = "PZA",
        PrecioUnitario = 100m,
        DepartamentoSolicitanteId = Guid.Parse("00000004-0001-0000-0000-000000000001"),
        DescuentoTipo = (int?)null,
        DescuentoValor = (decimal?)null,
        IndicadorImpuestos = (string?)null,
        DescripcionExtendida = (string?)null,
        FechaEntregaLinea = (DateTimeOffset?)null,
        CentroCostoId = centroCostoId,
        TextoAdicional = (string?)null,
    };

    // --------- GET /{id}/adjuntos/{adjuntoId}/contenido ---------

    [Fact]
    public async Task ContenidoAdjunto_Sin_Permiso_Retorna_403()
    {
        // El authorization corre antes del handler: con ids cualquiera
        // basta para verificar que el gate de compras.ordenes.leer aplica
        // (y que NO se filtra como 404).
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            $"{EndpointBase}/{Guid.NewGuid()}/adjuntos/{Guid.NewGuid()}/contenido");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ContenidoAdjunto_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        // OC real, pero adjuntoId que no existe → 404 (tiene el permiso,
        // así que NO debe ser 403).
        var ocResp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        ocResp.EnsureSuccessStatusCode();
        var ocId = (await ReadJsonAsync(ocResp)).GetProperty("id").GetGuid();

        var response = await client.GetAsync(
            $"{EndpointBase}/{ocId}/adjuntos/{Guid.NewGuid()}/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("OC_ADJUNTO_NO_ENCONTRADO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ContenidoAdjunto_Existente_Retorna_200_Con_ContentType_Y_Bytes()
    {
        var client = await CreateSuperAdminClientAsync();

        // Crear OC + subir un adjunto real por el flujo normal (multipart).
        var ocResp = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        ocResp.EnsureSuccessStatusCode();
        var ocId = (await ReadJsonAsync(ocResp)).GetProperty("id").GetGuid();

        var contenidoOriginal = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }; // "%PDF-1.4"
        var adjuntoId = await SubirAdjuntoAsync(
            client, ocId, contenidoOriginal, "application/pdf", "prueba.pdf");

        var response = await client.GetAsync(
            $"{EndpointBase}/{ocId}/adjuntos/{adjuntoId}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(contenidoOriginal, bytes);
    }

    // --------- Helpers ---------

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Construye una RQ en estado <c>Cerrada</c> (cubrimiento total por
    /// almacén) para verificar que la resolución de folio es state-agnostic.
    /// Mismo recipe que <c>ComprasRequisicionReadAdapterTests.CrearRqCerrada</c>.
    /// Los ids de sucursal/depto/almacén/usuario son arbitrarios: no hay FK
    /// física hacia ellos (relaciones cross-schema lógicas, ADR-0011).
    /// </summary>
    private static Requisicion CrearRqCerrada(Guid empresaId, string folio)
    {
        var rq = new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: Folio.Parse(folio),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: Guid.CreateVersion7(),
            departamentoId: Guid.CreateVersion7(),
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: null);

        rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA", Money.Mxn(15m));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            requiereNivel: RequiereNivel.SoloN1);

        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarCubrimiento(
            new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 10m, CantidadDeCompra: 0m) },
            DateTimeOffset.UtcNow);

        return rq;
    }

    // Tipo de documento OC del seed compartido (cotización, activo).
    private static readonly Guid TipoDocumentoCotizacionId =
        Guid.Parse("00000003-0003-0010-0000-000000000001");

    private static async Task<Guid> SubirAdjuntoAsync(
        HttpClient client,
        Guid ocId,
        byte[] contenido,
        string contentType,
        string nombreArchivo)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
        };
        var fileContent = new ByteArrayContent(contenido);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "archivo", nombreArchivo);

        var response = await client.PostAsync($"{EndpointBase}/{ocId}/adjuntos", form);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("adjuntoId").GetGuid();
    }

    private static CrearOrdenCompraVaciaBody ValidBody() => new(
        SucursalDestinoId: SucursalIdFija,
        SucursalCodigo: "MID",
        FolioAnio: 2026,
        ProveedorId: ProveedorActivoId,
        CondicionesPagoId: Guid.CreateVersion7(),
        UsoPrincipalId: Guid.CreateVersion7(),
        FechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
        Moneda: "MXN",
        TipoCambio: null,
        SinRequisicionPrevia: false,
        EsImportacion: false,
        CotizacionExcepcionada: false,
        Observaciones: "OC test integration F1-PR2",
        MotivoSinRequisicion: null,
        FechaEntregaEsperada: null,
        EncargadoComprasId: null,
        OcOrigenId: null);

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Body del POST. Mismo shape que <c>CrearOrdenCompraVaciaCommand</c>
    /// (sin EmpresaId ni CompradorTitularId — los resuelve el handler del JWT).
    /// </summary>
    private sealed record CrearOrdenCompraVaciaBody(
        Guid SucursalDestinoId,
        string SucursalCodigo,
        short FolioAnio,
        Guid ProveedorId,
        Guid CondicionesPagoId,
        Guid UsoPrincipalId,
        DateOnly FechaDocumento,
        string Moneda,
        decimal? TipoCambio,
        bool SinRequisicionPrevia,
        bool EsImportacion,
        bool CotizacionExcepcionada,
        string? Observaciones,
        string? MotivoSinRequisicion,
        DateOnly? FechaEntregaEsperada,
        Guid? EncargadoComprasId,
        Guid? OcOrigenId);
}
