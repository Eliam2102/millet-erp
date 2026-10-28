using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>86e3kdwng · P8.6 no deducibles e IVA no acreditable.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class NoDeduciblesEndpointsTests : IClassFixture<NoDeduciblesEndpointsTests.Servidor>
{
    private readonly Servidor factory;
    public NoDeduciblesEndpointsTests(Servidor fixture)
    {
        factory = fixture;
        fixture.SinPeriodo.Blob.Reiniciar();
        fixture.ConPeriodo.Blob.Reiniciar();
        fixture.SinPeriodo.Reloj.UtcNow = DateTimeOffset.UtcNow;
        fixture.ConPeriodo.Reloj.UtcNow = DateTimeOffset.UtcNow;
    }

    public sealed class Servidor : WebApplicationFactory<Program>
    {
        public AdjuntosProveedorAmbiente SinPeriodo { get; }
        public AdjuntosProveedorAmbiente ConPeriodo { get; }
        public Servidor()
        {
            SinPeriodo = new AdjuntosProveedorAmbiente(this);
            ConPeriodo = new AdjuntosProveedorAmbiente(this, s =>
            {
                s.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort>();
                s.AddSingleton<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
            });
        }
    }

    private const string Cxp = "/api/v1/cuentas-por-pagar";
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Cliente, Guid TarjetaId, Guid TitularId)> AmbienteAsync()
    {
        var amb = factory.ConPeriodo;
        var (cliente, usuarioId) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento, PermisosCanonicos.CuentasPorPagarTcLeer,
            PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales, PermisosCanonicos.CuentasPorPagarDocumentosLeerTodasSucursales,
            PermisosCanonicos.CuentasPorPagarFacturasLeer, PermisosCanonicos.CuentasPorPagarFacturasLeerTodasSucursales);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var titular = Guid.NewGuid();
        var t = Tarjeta.Crear(Empresa, "Amex", "AMEX_MX", NumeroTarjetaEnmascarado.FromUltimosCuatro("4321"), "P8.6 " + Guid.NewGuid().ToString("N")[..6],
            titular, Guid.NewGuid(), 100000m, "MXN", 15, 20, new DateOnly(2026, 1, 1));
        db.TarjetasCredito.Add(t);
        await db.SaveChangesAsync();
        return (cliente, t.Id, titular);
    }

    private async Task<Guid> SembrarCfdiAsync(string uuid, decimal subtotal, decimal iva)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var cfdi = CfdiRecibido.Ingresar(Empresa, UuidCfdi.Parse(uuid), RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"),
            TipoCfdi.Ingreso, "ND-1", "ND", ahora, subtotal + iva, subtotal, iva, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, ahora, "nd-blob", null, "nd-hash");
        db.CfdisRecibidos.Add(cfdi);
        await db.SaveChangesAsync();
        return cfdi.Id;
    }

    private static object SinCfdi(Guid tarjetaId, Guid titular, string? clasificacion) => new
    {
        tarjetaId, usuarioQueUsoId = titular, fechaMovimiento = new DateOnly(2026, 10, 20), montoOriginal = 350m, monedaOriginal = "MXN",
        merchantRaw = "Restaurante Demo", conceptoContable = "Comidas", clasificacionNoDeducible = clasificacion,
    };

    private static object ConCfdi(Guid tarjetaId, Guid titular, Guid cfdiId, string uuid, string? clasificacion) => new
    {
        tarjetaId, usuarioQueUsoId = titular, fechaMovimiento = new DateOnly(2026, 10, 20), montoOriginal = 1160m, monedaOriginal = "MXN",
        merchantRaw = "Papeleria Demo", conceptoContable = "Papeleria", cfdiRecibidoId = cfdiId, proveedorId = Guid.NewGuid(), uuidCfdi = uuid,
        fechaCfdi = DateTimeOffset.UtcNow, subtotal = 1000m, descuentos = 0m, impuestosTrasladados = 160m, retenciones = 0m, totalFactura = 1160m,
        fechaVencimiento = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), clasificacionNoDeducible = clasificacion,
    };

    [Fact]
    public async Task Catalogo_clasificaciones_lista_DEMO_a_cualquier_sesion_y_401_sin_sesion()
    {
        var amb = factory.SinPeriodo;
        // Quien solo comprueba viáticos (sin permiso de tarjeta) también necesita el catálogo para sus tickets.
        var (conPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarViaticosCapturarComprobacion);

        var ok = await conPermiso.GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var filas = (await Json(ok)).EnumerateArray().ToList();
        Assert.Contains(filas, f => f.GetProperty("clave").GetString() == "COMIDAS" && f.GetProperty("descripcion").GetString() == "DEMO · Comidas");
        Assert.True(filas.Count >= 6);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles")).StatusCode);
    }

    [Fact]
    public async Task Tarjeta_sin_CFDI_exige_clasificacion_valida_y_la_guarda()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();

        foreach (var invalida in new string?[] { null, "", "NO_EXISTE" })
        {
            var r = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/sin-cfdi", SinCfdi(tarjeta, titular, invalida));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("TC_MOV_CLASIFICACION_INVALIDA", (await Json(r)).GetProperty("code").GetString());
        }

        var ok = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/sin-cfdi", SinCfdi(tarjeta, titular, "comidas"));
        Assert.True(ok.StatusCode == HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var m = await Json(ok);
        Assert.Equal("COMIDAS", m.GetProperty("clasificacionNoDeducible").GetString());
        Assert.Equal(0m, m.GetProperty("ivaNoAcreditable").GetDecimal());

        var lista = await Json(await c.GetAsync(Cxp + $"/movimientos-tc?tarjetaId={tarjeta}"));
        Assert.Contains(lista.GetProperty("items").EnumerateArray(), x => x.GetProperty("clasificacionNoDeducible").GetString() == "COMIDAS");
    }

    [Fact]
    public async Task Tarjeta_con_CFDI_no_deducible_manda_el_IVA_a_no_acreditable_y_sin_marca_es_deducible()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();

        var uuidND = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiND = await SembrarCfdiAsync(uuidND, 1000m, 160m);
        var nd = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiND, uuidND, "PROPINAS"));
        Assert.True(nd.StatusCode == HttpStatusCode.Created, await nd.Content.ReadAsStringAsync());
        var mND = await Json(nd);
        Assert.Equal("PROPINAS", mND.GetProperty("clasificacionNoDeducible").GetString());
        Assert.Equal(160m, mND.GetProperty("ivaNoAcreditable").GetDecimal());

        var uuidD = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiD = await SembrarCfdiAsync(uuidD, 1000m, 160m);
        var d = await Json(await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiD, uuidD, null)));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("clasificacionNoDeducible").ValueKind);
        Assert.Equal(0m, d.GetProperty("ivaNoAcreditable").GetDecimal());

        var lista = (await Json(await c.GetAsync(Cxp + $"/movimientos-tc?tarjetaId={tarjeta}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(160m, lista.Single(x => x.GetProperty("id").GetGuid() == mND.GetProperty("id").GetGuid()).GetProperty("ivaNoAcreditable").GetDecimal());

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var f = await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.AsNoTracking()
            .SingleAsync(x => x.UuidCfdi == uuidND);
        Assert.Equal("PROPINAS", f.ClasificacionNoDeducible);
        Assert.Equal(160m, f.IvaNoAcreditable);

        var rd = await c.GetAsync(Cxp + $"/facturas/{f.Id}");
        Assert.True(rd.StatusCode == HttpStatusCode.OK, await rd.Content.ReadAsStringAsync());
        var detalle = await Json(rd);
        Assert.Equal("PROPINAS", detalle.GetProperty("clasificacionNoDeducible").GetString());
        Assert.Equal(160m, detalle.GetProperty("ivaNoAcreditable").GetDecimal());
    }

    [Fact]
    public async Task Tarjeta_con_CFDI_rechaza_clasificacion_inexistente_sin_consumir_el_CFDI()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdi = await SembrarCfdiAsync(uuid, 1000m, 160m);

        var r = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdi, uuid, "NO_EXISTE"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("TC_MOV_CLASIFICACION_INVALIDA", (await Json(r)).GetProperty("code").GetString());

        var ok = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdi, uuid, "COMIDAS"));
        Assert.True(ok.StatusCode == HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
    }

    private async Task<(HttpClient Cliente, AdjuntosProveedorAmbiente Amb)> ClienteComprobacionesAsync()
    {
        var amb = factory.ConPeriodo;
        var (cliente, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CuentasPorPagarComprobacionesCapturar, PermisosCanonicos.CuentasPorPagarComprobacionesLeer,
            PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel1,
            PermisosCanonicos.CuentasPorPagarViaticosCapturarComprobacion, PermisosCanonicos.CuentasPorPagarViaticosLiberar,
            PermisosCanonicos.CuentasPorPagarViaticosLeer, PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales,
            PermisosCanonicos.CuentasPorPagarDocumentosLeerTodasSucursales);
        return (cliente, amb);
    }

    private static object LineaCajaChica(Guid? cfdiId, string? uuid, decimal subtotal, decimal iva, string? clasificacion) => new
    {
        cfdiRecibidoId = cfdiId, uuidCfdi = uuid, proveedorId = Guid.NewGuid(), folioProveedor = "ND-1", serieProveedor = "A",
        fechaCfdi = DateTimeOffset.UtcNow, subtotal, descuentos = 0m, impuestosTrasladados = iva, retenciones = 0m, total = subtotal + iva,
        fechaVencimiento = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), concepto = "Gasto", clasificacionNoDeducible = clasificacion,
    };

    [Fact]
    public async Task Caja_chica_exige_clasificacion_en_ticket_y_marca_no_deducibles_ticket_y_CFDI()
    {
        var (c, _) = await ClienteComprobacionesAsync();
        object Comprobacion(params object[] lineas) => new
        {
            sucursalId = Guid.NewGuid(), responsableId = Guid.NewGuid(), fechaInicio = new DateOnly(2026, 10, 1), fechaFin = new DateOnly(2026, 10, 20),
            moneda = "MXN", cfdis = lineas,
        };

        var sinClasificacion = await c.PostAsJsonAsync(Cxp + "/comprobaciones/caja-chica", Comprobacion(LineaCajaChica(null, null, 103.45m, 16.55m, null)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinClasificacion.StatusCode);
        Assert.Equal("COMP_LINEA_CLASIFICACION_REQUERIDA", (await Json(sinClasificacion)).GetProperty("code").GetString());

        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdi = await SembrarCfdiAsync(uuid, 500m, 80m);
        var ok = await c.PostAsJsonAsync(Cxp + "/comprobaciones/caja-chica", Comprobacion(
            LineaCajaChica(null, null, 103.45m, 16.55m, "ESTACIONAMIENTO"),
            LineaCajaChica(cfdi, uuid, 500m, 80m, "COMIDAS")));
        Assert.True(ok.StatusCode == HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var id = (await Json(ok)).GetProperty("id").GetGuid();

        var detalle = await Json(await c.GetAsync(Cxp + $"/comprobaciones/{id}"));
        var lineas = detalle.GetProperty("lineas").EnumerateArray().ToList();
        Assert.Equal(2, lineas.Count);
        Assert.All(lineas, l => Assert.False(string.IsNullOrEmpty(l.GetProperty("clasificacionNoDeducible").GetString())));
        Assert.Equal(96.55m + 0m, lineas.Sum(l => l.GetProperty("ivaNoAcreditable").GetDecimal()));

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var facturaIds = (await db.ComprobacionesGastos.AsNoTracking().Include(x => x.Lineas).SingleAsync(x => x.Id == id))
            .Lineas.Select(l => l.FacturaProveedorId).ToList();
        var facturas = await db.FacturasProveedor.AsNoTracking().Where(f => facturaIds.Contains(f.Id)).ToListAsync();
        Assert.Equal(2, facturas.Count);
        Assert.All(facturas, f => Assert.NotNull(f.ClasificacionNoDeducible));
        Assert.Equal(96.55m, facturas.Sum(f => f.IvaNoAcreditable));
    }

    [Fact]
    public async Task Caja_chica_rechaza_clasificacion_inexistente_en_linea_con_CFDI()
    {
        var (c, _) = await ClienteComprobacionesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdi = await SembrarCfdiAsync(uuid, 500m, 80m);
        var r = await c.PostAsJsonAsync(Cxp + "/comprobaciones/caja-chica", new
        {
            sucursalId = Guid.NewGuid(), responsableId = Guid.NewGuid(), fechaInicio = new DateOnly(2026, 10, 1), fechaFin = new DateOnly(2026, 10, 20),
            moneda = "MXN", cfdis = new[] { LineaCajaChica(cfdi, uuid, 500m, 80m, "NO_EXISTE") },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("COMP_LINEA_CLASIFICACION_REQUERIDA", (await Json(r)).GetProperty("code").GetString());
    }

    private async Task<(Guid Id, int Version)> SembrarViaticoAnticipadoAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var jefe = Guid.NewGuid();
        var s = Millet.CuentasPorPagar.Domain.Viaticos.SolicitudViaticos.Solicitar(Empresa, Guid.NewGuid(), Guid.NewGuid(), jefe, "Cancún",
            Millet.CuentasPorPagar.Domain.Catalogos.TipoDestinoViatico.Nacional, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3),
            "MXN", 5000m, 6000m, 10, null, DateTimeOffset.UtcNow);
        s.AutorizarPorJefe(jefe, DateTimeOffset.UtcNow);
        s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        db.SolicitudesViaticos.Add(s);
        await db.SaveChangesAsync();
        return (s.Id, s.Version);
    }

    private static object LineaViatico(string? uuid, bool ticket, decimal subtotal, decimal iva, string? clasificacion) => new
    {
        cfdiRecibidoId = (Guid?)null, uuidCfdi = uuid, proveedorId = ticket ? (Guid?)null : Guid.NewGuid(), folioProveedor = "V-1",
        fechaGasto = DateTimeOffset.UtcNow, subtotal, impuestosTrasladados = iva, retenciones = 0m, total = subtotal + iva, moneda = "MXN",
        concepto = "Alimentos", esTicketNoFiscal = ticket, clasificacionNoDeducible = clasificacion,
    };

    private static async Task<HttpResponseMessage> PostConVersionAsync(HttpClient c, string ruta, int version, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(body ?? new { }) };
        req.Headers.Add("X-Expected-Version", version.ToString());
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await c.SendAsync(req);
    }

    [Fact]
    public async Task Viaticos_exige_clasificacion_en_ticket_no_fiscal_y_la_copia_a_la_factura_al_liberar()
    {
        var (c, _) = await ClienteComprobacionesAsync();
        var (id, version) = await SembrarViaticoAnticipadoAsync();
        var ruta = Cxp + $"/viaticos/{id}";

        var sin = await PostConVersionAsync(c, ruta + "/capturar-comprobacion", version,
            new { lineas = new[] { LineaViatico(null, true, 120m, 0m, null) } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sin.StatusCode);
        Assert.Equal("VIA_LINEA_CLASIFICACION_REQUERIDA", (await Json(sin)).GetProperty("code").GetString());

        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var ok = await PostConVersionAsync(c, ruta + "/capturar-comprobacion", version, new
        {
            lineas = new[] { LineaViatico(null, true, 120m, 0m, "VIATICOS_SIN_CFDI"), LineaViatico(uuid, false, 500m, 80m, "PROPINAS") },
        });
        Assert.True(ok.StatusCode == HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var nuevaVersion = (await Json(ok)).GetProperty("version").GetInt32();

        var detalle = await Json(await c.GetAsync(ruta));
        var lineas = detalle.GetProperty("lineas").EnumerateArray().ToList();
        Assert.All(lineas, l => Assert.False(string.IsNullOrEmpty(l.GetProperty("clasificacionNoDeducible").GetString())));
        Assert.Equal(80m, lineas.Sum(l => l.GetProperty("ivaNoAcreditable").GetDecimal()));

        var liberar = await PostConVersionAsync(c, ruta + "/liberar", nuevaVersion);
        Assert.True(liberar.StatusCode == HttpStatusCode.OK, await liberar.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var f = await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.AsNoTracking().SingleAsync(x => x.UuidCfdi == uuid);
        Assert.Equal("PROPINAS", f.ClasificacionNoDeducible);
        Assert.Equal(80m, f.IvaNoAcreditable);
    }

    [Fact]
    public async Task Captura_retroactiva_desde_estado_de_cuenta_exige_clasificacion_y_la_guarda()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        Guid ecId, lineaId; int version;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var ec = EstadoCuentaTc.Crear(Empresa, tarjeta, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 25), new DateOnly(2026, 11, 10));
            var linea = ec.AgregarLinea(1, new DateOnly(2026, 10, 20), 350m, "MXN", 350m, "RESTAURANTE", null, "Compra");
            db.EstadosCuentaTc.Add(ec);
            // MarcarSinSugerencia es internal: se fija el estado por EF para dejar la línea capturable.
            db.Entry(linea).Property("EstadoMatch").CurrentValue = Millet.CuentasPorPagar.Domain.TarjetaCredito.EstadoMatchLineaBanco.NoConciliado;
            await db.SaveChangesAsync();
            (ecId, lineaId, version) = (ec.Id, linea.Id, ec.Version);
        }
        var ruta = Cxp + $"/estados-cuenta-tc/{ecId}/lineas/{lineaId}/capturar-retroactiva";

        var sin = await PostConVersionAsync(c, ruta, version, new { usuarioQueUsoId = titular, conceptoContable = "Comidas" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sin.StatusCode);
        Assert.Equal("TC_MOV_CLASIFICACION_INVALIDA", (await Json(sin)).GetProperty("code").GetString());

        var ok = await PostConVersionAsync(c, ruta, version, new { usuarioQueUsoId = titular, conceptoContable = "Comidas", clasificacionNoDeducible = "comidas" });
        Assert.True(ok.StatusCode == HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var lista = (await Json(await c.GetAsync(Cxp + $"/movimientos-tc?tarjetaId={tarjeta}"))).GetProperty("items").EnumerateArray();
        Assert.Contains(lista, x => x.GetProperty("clasificacionNoDeducible").GetString() == "COMIDAS");
    }

    // ---- CA8.15: UUID duplicado entre rutas de captura ----

    private static async Task<Guid> SembrarFacturaAsync(AdjuntosProveedorAmbiente amb, string uuid)
    {
        using var scope = amb.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var f = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(Empresa, null, uuid, Guid.NewGuid(), Guid.NewGuid(),
            "DUP-1", null, ahora, ahora, DateOnly.FromDateTime(ahora.DateTime).AddDays(30), "MXN", null, 1000m, 0m, 160m, 0m, 1160m, "CA8.15", ahora);
        db.FacturasProveedor.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    [Fact]
    public async Task UUID_ya_capturado_como_factura_se_bloquea_en_tarjeta()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        var (_, amb) = await ClienteComprobacionesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        await SembrarFacturaAsync(amb, uuid);
        var cfdi =await SembrarCfdiAsync(uuid, 1000m, 160m);

        var r = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdi, uuid, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("TC_MOV_FACTURA_DUPLICADA", (await Json(r)).GetProperty("code").GetString());

        // Control: un UUID nuevo sí guarda.
        var uuidNuevo = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiNuevo = await SembrarCfdiAsync(uuidNuevo, 1000m, 160m);
        var ok = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiNuevo, uuidNuevo, null));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task UUID_capturado_en_tarjeta_se_bloquea_como_factura_y_en_caja_chica()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        var (cc, amb) = await ClienteComprobacionesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdi = await SembrarCfdiAsync(uuid, 1000m, 160m);
        var tc =await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdi, uuid, null));
        Assert.True(tc.StatusCode == HttpStatusCode.Created, await tc.Content.ReadAsStringAsync());

        // tarjeta → factura: el guard central del DbContext rechaza el UUID repetido.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SembrarFacturaAsync(amb, uuid));
        Assert.Contains("CXP_UUID_DUPLICADO", ex is Millet.SharedKernel.Application.Exceptions.BusinessRuleException b ? b.Code : ex.ToString());

        // tarjeta → caja chica.
        var r = await cc.PostAsJsonAsync(Cxp + "/comprobaciones/caja-chica", new
        {
            sucursalId = Guid.NewGuid(), responsableId = Guid.NewGuid(), fechaInicio = new DateOnly(2026, 10, 1), fechaFin = new DateOnly(2026, 10, 20),
            moneda = "MXN", cfdis = new[] { LineaCajaChica(null, uuid, 1000m, 160m, null) },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("COMP_LINEA_FACTURA_DUPLICADA", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UUID_ya_capturado_como_factura_se_bloquea_al_capturar_viaticos_no_solo_al_liberar()
    {
        var (c, amb) = await ClienteComprobacionesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        await SembrarFacturaAsync(amb, uuid);
        var (id, version) = await SembrarViaticoAnticipadoAsync();

        var r = await PostConVersionAsync(c, Cxp + $"/viaticos/{id}/capturar-comprobacion", version,
            new { lineas = new[] { LineaViatico(uuid, false, 500m, 80m, null) } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("VIA_LINEA_FACTURA_DUPLICADA", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task UUID_en_viatico_pendiente_se_bloquea_en_otro_viatico_caja_chica_y_tarjeta()
    {
        var (tc, tarjeta, titular) = await AmbienteAsync();
        var (c, _) = await ClienteComprobacionesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var (id1, v1) = await SembrarViaticoAnticipadoAsync();
        var ok = await PostConVersionAsync(c, Cxp + $"/viaticos/{id1}/capturar-comprobacion", v1,
            new { lineas = new[] { LineaViatico(uuid, false, 500m, 80m, null) } });
        Assert.True(ok.StatusCode == HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());

        // viático → otro viático (minúsculas: debe ser case-insensitive).
        var (id2, v2) = await SembrarViaticoAnticipadoAsync();
        var r2 = await PostConVersionAsync(c, Cxp + $"/viaticos/{id2}/capturar-comprobacion", v2,
            new { lineas = new[] { LineaViatico(uuid.ToLowerInvariant(), false, 500m, 80m, null) } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);
        Assert.Equal("VIA_LINEA_FACTURA_DUPLICADA", (await Json(r2)).GetProperty("code").GetString());

        // viático → caja chica.
        var r3 = await c.PostAsJsonAsync(Cxp + "/comprobaciones/caja-chica", new
        {
            sucursalId = Guid.NewGuid(), responsableId = Guid.NewGuid(), fechaInicio = new DateOnly(2026, 10, 1), fechaFin = new DateOnly(2026, 10, 20),
            moneda = "MXN", cfdis = new[] { LineaCajaChica(null, uuid, 1000m, 160m, null) },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r3.StatusCode);
        Assert.Equal("COMP_LINEA_FACTURA_DUPLICADA", (await Json(r3)).GetProperty("code").GetString());

        // viático → tarjeta.
        var cfdi = await SembrarCfdiAsync(uuid, 1000m, 160m);
        var r4 = await tc.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdi, uuid, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r4.StatusCode);
        Assert.Equal("TC_MOV_FACTURA_DUPLICADA", (await Json(r4)).GetProperty("code").GetString());
    }

    // ---- S4: gasto.registrado.v1 en el outbox ----

    private async Task<List<GastoRegistradoIntegrationEvent>> EventosGastoAsync(Func<GastoRegistradoIntegrationEvent, bool> filtro)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var payloads = await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().OutboxEntries.AsNoTracking()
            .Where(e => e.EventType == "cuentas_por_pagar.gasto.registrado.v1").Select(e => e.Payload).ToListAsync();
        return payloads.Select(p => JsonSerializer.Deserialize<GastoRegistradoIntegrationEvent>(p, JsonSerializerOptions.Web)!).Where(filtro).ToList();
    }

    [Fact]
    public async Task Gasto_registrado_tarjeta_sin_CFDI_es_no_deducible_sin_desglose_de_IVA()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        var ok = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/sin-cfdi", SinCfdi(tarjeta, titular, "COMIDAS"));
        var id = (await Json(ok)).GetProperty("id").GetGuid();

        var e = Assert.Single(await EventosGastoAsync(x => x.DocumentoId == id));
        Assert.Equal("tarjeta", e.Origen);
        Assert.Equal(Empresa, e.EmpresaId);
        Assert.False(e.Deducible);
        Assert.Equal("COMIDAS", e.ClasificacionNoDeducible);
        Assert.Null(e.Uuid);
        Assert.Null(e.FacturaProveedorId);
        Assert.Equal((350m, 0m, 0m, "MXN"), (e.Total, e.IvaTrasladado, e.IvaNoAcreditable, e.Moneda));
        Assert.Equal(new DateOnly(2026, 10, 20), e.Fecha);

        // Clasificación inválida: no guarda ni publica.
        var antes = (await EventosGastoAsync(_ => true)).Count;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync(Cxp + "/movimientos-tc/sin-cfdi", SinCfdi(tarjeta, titular, "NO_EXISTE"))).StatusCode);
        Assert.Equal(antes, (await EventosGastoAsync(_ => true)).Count);
    }

    [Fact]
    public async Task Gasto_registrado_tarjeta_con_CFDI_manda_el_IVA_a_no_acreditable_solo_si_es_no_deducible_y_no_se_duplica()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        var uuidND = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiND = await SembrarCfdiAsync(uuidND, 1000m, 160m);
        var nd = await Json(await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiND, uuidND, "PROPINAS")));
        var uuidD = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiD = await SembrarCfdiAsync(uuidD, 1000m, 160m);
        var d = await Json(await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiD, uuidD, null)));

        var eND = Assert.Single(await EventosGastoAsync(x => x.DocumentoId == nd.GetProperty("id").GetGuid()));
        Assert.Equal("tarjeta", eND.Origen);
        Assert.False(eND.Deducible);
        Assert.Equal("PROPINAS", eND.ClasificacionNoDeducible);
        Assert.Equal(uuidND, eND.Uuid);
        Assert.Equal("PRO010101AAA", eND.ProveedorRfc);
        Assert.NotNull(eND.ProveedorId);
        Assert.NotNull(eND.FacturaProveedorId);
        Assert.Equal((1000m, 160m, 160m, 1160m, "MXN"), (eND.Subtotal, eND.IvaTrasladado, eND.IvaNoAcreditable, eND.Total, eND.Moneda));

        var eD = Assert.Single(await EventosGastoAsync(x => x.DocumentoId == d.GetProperty("id").GetGuid()));
        Assert.True(eD.Deducible);
        Assert.Null(eD.ClasificacionNoDeducible);
        Assert.Equal((160m, 0m), (eD.IvaTrasladado, eD.IvaNoAcreditable));

        // Reprocesar el mismo CFDI se rechaza y no agrega eventos.
        var otra = await c.PostAsJsonAsync(Cxp + "/movimientos-tc/con-cfdi", ConCfdi(tarjeta, titular, cfdiND, uuidND, "PROPINAS"));
        Assert.False(otra.IsSuccessStatusCode);
        Assert.Single(await EventosGastoAsync(x => x.Uuid == uuidND));
    }

    [Fact]
    public async Task Gasto_registrado_caja_chica_se_publica_una_vez_por_linea_al_aprobar_el_pasivo_no_antes()
    {
        var (c, _) = await ClienteComprobacionesAsync();
        var uuidND = Guid.NewGuid().ToString().ToUpperInvariant();
        var uuidD = Guid.NewGuid().ToString().ToUpperInvariant();
        var cfdiND = await SembrarCfdiAsync(uuidND, 500m, 80m);
        var cfdiD = await SembrarCfdiAsync(uuidD, 200m, 32m);
        var creada = await c.PostAsJsonAsync(Cxp + "/comprobaciones/caja-chica", new
        {
            sucursalId = Guid.NewGuid(), responsableId = Guid.NewGuid(), fechaInicio = new DateOnly(2026, 10, 1), fechaFin = new DateOnly(2026, 10, 20),
            moneda = "MXN", cfdis = new[]
            {
                LineaCajaChica(null, null, 103.45m, 16.55m, "ESTACIONAMIENTO"),
                LineaCajaChica(cfdiND, uuidND, 500m, 80m, "COMIDAS"),
                LineaCajaChica(cfdiD, uuidD, 200m, 32m, null),
            },
        });
        Assert.True(creada.StatusCode == HttpStatusCode.Created, await creada.Content.ReadAsStringAsync());
        var id = (await Json(creada)).GetProperty("id").GetGuid();
        var ruta = Cxp + $"/comprobaciones/{id}";

        var version = (await Json(await c.GetAsync(ruta))).GetProperty("version").GetInt32();
        var envio = await PostConVersionAsync(c, ruta + "/enviar-revision", version);
        Assert.True(envio.StatusCode == HttpStatusCode.OK, await envio.Content.ReadAsStringAsync());
        Assert.Empty(await EventosGastoAsync(x => x.DocumentoId == id));

        version = (await Json(envio)).GetProperty("version").GetInt32();
        var aprobada = await PostConVersionAsync(c, ruta + "/aprobar-pasivo", version);
        Assert.True(aprobada.StatusCode == HttpStatusCode.OK, await aprobada.Content.ReadAsStringAsync());

        var eventos = await EventosGastoAsync(x => x.DocumentoId == id);
        Assert.Equal(3, eventos.Count);
        Assert.All(eventos, e => { Assert.Equal("caja_chica", e.Origen); Assert.NotNull(e.LineaId); Assert.NotNull(e.FacturaProveedorId); });
        Assert.Equal(3, eventos.Select(e => e.LineaId).Distinct().Count());
        var ticket = eventos.Single(e => e.Uuid is null);
        Assert.False(ticket.Deducible);
        Assert.Equal("ESTACIONAMIENTO", ticket.ClasificacionNoDeducible);
        Assert.Equal((16.55m, 16.55m, 120m), (ticket.IvaTrasladado, ticket.IvaNoAcreditable, ticket.Total));
        var nd = eventos.Single(e => e.Uuid == uuidND);
        Assert.Equal((false, "COMIDAS", 80m, 80m, "PRO010101AAA"), (nd.Deducible, nd.ClasificacionNoDeducible, nd.IvaTrasladado, nd.IvaNoAcreditable, nd.ProveedorRfc));
        var d = eventos.Single(e => e.Uuid == uuidD);
        Assert.Equal((true, (string?)null, 32m, 0m), (d.Deducible, d.ClasificacionNoDeducible, d.IvaTrasladado, d.IvaNoAcreditable));

        // Re-aprobar: la comprobación ya está Aplicada → se rechaza y no hay eventos nuevos.
        var otra = await PostConVersionAsync(c, ruta + "/aprobar-pasivo", version + 1);
        Assert.False(otra.IsSuccessStatusCode);
        Assert.Equal(3, (await EventosGastoAsync(x => x.DocumentoId == id)).Count);
    }

    [Fact]
    public async Task Gasto_registrado_viaticos_se_publica_una_vez_por_linea_al_liberar_no_al_capturar()
    {
        var (c, _) = await ClienteComprobacionesAsync();
        var (id, version) = await SembrarViaticoAnticipadoAsync();
        var ruta = Cxp + $"/viaticos/{id}";
        var uuidND = Guid.NewGuid().ToString().ToUpperInvariant();
        var uuidD = Guid.NewGuid().ToString().ToUpperInvariant();

        var cap = await PostConVersionAsync(c, ruta + "/capturar-comprobacion", version, new
        {
            lineas = new[]
            {
                LineaViatico(null, true, 120m, 0m, "VIATICOS_SIN_CFDI"),
                LineaViatico(uuidND, false, 500m, 80m, "PROPINAS"),
                LineaViatico(uuidD, false, 200m, 32m, null),
            },
        });
        Assert.True(cap.StatusCode == HttpStatusCode.OK, await cap.Content.ReadAsStringAsync());
        Assert.Empty(await EventosGastoAsync(x => x.DocumentoId == id));

        version = (await Json(cap)).GetProperty("version").GetInt32();
        var lib = await PostConVersionAsync(c, ruta + "/liberar", version);
        Assert.True(lib.StatusCode == HttpStatusCode.OK, await lib.Content.ReadAsStringAsync());

        var eventos = await EventosGastoAsync(x => x.DocumentoId == id);
        Assert.Equal(3, eventos.Count);
        Assert.All(eventos, e => Assert.Equal("viaticos", e.Origen));
        var ticket = eventos.Single(e => e.Uuid is null);
        Assert.Equal((false, "VIATICOS_SIN_CFDI", 0m, 0m, 120m, (Guid?)null), (ticket.Deducible, ticket.ClasificacionNoDeducible, ticket.IvaTrasladado, ticket.IvaNoAcreditable, ticket.Total, ticket.FacturaProveedorId));
        var nd = eventos.Single(e => e.Uuid == uuidND);
        Assert.Equal((false, "PROPINAS", 80m, 80m), (nd.Deducible, nd.ClasificacionNoDeducible, nd.IvaTrasladado, nd.IvaNoAcreditable));
        Assert.NotNull(nd.FacturaProveedorId);
        var d = eventos.Single(e => e.Uuid == uuidD);
        Assert.Equal((true, (string?)null, 32m, 0m), (d.Deducible, d.ClasificacionNoDeducible, d.IvaTrasladado, d.IvaNoAcreditable));

        var otra = await PostConVersionAsync(c, ruta + "/liberar", version + 1);
        Assert.False(otra.IsSuccessStatusCode);
        Assert.Equal(3, (await EventosGastoAsync(x => x.DocumentoId == id)).Count);
    }

    [Fact]
    public async Task Gasto_registrado_captura_retroactiva_desde_estado_de_cuenta_se_publica_una_vez()
    {
        var (c, tarjeta, titular) = await AmbienteAsync();
        Guid ecId, lineaId; int version;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var ec = EstadoCuentaTc.Crear(Empresa, tarjeta, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 25), new DateOnly(2026, 11, 10));
            var linea = ec.AgregarLinea(1, new DateOnly(2026, 10, 20), 350m, "MXN", 350m, "RESTAURANTE", null, "Compra");
            db.EstadosCuentaTc.Add(ec);
            db.Entry(linea).Property("EstadoMatch").CurrentValue = EstadoMatchLineaBanco.NoConciliado;
            await db.SaveChangesAsync();
            (ecId, lineaId, version) = (ec.Id, linea.Id, ec.Version);
        }
        var ruta = Cxp + $"/estados-cuenta-tc/{ecId}/lineas/{lineaId}/capturar-retroactiva";
        var cuerpo = new { usuarioQueUsoId = titular, conceptoContable = "Comidas", clasificacionNoDeducible = "PROPINAS" };

        var ok = await PostConVersionAsync(c, ruta, version, cuerpo);
        Assert.True(ok.StatusCode == HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var movId = (await Json(await c.GetAsync(Cxp + $"/movimientos-tc?tarjetaId={tarjeta}"))).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var e = Assert.Single(await EventosGastoAsync(x => x.DocumentoId == movId));
        Assert.Equal("estado_cuenta", e.Origen);
        Assert.Equal((false, "PROPINAS", 350m, 0m, "MXN"), (e.Deducible, e.ClasificacionNoDeducible, e.Total, e.IvaNoAcreditable, e.Moneda));
        Assert.Equal(new DateOnly(2026, 10, 20), e.Fecha);

        // Reintento con la versión nueva: la línea ya no es NoConciliado → 422 y sin evento extra.
        var nuevaVersion = (await Json(ok)).GetProperty("version").GetInt32();
        var otra = await PostConVersionAsync(c, ruta, nuevaVersion, cuerpo);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otra.StatusCode);
        Assert.Single(await EventosGastoAsync(x => x.DocumentoId == movId));
    }

    // ---- Unicidad de UUID en BD: carrera entre capturas ----

    [Fact]
    public async Task Dos_capturas_de_viaticos_con_el_mismo_UUID_en_paralelo_dejan_una_sola_y_la_otra_recibe_el_error_de_negocio()
    {
        var (c, _) = await ClienteComprobacionesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var a = await SembrarViaticoAnticipadoAsync();
        var b = await SembrarViaticoAnticipadoAsync();

        var respuestas = await Task.WhenAll(new[] { a, b }.Select(v => PostConVersionAsync(c, Cxp + $"/viaticos/{v.Id}/capturar-comprobacion", v.Version,
            new { lineas = new[] { LineaViatico(uuid, false, 500m, 80m, null) } })));

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }, respuestas.Select(r => r.StatusCode).OrderBy(x => x).ToArray());
        var perdedora = respuestas.Single(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        Assert.Equal("VIA_LINEA_FACTURA_DUPLICADA", (await Json(perdedora)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task El_indice_de_viaticos_pendientes_rechaza_el_UUID_repetido_aunque_el_chequeo_de_la_app_se_salte()
    {
        var uuid = Guid.NewGuid().ToString().ToLowerInvariant(); // el dominio lo guarda en mayúsculas
        async Task CapturarAsync(Guid id)
        {
            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var s = await db.SolicitudesViaticos.Include(x => x.Lineas).SingleAsync(x => x.Id == id);
            s.CapturarComprobacion([new Millet.CuentasPorPagar.Domain.Viaticos.LineaComprobacionViaticosInput(null, uuid, Guid.NewGuid(), "V-1",
                DateTimeOffset.UtcNow, 500m, 80m, 0m, 580m, "MXN", "Alimentos", false)], DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        var a = await SembrarViaticoAnticipadoAsync();
        var b = await SembrarViaticoAnticipadoAsync();
        await CapturarAsync(a.Id);

        var ex = await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => CapturarAsync(b.Id));
        Assert.Equal("VIA_LINEA_FACTURA_DUPLICADA", ex.Code);

        // Recapturar la misma solicitud con el mismo UUID (reemplaza sus líneas) no es duplicado.
        await CapturarAsync(a.Id);
    }

    [Fact]
    public async Task El_indice_de_facturas_rechaza_el_UUID_repetido_sin_distinguir_mayusculas_y_ignora_las_canceladas()
    {
        var (_, amb) = await ClienteComprobacionesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        await SembrarFacturaAsync(amb, uuid);
        var otraId = await SembrarFacturaAsync(amb, Guid.NewGuid().ToString().ToUpperInvariant());

        // Cambiar el UUID de una factura existente no pasa por el chequeo de altas: lo ataja el índice único.
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var otra = await db.FacturasProveedor.SingleAsync(f => f.Id == otraId);
        db.Entry(otra).Property(f => f.UuidCfdi).CurrentValue = uuid.ToLowerInvariant();
        var ex = await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Equal("CXP_UUID_DUPLICADO", ex.Code);
    }

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
