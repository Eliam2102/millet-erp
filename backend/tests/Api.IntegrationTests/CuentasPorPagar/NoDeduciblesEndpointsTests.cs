using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>86e3kdwng · P8.6 no deducibles e IVA no acreditable.</summary>
public sealed class NoDeduciblesEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Cxp = "/api/v1/cuentas-por-pagar";
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Cliente, Guid TarjetaId, Guid TitularId)> AmbienteAsync()
    {
        var amb = new AdjuntosProveedorAmbiente(factory, s =>
        {
            s.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort>();
            s.AddSingleton<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        var (cliente, usuarioId) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento, PermisosCanonicos.CuentasPorPagarTcLeer,
            PermisosCanonicos.CuentasPorPagarDocumentosGestionarTodasSucursales, PermisosCanonicos.CuentasPorPagarDocumentosLeerTodasSucursales);
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
    public async Task Catalogo_clasificaciones_lista_DEMO_con_permiso_de_captura_y_403_sin_el()
    {
        var amb = new AdjuntosProveedorAmbiente(factory, _ => { });
        var (conPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento);
        var (sinPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarFacturasLeer);

        var ok = await conPermiso.GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var filas = (await Json(ok)).EnumerateArray().ToList();
        Assert.Contains(filas, f => f.GetProperty("clave").GetString() == "COMIDAS" && f.GetProperty("descripcion").GetString() == "DEMO · Comidas");
        Assert.True(filas.Count >= 6);

        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles")).StatusCode);
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
        var amb = new AdjuntosProveedorAmbiente(factory, s =>
        {
            s.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort>();
            s.AddSingleton<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        var (cliente, _) = await amb.ClienteConPermisosAsync(
            PermisosCanonicos.CuentasPorPagarComprobacionesCapturar, PermisosCanonicos.CuentasPorPagarComprobacionesLeer,
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

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
