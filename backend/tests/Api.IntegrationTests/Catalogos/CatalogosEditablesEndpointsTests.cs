using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Catalogos;

/// <summary>
/// Tests integration de los catálogos editables (F-Admin-PR5.2):
/// CondicionesPago, Incoterms, Transportistas, UsosPrincipales.
/// Happy paths CRUD + desactivar.
/// </summary>
public class CatalogosEditablesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private readonly WebApplicationFactory<Program> _factory;

    public CatalogosEditablesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CrearCondicionesPago_Y_PATCH_Funciona()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"CP-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var created = await client.PostAsJsonAsync("/api/v1/catalogos/condiciones-pago", new
        {
            Clave = clave,
            Nombre = "Test 75 días",
            DiasCredito = 75,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal(75, body.GetProperty("diasCredito").GetInt32());

        var patched = await client.PatchAsJsonAsync($"/api/v1/catalogos/condiciones-pago/{id}", new
        {
            Nombre = "Test 90 días renombrado",
            DiasCredito = 90,
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var patchedBody = await ReadJsonAsync(patched);
        Assert.Equal(90, patchedBody.GetProperty("diasCredito").GetInt32());
    }

    [Fact]
    public async Task CrearIncoterm_Y_Desactivar()
    {
        var client = await CreateSuperAdminClientAsync();
        // Genera código único 3-letras evitando los 11 seeds existentes.
        var codigo = $"Z{new Random().Next(10, 99)}";

        var created = await client.PostAsJsonAsync("/api/v1/catalogos/incoterms", new
        {
            Codigo = codigo,
            Nombre = "Test Incoterm",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created);
        var id = body.GetProperty("id").GetGuid();

        var deact = await client.PostAsJsonAsync(
            $"/api/v1/catalogos/incoterms/{id}/desactivar", new { });
        Assert.Equal(HttpStatusCode.OK, deact.StatusCode);
        var deactBody = await ReadJsonAsync(deact);
        // Estatus.Inactivo == 1
        Assert.Equal(1, deactBody.GetProperty("estatus").GetInt32());
    }

    [Fact]
    public async Task CrearTransportista_Con_Clave_Duplicada_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"TR-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var first = await client.PostAsJsonAsync("/api/v1/catalogos/transportistas", new
        {
            Clave = clave,
            Nombre = "Transportista Original",
            Email = (string?)null,
            Telefono = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var dup = await client.PostAsJsonAsync("/api/v1/catalogos/transportistas", new
        {
            Clave = clave,
            Nombre = "Transportista Duplicado",
            Email = (string?)null,
            Telefono = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task CrearUsoPrincipal_Funciona()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"UP-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var created = await client.PostAsJsonAsync("/api/v1/catalogos/usos-principales", new
        {
            Clave = clave,
            Nombre = "Uso Test",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created);
        Assert.Equal(clave, body.GetProperty("clave").GetString());
    }

    [Fact]
    public async Task ListarFormasPago_Retorna_Seed_SAT()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/formas-pago");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadJsonAsync(response);
        Assert.True(items.GetArrayLength() >= 20, "Seed SAT debe tener 22 entries.");
        // Verifica que la primera tenga claveSat="01"
        var primera = items[0];
        Assert.Equal("01", primera.GetProperty("claveSat").GetString());
    }

    [Fact]
    public async Task ListarUsosCfdi_Retorna_Seed_SAT()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/usos-cfdi");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadJsonAsync(response);
        Assert.True(items.GetArrayLength() >= 20, "Seed SAT debe tener 23 entries.");
        // Verifica primero es CN01 (orden alfabético) o G01
        var claves = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            claves.Add(item.GetProperty("claveSat").GetString()!);
        }
        Assert.Contains("G01", claves);
        Assert.Contains("S01", claves);
    }

    // --- Unidades de medida (ADR-0046 Etapa 1a) ---

    [Fact]
    public async Task ListarUnidadesMedida_Retorna_Seed_ConFactoresYDecimales()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/unidades-medida");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadJsonAsync(response);
        Assert.True(items.GetArrayLength() >= 10, "El seed debe tener 10 unidades.");

        var porCodigo = new Dictionary<string, JsonElement>();
        foreach (var it in items.EnumerateArray())
            porCodigo[it.GetProperty("codigo").GetString()!] = it;

        // PZA: Conteo (0), base, factor 1, 0 decimales, estatus Activo (0).
        var pza = porCodigo["PZA"];
        Assert.Equal(0, pza.GetProperty("dimension").GetInt32());
        Assert.Equal(1m, pza.GetProperty("factorABase").GetDecimal());
        Assert.Equal(0, pza.GetProperty("decimales").GetInt32());
        Assert.True(pza.GetProperty("esBase").GetBoolean());
        Assert.Equal(0, pza.GetProperty("estatus").GetInt32());

        // KG: Peso (1), base, 3 decimales.
        var kg = porCodigo["KG"];
        Assert.Equal(1, kg.GetProperty("dimension").GetInt32());
        Assert.Equal(3, kg.GetProperty("decimales").GetInt32());
        Assert.True(kg.GetProperty("esBase").GetBoolean());

        // G: Peso, no base, factor 0.001.
        var g = porCodigo["G"];
        Assert.Equal(0.001m, g.GetProperty("factorABase").GetDecimal());
        Assert.False(g.GetProperty("esBase").GetBoolean());
    }

    [Fact]
    public async Task CrearUnidadMedida_Y_PATCH_Funciona()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = $"UM{new Random().Next(100000, 999999)}";

        var created = await client.PostAsJsonAsync("/api/v1/catalogos/unidades-medida", new
        {
            Codigo = codigo,
            Nombre = "Unidad de prueba",
            Dimension = 2, // Volumen
            FactorABase = 0.5m,
            Decimales = 2,
            EsBase = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal(2, body.GetProperty("dimension").GetInt32());
        Assert.Equal(0.5m, body.GetProperty("factorABase").GetDecimal());

        // PATCH de campos siempre editables (nombre + decimales).
        var patched = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/unidades-medida/{id}", new
            {
                Nombre = "Unidad renombrada",
                Decimales = 3,
            });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var patchedBody = await ReadJsonAsync(patched);
        Assert.Equal("Unidad renombrada", patchedBody.GetProperty("nombre").GetString());
        Assert.Equal(3, patchedBody.GetProperty("decimales").GetInt32());
    }

    [Fact]
    public async Task ActualizarUnidadMedida_Factor_NoEnUso_SeAplica()
    {
        // Guardrail (ADR-0046): cambiar factor/dimensión solo se bloquea si la
        // unidad está EN USO. En 1a nadie la referencia → el cambio se permite.
        // El bloqueo (UNIDAD_MEDIDA_EN_USO) está cubierto por el test de dominio.
        var client = await CreateSuperAdminClientAsync();
        var codigo = $"UM{new Random().Next(100000, 999999)}";

        var created = await client.PostAsJsonAsync("/api/v1/catalogos/unidades-medida", new
        {
            Codigo = codigo,
            Nombre = "Factor editable",
            Dimension = 1,
            FactorABase = 1m,
            Decimales = 0,
            EsBase = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/unidades-medida/{id}", new { FactorABase = 0.25m });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal(0.25m, (await ReadJsonAsync(patched)).GetProperty("factorABase").GetDecimal());
    }

    [Fact]
    public async Task CrearUnidadMedida_Codigo_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        // PZA ya existe en el seed.
        var dup = await client.PostAsJsonAsync("/api/v1/catalogos/unidades-medida", new
        {
            Codigo = "PZA",
            Nombre = "Pieza duplicada",
            Dimension = 0,
            FactorABase = 1m,
            Decimales = 0,
            EsBase = true,
        });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    // --- Artículo ↔ unidad: FK + guardrail (ADR-0046 Etapa 1b) ---

    [Fact]
    public async Task CrearArticulo_ConUnidadMedidaId_SeteaFk_Y_SincronizaDefault()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigoUnidad = $"UMA{new Random().Next(100000, 999999)}";
        var unidadId = await CrearUnidadAsync(client, codigoUnidad, dimension: 0, factor: 1m, decimales: 0);

        var creado = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", new
        {
            Clave = $"ART-FK-{Guid.NewGuid().ToString("N")[..6]}",
            Nombre = "Artículo con FK",
            UnidadMedidaId = unidadId,
            Naturaleza = 0,
            DescripcionLarga = (string?)null,
            CategoriaId = (Guid?)null,
            PrecioReferenciaMonto = (decimal?)null,
            PrecioReferenciaMoneda = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await ReadJsonAsync(creado)).GetProperty("id").GetGuid();

        var detalle = await ReadJsonAsync(await client.GetAsync($"/api/v1/catalogos/articulos/{id}"));
        Assert.Equal(unidadId, detalle.GetProperty("unidadMedidaId").GetGuid());
        // unidad_medida_default sincronizado al código de la unidad.
        Assert.Equal(codigoUnidad, detalle.GetProperty("unidadMedidaDefault").GetString());
    }

    [Fact]
    public async Task PatchUnidad_Factor_EnUso_422_NoEnUso_200()
    {
        var client = await CreateSuperAdminClientAsync();

        // Unidad EN USO (referenciada por un artículo) → cambiar factor se bloquea.
        var unidadEnUso = await CrearUnidadAsync(
            client, $"UMU{new Random().Next(100000, 999999)}", 1, 1m, 0);
        var creado = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", new
        {
            Clave = $"ART-EU-{Guid.NewGuid().ToString("N")[..6]}",
            Nombre = "Referenciante",
            UnidadMedidaId = unidadEnUso,
            Naturaleza = 0,
            DescripcionLarga = (string?)null,
            CategoriaId = (Guid?)null,
            PrecioReferenciaMonto = (decimal?)null,
            PrecioReferenciaMoneda = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);

        var patchEnUso = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/unidades-medida/{unidadEnUso}", new { FactorABase = 9m });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, patchEnUso.StatusCode);
        Assert.Equal("UNIDAD_MEDIDA_EN_USO",
            (await ReadJsonAsync(patchEnUso)).GetProperty("code").GetString());

        // Unidad NO referenciada → el factor sí se puede cambiar.
        var unidadLibre = await CrearUnidadAsync(
            client, $"UML{new Random().Next(100000, 999999)}", 1, 1m, 0);
        var patchLibre = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/unidades-medida/{unidadLibre}", new { FactorABase = 0.5m });
        Assert.Equal(HttpStatusCode.OK, patchLibre.StatusCode);
    }

    private static async Task<Guid> CrearUnidadAsync(
        HttpClient client, string codigo, int dimension, decimal factor, int decimales)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalogos/unidades-medida", new
        {
            Codigo = codigo,
            Nombre = codigo,
            Dimension = dimension,
            FactorABase = factor,
            Decimales = decimales,
            EsBase = false,
        });
        resp.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(resp)).GetProperty("id").GetGuid();
    }

    // --- Artículo ↔ categoría: FK + guardrail (ADR-0046 PR2) ---

    [Fact]
    public async Task CrearArticulo_ConCategoriaId_SeteaFk_Y_SincronizaNombre()
    {
        var client = await CreateSuperAdminClientAsync();
        var nombreCat = $"Cat FK {Guid.NewGuid().ToString("N")[..6]}";
        var categoriaId = await CrearCategoriaAsync(client, nombreCat);
        var unidadId = await CrearUnidadAsync(
            client, $"UMC{new Random().Next(100000, 999999)}", 0, 1m, 0);

        var creado = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", new
        {
            Clave = $"ART-CAT-{Guid.NewGuid().ToString("N")[..6]}",
            Nombre = "Artículo con categoría",
            UnidadMedidaId = unidadId,
            Naturaleza = 0,
            DescripcionLarga = (string?)null,
            CategoriaId = categoriaId,
            PrecioReferenciaMonto = (decimal?)null,
            PrecioReferenciaMoneda = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await ReadJsonAsync(creado)).GetProperty("id").GetGuid();

        var detalle = await ReadJsonAsync(await client.GetAsync($"/api/v1/catalogos/articulos/{id}"));
        Assert.Equal(categoriaId, detalle.GetProperty("categoriaId").GetGuid());
        // AsignarCategoria sincroniza el string legacy categoria = nombre.
        Assert.Equal(nombreCat, detalle.GetProperty("categoria").GetString());
    }

    [Fact]
    public async Task CrearArticulo_ConCategoriaIdInexistente_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var unidadId = await CrearUnidadAsync(
            client, $"UMX{new Random().Next(100000, 999999)}", 0, 1m, 0);

        var creado = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", new
        {
            Clave = $"ART-CX-{Guid.NewGuid().ToString("N")[..6]}",
            Nombre = "Categoría inexistente",
            UnidadMedidaId = unidadId,
            Naturaleza = 0,
            DescripcionLarga = (string?)null,
            CategoriaId = Guid.NewGuid(),
            PrecioReferenciaMonto = (decimal?)null,
            PrecioReferenciaMoneda = (string?)null,
        });
        // Misma rama de validación que "categoría inactiva" (null || no activa).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, creado.StatusCode);
        Assert.Equal("ARTICULO_CATEGORIA_NO_REGISTRADA",
            (await ReadJsonAsync(creado)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task DesactivarCategoria_EnUso_422_Libre_200()
    {
        var client = await CreateSuperAdminClientAsync();
        var unidadId = await CrearUnidadAsync(
            client, $"UMG{new Random().Next(100000, 999999)}", 0, 1m, 0);

        // Categoría EN USO (referenciada por un artículo vía FK) → no se desactiva.
        var catEnUso = await CrearCategoriaAsync(
            client, $"Cat EnUso {Guid.NewGuid().ToString("N")[..6]}");
        var art = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", new
        {
            Clave = $"ART-CEU-{Guid.NewGuid().ToString("N")[..6]}",
            Nombre = "Referenciante",
            UnidadMedidaId = unidadId,
            Naturaleza = 0,
            DescripcionLarga = (string?)null,
            CategoriaId = catEnUso,
            PrecioReferenciaMonto = (decimal?)null,
            PrecioReferenciaMoneda = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, art.StatusCode);

        var deactEnUso = await client.PostAsJsonAsync(
            $"/api/v1/catalogos/categorias-articulo/{catEnUso}/desactivar", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deactEnUso.StatusCode);
        Assert.Equal("CATEGORIA_ARTICULO_EN_USO",
            (await ReadJsonAsync(deactEnUso)).GetProperty("code").GetString());

        // Categoría libre → sí se desactiva.
        var catLibre = await CrearCategoriaAsync(
            client, $"Cat Libre {Guid.NewGuid().ToString("N")[..6]}");
        var deactLibre = await client.PostAsJsonAsync(
            $"/api/v1/catalogos/categorias-articulo/{catLibre}/desactivar", new { });
        Assert.Equal(HttpStatusCode.OK, deactLibre.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(deactLibre)).GetProperty("estatus").GetInt32());
    }

    private static async Task<Guid> CrearCategoriaAsync(HttpClient client, string nombre)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/categorias-articulo", new { Nombre = nombre });
        resp.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(resp)).GetProperty("id").GetGuid();
    }

    // --- Categorías de artículo (patrón ADR-0046) ---

    [Fact]
    public async Task ListarCategoriasArticulo_Retorna_Seed_14()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/categorias-articulo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadJsonAsync(response);
        Assert.True(items.GetArrayLength() >= 14, "El seed debe tener 14 categorías.");

        var nombres = new List<string>();
        foreach (var it in items.EnumerateArray())
            nombres.Add(it.GetProperty("nombre").GetString()!);
        Assert.Contains("INSUMOS PRODUCCION", nombres);
        Assert.Contains("Servicios técnicos", nombres);
    }

    [Fact]
    public async Task CrearCategoriaArticulo_Y_PATCH_Funciona()
    {
        var client = await CreateSuperAdminClientAsync();
        var nombre = $"Cat Test {Guid.NewGuid().ToString("N")[..6]}";

        var created = await client.PostAsJsonAsync(
            "/api/v1/catalogos/categorias-articulo", new { Nombre = nombre });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal(nombre, body.GetProperty("nombre").GetString());

        var patched = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/categorias-articulo/{id}",
            new { Nombre = nombre + " renombrada" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal(nombre + " renombrada",
            (await ReadJsonAsync(patched)).GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task CrearCategoriaArticulo_Nombre_Duplicado_Normalizado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var suf = Guid.NewGuid().ToString("N")[..6];

        var first = await client.PostAsJsonAsync(
            "/api/v1/catalogos/categorias-articulo", new { Nombre = $"CAT DUP {suf}" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Distinta capitalización + espacios extra internos → normaliza igual → 409.
        var dup = await client.PostAsJsonAsync(
            "/api/v1/catalogos/categorias-articulo", new { Nombre = $"  cat   dup   {suf} " });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("CATEGORIA_ARTICULO_NOMBRE_DUPLICADO",
            (await ReadJsonAsync(dup)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task DesactivarCategoriaArticulo_CambiaEstatus_A_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var nombre = $"Cat Desac {Guid.NewGuid().ToString("N")[..6]}";
        var created = await client.PostAsJsonAsync(
            "/api/v1/catalogos/categorias-articulo", new { Nombre = nombre });
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var deact = await client.PostAsJsonAsync(
            $"/api/v1/catalogos/categorias-articulo/{id}/desactivar", new { });
        Assert.Equal(HttpStatusCode.OK, deact.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(deact)).GetProperty("estatus").GetInt32());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

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
}
