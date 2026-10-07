using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>Cuentas: CRUD, reglas R1–R9, concurrencia, idempotencia y permisos (§12). HTTP real + Postgres real. Datos FIX-*.</summary>
public class CatalogoHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Alta_de_titulo_e_hijas_calcula_nivel_y_marca_pendiente_si_faltan_naturaleza_o_tipo()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var raiz = await CrearCuenta(c, Codigo(suf, "100"), "FIX raiz", tipo: "Titulo");
            Assert.Equal(1, raiz.GetProperty("nivel").GetInt32());
            var hija = await CrearCuenta(c, Codigo(suf, "100.10"), padreId: raiz.GetProperty("id").GetGuid(), naturaleza: null, tipo: null);
            Assert.Equal(2, hija.GetProperty("nivel").GetInt32());
            Assert.True(hija.GetProperty("pendienteValidacion").GetBoolean());
            Assert.False(raiz.GetProperty("pendienteValidacion").GetBoolean());
            Assert.Equal(JsonValueKind.Null, hija.GetProperty("naturaleza").ValueKind);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Alta_manual_sugiere_el_siguiente_codigo_y_rechaza_codigos_fuera_de_la_rama_del_padre()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var raiz = await CrearCuenta(c, Codigo(suf, "100.00.00.00"), "FIX raiz", tipo: "Titulo");
            var bancos = await CrearCuenta(c, Codigo(suf, "100.10.00.00"), "FIX bancos", padreId: raiz.GetProperty("id").GetGuid(), tipo: "Titulo");
            var bancosId = bancos.GetProperty("id").GetGuid();
            await CrearCuenta(c, Codigo(suf, "100.10.01.00"), padreId: bancosId, tipo: "Afectable");

            // Sugerencia: siguiente hija libre de la rama, con el mismo ancho.
            var sug = await c.GetAsync($"{Base}/cuentas/siguiente-codigo?padreId={bancosId}");
            Assert.Equal(HttpStatusCode.OK, sug.StatusCode);
            var cuerpo = await Json(sug);
            Assert.Equal(Codigo(suf, "100.10.02.00"), cuerpo.GetProperty("codigo").GetString());
            Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("motivo").ValueKind);

            // La sugerencia se acepta tal cual.
            await CrearCuenta(c, Codigo(suf, "100.10.02.00"), padreId: bancosId, tipo: "Afectable");

            // Otra rama o un nivel saltado: 422 con el código de error y nada se crea.
            foreach (var fuera in new[] { "100.20.01.00", "100.10.02.07" })
            {
                var r = await c.PostAsJsonAsync($"{Base}/cuentas",
                    new { codigo = Codigo(suf, fuera), nombre = "FIX fuera", padreId = bancosId, tipo = "Afectable", cuentaControl = "Ninguna" });
                Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
                Assert.Equal("CONTAB_CUENTA_CODIGO_FUERA_DE_RAMA", await Code(r));
            }

            // P20: un padre afectable también recibe sugerencia (al crear la hija pasará a acumular).
            var hoja = await CrearCuenta(c, Codigo(suf, "100.10.03.00"), padreId: bancosId, tipo: "Afectable");
            var sobreHoja = await c.GetAsync($"{Base}/cuentas/siguiente-codigo?padreId={hoja.GetProperty("id").GetGuid()}");
            Assert.Equal(HttpStatusCode.OK, sobreHoja.StatusCode);
            Assert.Equal(Codigo(suf, "100.10.03.01"), (await Json(sobreHoja)).GetProperty("codigo").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Codigo_repetido_da_409_y_el_codigo_invalido_422()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            await CrearCuenta(c, Codigo(suf, "1"));
            var dup = await c.PostAsJsonAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "1").ToLowerInvariant(), nombre = "otra", cuentaControl = "Ninguna" });
            Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
            Assert.Equal("CONTAB_CUENTA_CODIGO_DUPLICADO", await Code(dup));
            var mal = await c.PostAsJsonAsync($"{Base}/cuentas", new { codigo = "con espacio", nombre = "x", cuentaControl = "Ninguna" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mal.StatusCode);
            Assert.Equal("CONTAB_CUENTA_CODIGO_INVALIDO", await Code(mal));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Carrera_de_dos_altas_con_el_mismo_codigo_deja_una_201_y_una_409()
    {
        var suf = Sufijo();
        try
        {
            var a = await LoginAsync(factory);
            var b = await LoginAsync(factory);
            var cuerpo = new { codigo = Codigo(suf, "7"), nombre = "carrera", cuentaControl = "Ninguna" };
            var rs = await Task.WhenAll(a.PostAsJsonAsync($"{Base}/cuentas", cuerpo), b.PostAsJsonAsync($"{Base}/cuentas", cuerpo));
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], rs.Select(r => r.StatusCode).OrderBy(x => (int)x).ToArray());
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables", $"codigo = '{Codigo(suf, "7")}'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Idempotency_Key_repetida_no_duplica_ni_cambia_la_respuesta()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory, conIdempotencia: false);
            var llave = Guid.NewGuid().ToString("D");
            async Task<HttpResponseMessage> Enviar()
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/cuentas")
                { Content = JsonContent.Create(new { codigo = Codigo(suf, "1"), nombre = "idem", cuentaControl = "Ninguna" }) };
                req.Headers.Add("Idempotency-Key", llave);
                return await c.SendAsync(req);
            }
            var r1 = await Enviar();
            var r2 = await Enviar();
            Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
            Assert.Equal(r1.StatusCode, r2.StatusCode);
            Assert.Equal((await Json(r1)).GetProperty("id").GetGuid(), (await Json(r2)).GetProperty("id").GetGuid());
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables", $"codigo = '{Codigo(suf, "1")}'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Jerarquia_ciclica_por_PUT_es_422_y_el_registro_no_cambia()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var a = await CrearCuenta(c, Codigo(suf, "1"), tipo: "Titulo");
            var b = await CrearCuenta(c, Codigo(suf, "1.1"), padreId: a.GetProperty("id").GetGuid(), tipo: "Titulo");
            var r = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{a.GetProperty("id").GetGuid()}",
                new { nombre = "A", padreId = b.GetProperty("id").GetGuid(), naturaleza = "Deudora", tipo = "Titulo", cuentaControl = "Ninguna" }, Etag(a));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("CONTAB_CUENTA_CICLO", await Code(r));
            var auto = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{a.GetProperty("id").GetGuid()}",
                new { nombre = "A", padreId = a.GetProperty("id").GetGuid(), naturaleza = "Deudora", tipo = "Titulo", cuentaControl = "Ninguna" }, Etag(a));
            Assert.Equal("CONTAB_CUENTA_CICLO", await Code(auto));
            Assert.Equal(a.GetProperty("version").GetInt32(), (await Obtener(c, a.GetProperty("id").GetGuid())).GetProperty("version").GetInt32());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Padre_invalido_inactivo_y_nivel_excedido()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var sinPadre = await c.PostAsJsonAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "2"), nombre = "x", padreId = Guid.NewGuid(), cuentaControl = "Ninguna" });
            Assert.Equal("CONTAB_CUENTA_PADRE_INVALIDO", await Code(sinPadre));

            var titulo = await CrearCuenta(c, Codigo(suf, "3"), tipo: "Titulo");
            (await Send(c, HttpMethod.Post, $"{Base}/cuentas/{titulo.GetProperty("id").GetGuid()}/desactivar", null, Etag(titulo))).EnsureSuccessStatusCode();
            var inactivo = await c.PostAsJsonAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "3.1"), nombre = "x", padreId = titulo.GetProperty("id").GetGuid(), cuentaControl = "Ninguna" });
            Assert.Equal("CONTAB_CUENTA_PADRE_INVALIDO", await Code(inactivo));

            using var limitado = factory.ConEmpresa(null, ("Contabilidad:Catalogo:NivelMaximo", "2"));
            var c2 = await LoginAsync(limitado);
            var n1 = await CrearCuenta(c2, Codigo(suf, "4"), tipo: "Titulo");
            var n2 = await CrearCuenta(c2, Codigo(suf, "4.1"), padreId: n1.GetProperty("id").GetGuid(), tipo: "Titulo");
            var n3 = await c2.PostAsJsonAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "4.1.1"), nombre = "x", padreId = n2.GetProperty("id").GetGuid(), cuentaControl = "Ninguna" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, n3.StatusCode);
            Assert.Equal("CONTAB_CUENTA_NIVEL_EXCEDIDO", await Code(n3));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Mover_un_titulo_recalcula_el_nivel_de_su_subarbol()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var x = await CrearCuenta(c, Codigo(suf, "1"), tipo: "Titulo");
            var y = await CrearCuenta(c, Codigo(suf, "2"), tipo: "Titulo");
            var y1 = await CrearCuenta(c, Codigo(suf, "2.1"), padreId: y.GetProperty("id").GetGuid(), tipo: "Titulo");
            var z = await CrearCuenta(c, Codigo(suf, "1.1"), padreId: x.GetProperty("id").GetGuid(), tipo: "Afectable");
            // Mover X bajo Y.1: X pasa a nivel 3 y Z a nivel 4.
            var r = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{x.GetProperty("id").GetGuid()}",
                new { nombre = "FIX cuenta", padreId = y1.GetProperty("id").GetGuid(), naturaleza = "Deudora", tipo = "Titulo", cuentaControl = "Ninguna" }, Etag(x));
            r.EnsureSuccessStatusCode();
            Assert.Equal(3, (await Json(r)).GetProperty("nivel").GetInt32());
            Assert.Equal(4, (await Obtener(c, z.GetProperty("id").GetGuid())).GetProperty("nivel").GetInt32());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Concurrencia_If_Match_viejo_es_409_sin_header_es_428_y_el_registro_no_cambia()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var a = await CrearCuenta(c, Codigo(suf, "1"));
            var url = $"{Base}/cuentas/{a.GetProperty("id").GetGuid()}";
            var cuerpo = new { nombre = "editada", naturaleza = "Deudora", tipo = "Afectable", cuentaControl = "Ninguna" };
            var ok = await Send(c, HttpMethod.Put, url, cuerpo, Etag(a));
            ok.EnsureSuccessStatusCode();
            var viejo = await Send(c, HttpMethod.Put, url, cuerpo with { nombre = "pisada" }, Etag(a));
            Assert.Equal(HttpStatusCode.Conflict, viejo.StatusCode);
            var sin = await Send(c, HttpMethod.Put, url, cuerpo with { nombre = "pisada" });
            Assert.Equal((HttpStatusCode)428, sin.StatusCode);
            Assert.Equal("editada", (await Obtener(c, a.GetProperty("id").GetGuid())).GetProperty("nombre").GetString());
            var detalle = await c.GetAsync(url);
            Assert.NotNull(detalle.Headers.ETag);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Baja_logica_no_destruye_bloquea_titulo_con_hijas_activas_y_reactivar_exige_padre_activo()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var t = await CrearCuenta(c, Codigo(suf, "1"), tipo: "Titulo");
            var h = await CrearCuenta(c, Codigo(suf, "1.1"), padreId: t.GetProperty("id").GetGuid());
            string Url(JsonElement x, string accion) => $"{Base}/cuentas/{x.GetProperty("id").GetGuid()}/{accion}";

            var bloqueada = await Send(c, HttpMethod.Post, Url(t, "desactivar"), null, Etag(t));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bloqueada.StatusCode);
            Assert.Equal("CONTAB_CUENTA_BAJA_CON_HIJAS_ACTIVAS", await Code(bloqueada));

            var hBaja = await Json(await Send(c, HttpMethod.Post, Url(h, "desactivar"), null, Etag(h)));
            Assert.False(hBaja.GetProperty("activa").GetBoolean());
            var tBaja = await Json(await Send(c, HttpMethod.Post, Url(t, "desactivar"), null, Etag(t)));
            Assert.False(tBaja.GetProperty("activa").GetBoolean());
            // sigue legible y sin tocar a las hijas
            Assert.Equal(Codigo(suf, "1.1"), (await Obtener(c, h.GetProperty("id").GetGuid())).GetProperty("codigo").GetString());

            var hReact = await Send(c, HttpMethod.Post, Url(h, "reactivar"), null, Etag(hBaja));
            Assert.Equal("CONTAB_CUENTA_PADRE_INVALIDO", await Code(hReact));
            var tReact = await Json(await Send(c, HttpMethod.Post, Url(t, "reactivar"), null, Etag(tBaja)));
            Assert.True(tReact.GetProperty("activa").GetBoolean());
            (await Send(c, HttpMethod.Post, Url(h, "reactivar"), null, Etag(hBaja))).EnsureSuccessStatusCode();
            // El código de una cuenta dada de baja no se libera (P9): ya cubierto por índice único no parcial.
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables", $"codigo = '{Codigo(suf, "1.1")}'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Codigo_de_cuenta_inactiva_no_se_reutiliza()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var a = await CrearCuenta(c, Codigo(suf, "1"));
            (await Send(c, HttpMethod.Post, $"{Base}/cuentas/{a.GetProperty("id").GetGuid()}/desactivar", null, Etag(a))).EnsureSuccessStatusCode();
            var dup = await c.PostAsJsonAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "1"), nombre = "reuso", cuentaControl = "Ninguna" });
            Assert.Equal("CONTAB_CUENTA_CODIGO_DUPLICADO", await Code(dup));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Cambio_incompatible_sobre_cuenta_usada_se_bloquea_sin_alterar_el_historico()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var t = await CrearCuenta(c, Codigo(suf, "1"), tipo: "Titulo");
            var h = await CrearCuenta(c, Codigo(suf, "1.1"), padreId: t.GetProperty("id").GetGuid());
            var hId = h.GetProperty("id").GetGuid();
            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using (var scope = enEmpresa.Services.CreateScope())
                Assert.True(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegistrarUsoCuentaCommand(hId, "FIX-POLIZAS", "FIX-REF-1")));

            var antes = await Escalar<DateTime>(factory.Services, $"SELECT updated_at FROM contabilidad.cuentas_contables WHERE id = '{hId}'");
            var auditoriaAntes = await Escalar<long>(factory.Services, $"SELECT count(*) FROM core.audit_log WHERE entidad_id = '{hId}'");

            foreach (var cuerpo in new object[]
            {
                new { nombre = "FIX cuenta", padreId = (Guid?)t.GetProperty("id").GetGuid(), naturaleza = "Acreedora", tipo = "Afectable", cuentaControl = "Ninguna" }, // naturaleza
                new { nombre = "FIX cuenta", padreId = (Guid?)null, naturaleza = "Deudora", tipo = "Afectable", cuentaControl = "Ninguna" },                          // padre (y con él el tipo derivado)
            })
            {
                var r = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{hId}", cuerpo, Etag(h));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
                var problema = await Json(r);
                Assert.Equal("CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO", problema.GetProperty("code").GetString());
                Assert.Contains("ya tiene movimientos", problema.GetProperty("detail").GetString());
                Assert.Contains("procedimiento de impacto", problema.GetProperty("detail").GetString());
            }
            // Un ancestro de una cuenta usada también queda protegido.
            var anc = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{t.GetProperty("id").GetGuid()}",
                new { nombre = "FIX cuenta", padreId = (Guid?)null, naturaleza = "Acreedora", tipo = "Titulo", cuentaControl = "Ninguna" }, Etag(t));
            Assert.Equal("CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO", await Code(anc));

            // Nada cambió: misma versión, mismo updated_at, sin nuevas entradas de auditoría, uso intacto.
            Assert.Equal(h.GetProperty("version").GetInt32(), (await Obtener(c, hId)).GetProperty("version").GetInt32());
            Assert.Equal(antes, await Escalar<DateTime>(factory.Services, $"SELECT updated_at FROM contabilidad.cuentas_contables WHERE id = '{hId}'"));
            Assert.Equal(auditoriaAntes, await Escalar<long>(factory.Services, $"SELECT count(*) FROM core.audit_log WHERE entidad_id = '{hId}'"));
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables_uso", $"cuenta_id = '{hId}'"));

            // Los cambios no sensibles siguen permitidos.
            var nombre = await Send(c, HttpMethod.Put, $"{Base}/cuentas/{hId}",
                new { nombre = "FIX nombre nuevo", padreId = (Guid?)t.GetProperty("id").GetGuid(), naturaleza = "Deudora", tipo = "Afectable", cuentaControl = "Ninguna", grupoReporte = "FIX-GRP" }, Etag(h));
            nombre.EnsureSuccessStatusCode();
            Assert.Equal("FIX nombre nuevo", (await Json(nombre)).GetProperty("nombre").GetString());
            Assert.True((await Obtener(c, hId)).GetProperty("usada").GetBoolean());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Registrar_uso_es_idempotente_y_solo_lo_escribe_el_comando_interno()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var a = await CrearCuenta(c, Codigo(suf, "1"));
            using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
            using var scope = enEmpresa.Services.CreateScope();
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            var id = a.GetProperty("id").GetGuid();
            Assert.True(await m.Send(new RegistrarUsoCuentaCommand(id, "FIX-X", "R1")));
            Assert.False(await m.Send(new RegistrarUsoCuentaCommand(id, "FIX-X", "R1")));
            Assert.True(await m.Send(new RegistrarUsoCuentaCommand(id, "FIX-X", "R2")));
            Assert.Equal(2, await Contar(factory.Services, "cuentas_contables_uso", $"cuenta_id = '{id}'"));
            await AssertCreacionesAuditadasAsync(factory.Services, "cuentas_contables_uso", "CuentaContableUso",
                $"r.cuenta_id = '{id}'", "Referencia", 2, actorTipo: "sistema");
            // No hay ruta HTTP que escriba uso.
            var r = await c.PostAsJsonAsync($"{Base}/cuentas/{id}/uso", new { });
            Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    // ── Permisos ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Usuario_de_consulta_lee_pero_no_modifica_ni_importa_y_sin_token_es_401()
    {
        var suf = Sufijo();
        try
        {
            var admin = await LoginAsync(factory);
            var cuenta = await CrearCuenta(admin, Codigo(suf, "1"));
            var id = cuenta.GetProperty("id").GetGuid();
            var consulta = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadCatalogoLeer);

            Assert.Equal(HttpStatusCode.OK, (await consulta.GetAsync($"{Base}/cuentas?q={suf}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await consulta.GetAsync($"{Base}/cuentas/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await consulta.GetAsync($"{Base}/cuentas/arbol")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await consulta.GetAsync($"{Base}/configuracion-formato")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await consulta.GetAsync($"{Base}/importaciones")).StatusCode);

            var csv = FixturesCatalogoRequest(suf);
            var denegados = new[]
            {
                await consulta.PostAsJsonAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "2"), nombre = "x", cuentaControl = "Ninguna" }),
                await Send(consulta, HttpMethod.Put, $"{Base}/cuentas/{id}", new { nombre = "x", cuentaControl = "Ninguna" }, Etag(cuenta)),
                await Send(consulta, HttpMethod.Post, $"{Base}/cuentas/{id}/desactivar", null, Etag(cuenta)),
                await Send(consulta, HttpMethod.Post, $"{Base}/cuentas/{id}/reactivar", null, Etag(cuenta)),
                await consulta.PostAsJsonAsync($"{Base}/importaciones/vista-previa", csv),
                await consulta.PostAsJsonAsync($"{Base}/importaciones/perfilado", csv),
                await consulta.PostAsJsonAsync($"{Base}/importaciones", csv),
            };
            Assert.All(denegados, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
            Assert.Equal(1, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));

            using var anonimo = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"{Base}/cuentas")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync($"{Base}/importaciones/perfilado", csv)).StatusCode);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Administrar_no_implica_importar()
    {
        var suf = Sufijo();
        try
        {
            var op = await ClienteConPermisosAsync(factory, PermisosCanonicos.ContabilidadCatalogoLeer, PermisosCanonicos.ContabilidadCatalogoAdministrar);
            await CrearCuenta(op, Codigo(suf, "1"));
            var r = await op.PostAsJsonAsync($"{Base}/importaciones/vista-previa", FixturesCatalogoRequest(suf));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    // ── Configuración ────────────────────────────────────────────────────────

    [Fact]
    public async Task Configuracion_de_formato_expone_los_valores_activos_y_defaults_permisivos()
    {
        var c = await LoginAsync(factory);
        var cfg = await Json(await c.GetAsync($"{Base}/configuracion-formato"));
        Assert.Equal(10, cfg.GetProperty("nivelMaximo").GetInt32());
        Assert.Equal(5000, cfg.GetProperty("importacion").GetProperty("maxFilas").GetInt32());
        Assert.False(cfg.GetProperty("herenciaNaturaleza").GetBoolean());
        Assert.Equal(0, cfg.GetProperty("cuentasControl").GetArrayLength());
        Assert.Equal("PorSegmentos", cfg.GetProperty("jerarquia").GetProperty("modo").GetString());

        using var otra = factory.ConEmpresa(null, ("Contabilidad:Catalogo:NivelMaximo", "4"), ("Contabilidad:Catalogo:Jerarquia:Modo", "PorColumna"));
        var c2 = await LoginAsync(otra);
        var cfg2 = await Json(await c2.GetAsync($"{Base}/configuracion-formato"));
        Assert.Equal(4, cfg2.GetProperty("nivelMaximo").GetInt32());
        Assert.Equal("PorColumna", cfg2.GetProperty("jerarquia").GetProperty("modo").GetString());
    }

    [Fact]
    public void Configuracion_inconsistente_tumba_el_arranque_con_mensaje_claro()
    {
        using var mala = factory.WithWebHostBuilder(b => b.UseSetting("Contabilidad:Catalogo:Codigo:Patron", "(["));
        var ex = Assert.ThrowsAny<Exception>(() => mala.CreateClient());
        Assert.Contains("Codigo.Patron", ex.ToString());
    }

    // ── Aislamiento por empresa ──────────────────────────────────────────────

    [Fact]
    public async Task EmpresaId_enviado_en_el_body_se_ignora_la_cuenta_nace_en_la_empresa_del_token()
    {
        var suf = Sufijo();
        try
        {
            var c = await LoginAsync(factory);
            var r = await c.PostAsJsonAsync($"{Base}/cuentas", new { codigo = Codigo(suf, "1"), nombre = "x", cuentaControl = "Ninguna", empresaId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Equal(EmpresaBootstrapId, await Escalar<Guid>(factory.Services, $"SELECT empresa_id FROM contabilidad.cuentas_contables WHERE codigo = '{Codigo(suf, "1")}'"));
            var lista = await c.GetAsync($"{Base}/cuentas?q={suf}&empresaId={Guid.NewGuid()}");
            Assert.Equal(1, (await Json(lista)).GetProperty("total").GetInt32());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Otra_empresa_no_ve_ni_modifica_cuentas_ajenas_y_puede_repetir_el_codigo()
    {
        var suf = Sufijo();
        var empresaB = Guid.NewGuid(); // el esquema no tiene FK a empresas: basta un id distinto
        try
        {
            var c = await LoginAsync(factory);
            var a = await CrearCuenta(c, Codigo(suf, "1"));
            var idA = a.GetProperty("id").GetGuid();

            using var b = factory.ConEmpresa(empresaB);
            using var scope = b.Services.CreateScope();
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();

            var lista = await m.Send(new ListarCuentasQuery(null, null, suf, null, null, 0, 50));
            Assert.Equal(0, lista.Total);
            await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.EntityNotFoundException>(() => m.Send(new ObtenerCuentaQuery(idA)));
            await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.EntityNotFoundException>(() =>
                m.Send(new EditarCuentaCommand(idA, a.GetProperty("version").GetInt32(), "x", null, null, null, CuentaControl.Ninguna, null, null)));
            await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.EntityNotFoundException>(() =>
                m.Send(new DesactivarCuentaCommand(idA, a.GetProperty("version").GetInt32())));
            // Mismo código en la empresa B: permitido.
            var enB = await m.Send(new CrearCuentaCommand(Codigo(suf, "1"), "de B", null, NaturalezaCuenta.Deudora, TipoCuenta.Afectable, CuentaControl.Ninguna, null, null));
            Assert.NotEqual(idA, enB.Id);
            Sobre.Value = null; // de vuelta a la empresa del token para las verificaciones HTTP
            Assert.Equal(2, await Contar(factory.Services, "cuentas_contables", $"codigo = '{Codigo(suf, "1")}'"));
            // A sigue intacta.
            Assert.Equal("FIX cuenta", (await Obtener(c, idA)).GetProperty("nombre").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    private static Millet.Contabilidad.Application.Importacion.ImportacionRequest FixturesCatalogoRequest(string suf) =>
        Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo.Request($"codigo;nombre\n{Codigo(suf, "9")};FIX x\n", $"FIX-{suf}-F");
}
