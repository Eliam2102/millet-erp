using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del CRUD de Series + reserva de folios
/// (F-Admin-PR6.1).
///
/// <para>
/// Cubre los happy paths del CRUD + caminos negativos clave (409
/// duplicado, 422 SERIE_NO_CONFIGURADA, 200 reserva con folio
/// formateado) + concurrencia.
/// </para>
///
/// <para>
/// Cada test genera un sufijo aleatorio en el prefijo para no chocar
/// con seeds ni con datos dejados por tests previos en la DB compartida.
/// El <see cref="EmpresaBootstrapId"/> es la empresa creada por el
/// BootstrapSuperAdminHostedService (al iniciar la app de tests con
/// <c>Auth:Bootstrap:EmpresaInicial</c> configurada).
/// </para>
/// </summary>
public class SeriesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/series";

    // Empresa inicial determinista del BootstrapSuperAdminHostedService.
    private static readonly Guid EmpresaBootstrapId =
        Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public SeriesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --- LIST ---

    [Fact]
    public async Task Listar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Con_SuperAdmin_Retorna_200()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("total").GetInt32() >= 0);
    }

    [Fact]
    public async Task Operaciones_CrossTenant_No_Leen_Ni_Modifican_La_Serie_Ajena()
    {
        var client = await CreateSuperAdminClientAsync();
        var empresaAjena = Guid.CreateVersion7();
        var serieAjena = Guid.CreateVersion7();
        var prefijo = RandomPrefijo();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            db.Empresas.Add(new Empresa(
                empresaAjena, $"E{Guid.NewGuid():N}"[..12], $"R{Guid.NewGuid():N}"[..12],
                "Empresa ajena", "601", "Calle", "1", "Colonia", "Ciudad",
                "Municipio", "Estado", "MEX"));
            db.Series.Add(new Serie(
                serieAjena, empresaAjena, null, TipoDocumentoSerie.Poliza,
                prefijo, null, ReinicioPeriodo.None));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{EndpointBase}?empresaId={empresaAjena}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(EndpointBase, new
            {
                Id = Guid.Empty,
                EmpresaId = empresaAjena,
                SucursalId = (Guid?)null,
                TipoDocumento = 4,
                Prefijo = RandomPrefijo(),
                Sufijo = (string?)null,
                ReinicioPeriodo = 0,
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"{EndpointBase}/{serieAjena}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PatchSerieAsync(client, serieAjena, 0, new
            {
                Prefijo = RandomPrefijo(),
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await DesactivarSerieAsync(client, serieAjena, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
            {
                EmpresaId = empresaAjena,
                SucursalId = (Guid?)null,
                TipoDocumento = 4,
                FechaReferencia = "2026-05-14",
            })).StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        using var verifyBypass = verifyScope.ServiceProvider
            .GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var persisted = await verifyScope.ServiceProvider.GetRequiredService<CompartidoDbContext>()
            .Series.AsNoTracking().SingleAsync(x => x.Id == serieAjena);
        Assert.True(persisted.Activa);
        Assert.Equal(prefijo, persisted.Prefijo);
    }

    [Fact]
    public async Task Usuario_Solo_Ve_Y_Opera_Series_De_Sucursal_Asignada()
    {
        var admin = await CreateSuperAdminClientAsync();
        var propia = await CrearSucursalAsync(admin, "SRP");
        var ajena = await CrearSucursalAsync(admin, "SRA");
        var propiaId = await CrearSerieAsync(admin, propia, 4, RandomPrefijo());
        var ajenaId = await CrearSerieAsync(admin, ajena, 4, RandomPrefijo());
        var operativo = await CreateOperativoAsync(propia);

        var listado = await operativo.GetAsync(EndpointBase);
        listado.EnsureSuccessStatusCode();
        var ids = (await ReadJsonAsync(listado)).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(propiaId, ids);
        Assert.DoesNotContain(ajenaId, ids);

        Assert.Equal(HttpStatusCode.OK, (await operativo.GetAsync($"{EndpointBase}/{propiaId}")).StatusCode);
        await AssertSucursalForbiddenAsync(await operativo.GetAsync($"{EndpointBase}/{ajenaId}"));
        await AssertSucursalForbiddenAsync(await PatchSerieAsync(
            operativo, ajenaId, 0, new { Prefijo = RandomPrefijo() }));
        await AssertSucursalForbiddenAsync(await DesactivarSerieAsync(
            operativo, ajenaId, 0));
        await AssertSucursalForbiddenAsync(await operativo.PostAsJsonAsync(
            $"{EndpointBase}/reservar", new
            {
                EmpresaId = EmpresaBootstrapId,
                SucursalId = ajena,
                TipoDocumento = 4,
                FechaReferencia = "2026-05-14",
            }));
    }

    [Fact]
    public async Task Serie_Global_Es_Fallback_Pero_Solo_Corporativo_La_Gestiona()
    {
        var admin = await CreateSuperAdminClientAsync();
        var sucursal = await CrearSucursalAsync(admin, "SRG");
        var globalId = await CrearSerieAsync(admin, null, 3, RandomPrefijo());
        var operativo = await CreateOperativoAsync(sucursal);

        var reserva = await operativo.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = sucursal,
            TipoDocumento = 3,
            FechaReferencia = "2026-05-14",
        });
        reserva.EnsureSuccessStatusCode();

        var patch = await PatchSerieAsync(
            operativo, globalId, await ObtenerVersionSerieAsync(admin, globalId),
            new { Prefijo = RandomPrefijo() });
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal("SERIE_GLOBAL_RESTRINGIDA", (await ReadJsonAsync(patch)).GetProperty("code").GetString());
    }

    // --- CREATE ---

    [Fact]
    public async Task Crear_Con_Datos_Validos_Retorna_201_Con_Id()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();

        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4, // Poliza — evitar conflicto con OC en otros tests
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0, // None
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(prefijo, body.GetProperty("prefijo").GetString());
        Assert.True(body.GetProperty("activa").GetBoolean());
    }

    [Fact]
    public async Task Crear_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var payload = new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4, // Poliza
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0,
        };

        var primero = await client.PostAsJsonAsync(EndpointBase, payload);
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsJsonAsync(EndpointBase, payload);
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    // --- DETALLE ---

    [Fact]
    public async Task Obtener_Detalle_Incluye_Preview_Folio()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();

        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1, // Anual
        });
        createResp.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        var detalle = await client.GetAsync($"{EndpointBase}/{id}");
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        var body = await ReadJsonAsync(detalle);

        Assert.True(body.TryGetProperty("serie", out _));
        Assert.True(body.TryGetProperty("proximoFolioPreview", out var preview));
        var previewStr = preview.GetString();
        Assert.NotNull(previewStr);
        Assert.Matches($@"^{prefijo}-\d{{4}}-000001$", previewStr);
    }

    // --- PATCH ---

    [Fact]
    public async Task Patch_Cambia_Prefijo_Y_Get_Posterior_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var nuevoPrefijo = RandomPrefijo();

        var created = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0,
        });
        created.EnsureSuccessStatusCode();
        var createdBody = await ReadJsonAsync(created);
        var id = createdBody.GetProperty("id").GetGuid();
        var version = createdBody.GetProperty("version").GetInt32();
        var patched = await PatchSerieAsync(client, id, version, new
        {
            Prefijo = nuevoPrefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = (int?)null,
            LimpiarSufijo = false,
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        var verify = await client.GetAsync($"{EndpointBase}/{id}");
        verify.EnsureSuccessStatusCode();
        var verifyBody = await ReadJsonAsync(verify);
        Assert.Equal(nuevoPrefijo,
            verifyBody.GetProperty("serie").GetProperty("prefijo").GetString());

        var stale = await PatchSerieAsync(client, id, version, new { Prefijo = RandomPrefijo() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CONCURRENCY_CONFLICT", (await ReadJsonAsync(stale)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Desactivar_Con_Version_Obsoleta_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var id = await CrearSerieAsync(client, Guid.NewGuid(), 4, RandomPrefijo());
        var version = await ObtenerVersionSerieAsync(client, id);

        var patched = await PatchSerieAsync(client, id, version, new { Prefijo = RandomPrefijo() });
        patched.EnsureSuccessStatusCode();

        var stale = await DesactivarSerieAsync(client, id, version);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CONCURRENCY_CONFLICT", (await ReadJsonAsync(stale)).GetProperty("code").GetString());
    }

    // --- DESACTIVAR ---

    [Fact]
    public async Task Desactivar_Cambia_Activa_A_False()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();

        var created = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var resp = await DesactivarSerieAsync(client, id, await ObtenerVersionSerieAsync(client, id));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        Assert.False(body.GetProperty("activa").GetBoolean());
    }

    [Fact]
    public async Task Desactivar_Bloquea_Reserva_Pero_Conserva_Detalle()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursal = Guid.NewGuid();
        var tipoDocumento = await TipoSinSerieGlobalActivaAsync();
        var id = await CrearSerieAsync(client, sucursal, tipoDocumento, RandomPrefijo());

        (await DesactivarSerieAsync(client, id, await ObtenerVersionSerieAsync(client, id))).EnsureSuccessStatusCode();

        var reserva = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = sucursal,
            TipoDocumento = tipoDocumento,
            FechaReferencia = "2026-05-14",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reserva.StatusCode);
        Assert.Equal("SERIE_NO_CONFIGURADA", (await ReadJsonAsync(reserva)).GetProperty("code").GetString());

        var detalle = await client.GetAsync($"{EndpointBase}/{id}");
        detalle.EnsureSuccessStatusCode();
        Assert.False((await ReadJsonAsync(detalle)).GetProperty("serie").GetProperty("activa").GetBoolean());
    }

    // --- RESERVAR ---

    [Fact]
    public async Task Reservar_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, "dev-noperms", "noperms@dev.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 2,
            FechaReferencia = "2026-05-14",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reservar_Con_Usuario_Sin_Permiso_Retorna_403()
    {
        // Usuario autenticado sin admin.series.gestionar: no puede consumir folios por HTTP.
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(
            client, "00000000-0000-0000-0000-0000000000f3", "sin-permisos-series@dev.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = Guid.NewGuid(),
            SucursalId = (Guid?)null,
            TipoDocumento = 1,
            FechaReferencia = "2026-05-14",
        });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Reservar_Sin_Serie_Configurada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var tipoDocumento = await TipoSinSerieGlobalActivaAsync();

        var resp = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)Guid.NewGuid(),
            TipoDocumento = tipoDocumento,
            FechaReferencia = "2026-05-14",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        Assert.Equal("SERIE_NO_CONFIGURADA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reservar_Con_Serie_Activa_Retorna_200_Con_Folio_Formateado()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        // SucursalId única por test: el handler de Reservar busca primero
        // serie por (Empresa, Sucursal específica, TipoDoc); si no
        // encuentra, cae al match cross-sucursal. Usando una SucursalId
        // sintética por test evitamos chocar con series creadas por otros
        // tests en la misma DB.
        var sucursalUnica = Guid.NewGuid();

        // Crear serie Anual para reservar contra ella.
        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4, // Poliza
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1, // Anual
        });
        createResp.EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4,
            FechaReferencia = "2026-05-14",
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        var folio = body.GetProperty("folio").GetString();
        Assert.NotNull(folio);
        Assert.Matches($@"^{prefijo}-2026-\d{{6}}$", folio);
        Assert.True(body.GetProperty("numero").GetInt64() >= 1);
    }

    [Fact]
    public async Task Reservar_10_Veces_Devuelve_10_Folios_Consecutivos()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var sucursalUnica = Guid.NewGuid();

        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1, // Anual — todos los folios mismo período "2026"
        });
        createResp.EnsureSuccessStatusCode();

        var numeros = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var r = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
            {
                EmpresaId = EmpresaBootstrapId,
                SucursalId = (Guid?)sucursalUnica,
                TipoDocumento = 4,
                FechaReferencia = "2026-05-14",
            });
            r.EnsureSuccessStatusCode();
            var b = await ReadJsonAsync(r);
            numeros.Add(b.GetProperty("numero").GetInt64());
        }

        // Los números son consecutivos a partir del mínimo (otro test
        // previo puede haber reservado para ESTE prefijo solo si fueron
        // reservas del mismo prefijo — pero el prefijo es aleatorio por
        // test, así que arrancan desde 1).
        var ordenados = numeros.OrderBy(n => n).ToList();
        for (int i = 0; i < ordenados.Count - 1; i++)
        {
            Assert.Equal(ordenados[i] + 1, ordenados[i + 1]);
        }
    }

    [Fact]
    public async Task Reservar_Cfdi_Y_NotaCredito_Mantiene_Secuencias_Independientes()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal, 2, RandomPrefijo());
        await CrearSerieAsync(client, sucursal, 3, RandomPrefijo());

        async Task<long> Reservar(int tipo)
        {
            var response = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
            {
                EmpresaId = EmpresaBootstrapId,
                SucursalId = sucursal,
                TipoDocumento = tipo,
                FechaReferencia = "2026-05-14",
            });
            response.EnsureSuccessStatusCode();
            return (await ReadJsonAsync(response)).GetProperty("numero").GetInt64();
        }

        Assert.Equal(1, await Reservar(2));
        Assert.Equal(2, await Reservar(2));
        Assert.Equal(1, await Reservar(3));
    }

    [Fact]
    public async Task Reservar_Reintento_Idempotente_No_Consume_Otro_Folio_Y_Rechaza_Body_Distinto()
    {
        var admin = await CreateSuperAdminClientAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(admin, sucursal, 4, RandomPrefijo());
        var client = await CreateSuperAdminClientAsync(autoIdempotency: false);
        var key = Guid.NewGuid().ToString("D");
        var body = new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursal,
            TipoDocumento = 4,
            FechaReferencia = "2026-05-14",
        };

        var first = await PostReservaAsync(client, key, body);
        var replay = await PostReservaAsync(client, key, body);
        first.EnsureSuccessStatusCode();
        replay.EnsureSuccessStatusCode();
        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        var firstBody = await ReadJsonAsync(first);
        Assert.True(JsonElement.DeepEquals(firstBody, await ReadJsonAsync(replay)));

        var next = await PostReservaAsync(client, Guid.NewGuid().ToString("D"), body);
        next.EnsureSuccessStatusCode();
        Assert.Equal(
            firstBody.GetProperty("numero").GetInt64() + 1,
            (await ReadJsonAsync(next)).GetProperty("numero").GetInt64());

        var mismatch = await PostReservaAsync(client, key, new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursal,
            TipoDocumento = 4,
            FechaReferencia = "2026-05-15",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY",
            (await ReadJsonAsync(mismatch)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reserva_En_Espera_No_Nace_Despues_De_Desactivacion_Confirmada()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursal = Guid.NewGuid();
        var tipoDocumento = await TipoSinSerieGlobalActivaAsync();
        var serieId = await CrearSerieAsync(client, sucursal, tipoDocumento, RandomPrefijo());

        using var lockScope = _factory.Services.CreateScope();
        using var bypass = lockScope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        await using var blocker = await lockDb.Database.BeginTransactionAsync();
        var locked = await lockDb.Database.SqlQueryRaw<Guid>(
                "SELECT id AS \"Value\" FROM compartido.series WHERE id = {0} FOR UPDATE", serieId)
            .SingleAsync();
        Assert.Equal(serieId, locked);

        var deactivate = DesactivarSerieAsync(
            client, serieId, await ObtenerVersionSerieAsync(client, serieId));
        await EsperarUpdateSerieBloqueadoAsync();
        var reserve = client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursal,
            TipoDocumento = tipoDocumento,
            FechaReferencia = "2026-05-14",
        });
        await Task.Delay(100);
        Assert.False(reserve.IsCompleted);

        await blocker.CommitAsync();
        (await deactivate).EnsureSuccessStatusCode();
        var reservationResult = await reserve;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reservationResult.StatusCode);
        Assert.Equal("SERIE_NO_CONFIGURADA",
            (await ReadJsonAsync(reservationResult)).GetProperty("code").GetString());

        async Task EsperarUpdateSerieBloqueadoAsync()
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                using var probeScope = _factory.Services.CreateScope();
                using var probeBypass = probeScope.ServiceProvider
                    .GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var probeDb = probeScope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                var waiters = await probeDb.Database.SqlQueryRaw<int>("""
                        SELECT count(*)::int AS "Value"
                        FROM pg_stat_activity
                        WHERE wait_event_type = 'Lock'
                          AND query ILIKE '%UPDATE%series%'
                        """).SingleAsync();
                if (waiters > 0) return;
                await Task.Delay(20);
            }

            throw new TimeoutException("La desactivación no llegó a esperar el bloqueo de la serie.");
        }
    }

    [Fact]
    public async Task Reservar_50_Concurrentes_Devuelve_50_Folios_Unicos()
    {
        // Concurrencia: dispara 50 POSTs en paralelo con keys distintos.
        // Cada reserva debe obtener un número único — la atomicidad la
        // garantiza el ON CONFLICT DO UPDATE de la UPSERT en
        // ReservarFolioHandler.
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var sucursalUnica = Guid.NewGuid();

        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1,
        });
        createResp.EnsureSuccessStatusCode();

        const int N = 50;
        var tasks = Enumerable.Range(0, N).Select(async _ =>
        {
            var r = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
            {
                EmpresaId = EmpresaBootstrapId,
                SucursalId = (Guid?)sucursalUnica,
                TipoDocumento = 4,
                FechaReferencia = "2026-05-14",
            });
            r.EnsureSuccessStatusCode();
            var b = await ReadJsonAsync(r);
            return b.GetProperty("numero").GetInt64();
        }).ToArray();

        var numeros = await Task.WhenAll(tasks);
        Assert.Equal(N, numeros.Length);
        Assert.Equal(N, numeros.Distinct().Count());
    }

    [Fact]
    public async Task Serie_fiscal_continua_inicial_prioridad_e_inmutabilidad()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursal = Guid.NewGuid();
        var created = await client.PostAsJsonAsync(EndpointBase, new
        {
            EmpresaId = EmpresaBootstrapId, SucursalId = sucursal, TipoDocumento = 2,
            Prefijo = "DEMO", ReinicioPeriodo = 0, FolioInicial = 8201,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var preview = await client.GetAsync($"{EndpointBase}/{id}");
        Assert.Equal("DEMO-008201", (await ReadJsonAsync(preview)).GetProperty("proximoFolioPreview").GetString());
        async Task<JsonElement> Reserva(string fecha)
        {
            var result = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
            { EmpresaId = EmpresaBootstrapId, SucursalId = sucursal, TipoDocumento = 2, FechaReferencia = fecha });
            result.EnsureSuccessStatusCode();
            return await ReadJsonAsync(result);
        }
        var first = await Reserva("2026-12-31");
        Assert.Equal(8201, first.GetProperty("numero").GetInt64());
        Assert.Equal(id, first.GetProperty("serieId").GetGuid());
        Assert.Equal(8202, (await Reserva("2027-01-01")).GetProperty("numero").GetInt64());
        var edit = await PatchSerieAsync(client, id, await ObtenerVersionSerieAsync(client, id), new { Prefijo = "OTRA" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, edit.StatusCode);
        Assert.Equal("SERIE_USADA_INMUTABLE", (await ReadJsonAsync(edit)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Altas_fiscales_concurrentes_solo_crean_una_serie_activa()
    {
        var client = await CreateSuperAdminClientAsync();
        var sucursal = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(EndpointBase, new
        {
            EmpresaId = EmpresaBootstrapId, SucursalId = sucursal, TipoDocumento = 5,
            Prefijo = RandomPrefijo(), ReinicioPeriodo = 0,
        })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
    }


    // --- Helpers ---

    private static string RandomPrefijo()
    {
        // Máx 10: "T" + 9 hex. Con 3 hex la BD de dev ya chocaba (409).
        return "T" + Guid.NewGuid().ToString("N").Substring(0, 9).ToUpperInvariant();
    }

    private async Task<int> TipoSinSerieGlobalActivaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var usados = await db.Series.AsNoTracking()
            .Where(s => s.EmpresaId == EmpresaBootstrapId && s.SucursalId == null && s.Activa)
            .Select(s => s.TipoDocumento)
            .Distinct()
            .ToListAsync();
        return Enum.GetValues<TipoDocumentoSerie>()
            .Except(usados)
            .Select(tipo => (int)tipo)
            .First();
    }

    private static async Task<Guid> CrearSerieAsync(
        HttpClient client, Guid? sucursalId, int tipoDocumento, string prefijo)
    {
        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = sucursalId,
            TipoDocumento = tipoDocumento,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0,
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> CreateOperativoAsync(Guid sucursalId)
    {
        var random = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-series-{random}";
        var usuarioId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rolId = Guid.CreateVersion7();
            db.Roles.Add(new Rol(rolId, oid, "Rol de prueba de alcance de series"));
            var permisoId = await db.Permisos.AsNoTracking()
                .Where(p => p.Codigo == PermisosCanonicos.AdminSeriesGestionar)
                .Select(p => p.Id).SingleAsync();
            db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            db.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Series"));
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaBootstrapId, rolId, null));
            db.UsuarioSucursales.Add(new UsuarioSucursal(
                Guid.CreateVersion7(), usuarioId, sucursalId, EmpresaBootstrapId));
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Usuario Series");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix)
    {
        var clave = $"{prefix}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var response = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task AssertSucursalForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SUCURSAL_NO_ASOCIADA", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync(bool autoIdempotency = true)
    {
        var client = autoIdempotency
            ? _factory.CreateClientWithIdempotency()
            : _factory.CreateClient();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostReservaAsync(
        HttpClient client, string idempotencyKey, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{EndpointBase}/reservar")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchSerieAsync(
        HttpClient client, Guid id, int version, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{EndpointBase}/{id}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Expected-Version", version.ToString());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DesactivarSerieAsync(
        HttpClient client, Guid id, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{EndpointBase}/{id}/desactivar");
        request.Headers.Add("X-Expected-Version", version.ToString());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return await client.SendAsync(request);
    }

    private static async Task<int> ObtenerVersionSerieAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{EndpointBase}/{id}");
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("serie").GetProperty("version").GetInt32();
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
