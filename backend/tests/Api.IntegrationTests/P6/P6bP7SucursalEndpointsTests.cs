using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Api.Endpoints.Almacen.Vales;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Trazabilidad;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.P6;

public sealed partial class P6SucursalEndpointsTests
{
    public static TheoryData<string, string> RutasP7 => new()
    {
        { "recepcion-detalle", PermisosCanonicos.AlmacenEntradasLeer },
        { "salida-detalle", PermisosCanonicos.AlmacenSalidasLeerTodas },
        { "vale-detalle", PermisosCanonicos.AlmacenSalidasLeerTodas },
        { "recepcion-factura", PermisosCanonicos.AlmacenEntradasRegistrar },
        { "recepcion-packing", PermisosCanonicos.AlmacenEntradasRegistrar },
        { "salida-rq", PermisosCanonicos.AlmacenSalidasRegistrar },
        { "salida-vale", PermisosCanonicos.AlmacenSalidasPorVale },
        { "regularizar", PermisosCanonicos.AlmacenSalidasPorVale },
        { "reorden-detalle", PermisosCanonicos.AlmacenReordenRead },
        { "reorden-crear", PermisosCanonicos.AlmacenReordenAdministrar },
        { "reorden-crear-sucursal", PermisosCanonicos.AlmacenReordenAdministrar },
        { "reorden-editar", PermisosCanonicos.AlmacenReordenAdministrar },
        { "reorden-desactivar", PermisosCanonicos.AlmacenReordenAdministrar },
    };

    [Theory]
    [MemberData(nameof(RutasP7))]
    public async Task P7_por_ruta_ajeno_403_sin_mutar_propio_y_corporativo_exitosos(string ruta, string permiso)
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var denegada = await EnviarP7Async(datos.Operativo, ruta, datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, denegada.StatusCode);
        Assert.Contains("SUCURSAL_NO_ASOCIADA", await denegada.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            Assert.Equal(2, await db.Movimientos.CountAsync(m => m.Lineas.Any(l => l.UbicacionId == datos.Ajenos["bin_p7"])));
            Assert.True((await db.Movimientos.SingleAsync(m => m.Id == datos.Ajenos["vale_p7"])).PendienteRegularizacion);
            Assert.Equal(10, (await db.ConfiguracionesReorden.SingleAsync(c => c.Id == datos.Ajenos["config_p7"])).Maximo);
        }
        using var propia = await EnviarP7Async(datos.Operativo, ruta, datos.Propios);
        using var corporativa = await EnviarP7Async(datos.Corporativo, ruta, datos.Ajenos);
        var esperado = ruta.EndsWith("detalle", StringComparison.Ordinal) || ruta == "reorden-editar" || ruta == "reorden-desactivar"
            ? HttpStatusCode.OK : ruta == "regularizar" ? HttpStatusCode.NoContent : HttpStatusCode.Created;
        Assert.True(propia.StatusCode == esperado, $"Propia {ruta} con {permiso}: {(int)propia.StatusCode} {await propia.Content.ReadAsStringAsync()}");
        Assert.True(corporativa.StatusCode == esperado, $"Corporativa {ruta}: {(int)corporativa.StatusCode} {await corporativa.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [MemberData(nameof(RutasP7))]
    public async Task P7_permiso_de_operacion_se_exige_antes_de_sucursal(string ruta, string permiso)
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var permisoId = await db.Permisos.Where(p => p.Codigo == permiso).Select(p => p.Id).SingleAsync();
        db.RolPermisos.RemoveRange(await db.RolPermisos.Where(p => p.RolId == datos.RolId && p.PermisoId == permisoId).ToListAsync());
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);
        using var response = await EnviarP7Async(datos.Operativo, ruta, datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("SUCURSAL_NO_ASOCIADA", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Vale_archivo_propio_y_corporativo_conserva_contenido_y_descarga(bool descargar)
    {
        await using var datos = await PrepararP7ScopeAsync();
        foreach (var (cliente, mapa) in new[] { (datos.Operativo, datos.Propios), (datos.Corporativo, datos.Ajenos) })
        {
            using var respuesta = await cliente.GetAsync($"/api/v1/almacen/salidas/{mapa["vale_p7"]}/vale?download={descargar}");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
            Assert.Equal("%PDF-1.4 vale ficticio P6", await respuesta.Content.ReadAsStringAsync());
            Assert.Equal(descargar ? "attachment" : null, respuesta.Content.Headers.ContentDisposition?.DispositionType);
        }
    }

    [Theory]
    [InlineData("recepciones", "recepcion_p7")]
    [InlineData("salidas", "vale_p7")]
    [InlineData("reorden", "config_p7")]
    public async Task P7_bandejas_y_avisos_no_muestran_ajenos(string recurso, string llave)
    {
        await using var datos = await PrepararP7ScopeAsync();
        var path = $"/api/v1/almacen/{recurso}/?limit=500" + (recurso == "salidas" ? "&soloVales=true&soloVencidos=true" : "");
        foreach (var client in new[] { datos.Operativo, datos.Corporativo })
        {
            using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var texto = await response.Content.ReadAsStringAsync(); Assert.Contains(datos.Propios[llave].ToString(), texto);
            if (client == datos.Operativo) Assert.DoesNotContain(datos.Ajenos[llave].ToString(), texto);
            else Assert.Contains(datos.Ajenos[llave].ToString(), texto);
        }
    }

    [Theory]
    [InlineData(TipoDocumentoTrazabilidad.Requisicion, "rq")]
    [InlineData(TipoDocumentoTrazabilidad.OrdenCompra, "oc")]
    [InlineData(TipoDocumentoTrazabilidad.Recepcion, "recepcion_p7")]
    [InlineData(TipoDocumentoTrazabilidad.FacturaProveedor, "factura")]
    [InlineData(TipoDocumentoTrazabilidad.PagoProveedor, "pago")]
    public async Task P7_arbol_valida_todas_las_entradas_y_cada_nodo_relacionado(TipoDocumentoTrazabilidad tipo, string llave)
    {
        await using var datos = await PrepararP7ScopeAsync();
        string RutaArbol(Guid id) => $"/api/v1/compras/trazabilidad/arbol-documentos?desde={tipo}&id={id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.GetAsync(RutaArbol(datos.Ajenos[llave]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await datos.Operativo.GetAsync(RutaArbol(datos.Propios[llave]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await datos.Corporativo.GetAsync(RutaArbol(datos.Ajenos[llave]))).StatusCode);
        // El origen propio ahora contiene una recepción con bin ajeno. La raíz no basta.
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var rec = await db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Id == datos.Propios["recepcion_p7"]);
        db.Entry(rec.Lineas.Single()).Property(l => l.UbicacionId).CurrentValue = datos.Ajenos["bin_p7"];
        await db.SaveChangesAsync();
        using var bloqueado = await datos.Operativo.GetAsync(RutaArbol(datos.Propios[llave]));
        Assert.Equal(HttpStatusCode.Forbidden, bloqueado.StatusCode);
        Assert.DoesNotContain(datos.Ajenos["recepcion_p7"].ToString(), await bloqueado.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await datos.Corporativo.GetAsync(RutaArbol(datos.Propios[llave]))).StatusCode);
    }

    [Theory]
    [InlineData("recepcion-packing")]
    [InlineData("recepcion-factura")]
    [InlineData("salida-rq")]
    public async Task P7_alta_con_documento_propio_y_bin_ajeno_es_403(string ruta)
    {
        await using var datos = await PrepararP7ScopeAsync();
        var mezcla = new Dictionary<string, Guid>(datos.Propios) { ["bin_p7"] = datos.Ajenos["bin_p7"] };
        Assert.Equal(HttpStatusCode.Forbidden, (await EnviarP7Async(datos.Operativo, ruta, mezcla)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task P7_recepcion_sin_lineas_exige_corporativo_en_detalle_y_arbol(bool arbol)
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var rec = datos.Propios["recepcion_p7"];
        await db.Set<LineaMovimiento>().Where(l => l.MovimientoId == rec).ExecuteDeleteAsync();
        var path = arbol ? $"/api/v1/compras/trazabilidad/arbol-documentos?desde=Recepcion&id={rec}"
            : $"/api/v1/almacen/recepciones/{rec}";
        using var response = await datos.Operativo.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("SUCURSAL_NO_DETERMINADA", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await datos.Corporativo.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task P7_recepcion_con_OC_propia_y_CFDI_ajeno_es_403()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;
        using var response = await datos.Operativo.PostAsJsonAsync("/api/v1/almacen/recepciones/",
            new RegistrarRecepcionConFacturaCommand(d["oc"], new(2026, 10, 9), datos.Ajenos["cfdi"], null, "DEMO P6b",
                [new(d["articulo_p7"], d["linea_oc_p7"], 1, null, null, d["bin_p7"])]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("SUCURSAL_NO_ASOCIADA", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P7_obra_de_RQ_conserva_guarda_P6_y_edita_propias_y_corporativas()
    {
        await using var datos = await PrepararAsync();
        Task<HttpResponseMessage> Editar(HttpClient client, Guid id) => client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{id}", new { Obra = "Obra DEMO P6b/P7" });
        Assert.Equal(HttpStatusCode.Forbidden, (await Editar(datos.Operativo, datos.Ajenos["rq"])).StatusCode);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        Assert.Null((await db.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == datos.Ajenos["rq"])).Obra);
        Assert.Equal(HttpStatusCode.NoContent, (await Editar(datos.Operativo, datos.Propios["rq"])).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Editar(datos.Corporativo, datos.Ajenos["rq"])).StatusCode);
        foreach (var id in new[] { datos.Propios["rq"], datos.Ajenos["rq"] })
            Assert.Equal("Obra DEMO P6b/P7", (await db.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == id)).Obra);
    }

    private static Task<HttpResponseMessage> EnviarP7Async(HttpClient client, string ruta, Dictionary<string, Guid> d)
    {
        RegistrarRecepcionLineaInput[] entradas = [new(d["articulo_p7"], d["linea_oc_p7"], 1, null, null, d["bin_p7"])];
        RegistrarSalidaLineaInput[] salidas = [new(d["articulo_p7"], d["linea_rq_p7"], 1, null, null, null, null, d["bin_p7"])];
        return ruta switch
        {
            "recepcion-detalle" => client.GetAsync($"/api/v1/almacen/recepciones/{d["recepcion_p7"]}"),
            "salida-detalle" => client.GetAsync($"/api/v1/almacen/salidas/{d["vale_p7"]}"),
            "vale-detalle" => client.GetAsync($"/api/v1/almacen/salidas/{d["vale_p7"]}/vale"),
            "recepcion-factura" => client.PostAsJsonAsync("/api/v1/almacen/recepciones/", new RegistrarRecepcionConFacturaCommand(d["oc"], new(2026, 10, 9), null, Guid.NewGuid().ToString(), "DEMO P6b", entradas)),
            "recepcion-packing" => client.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list", new RegistrarRecepcionConPackingListCommand(d["oc"], new(2026, 10, 9), "DEMO-P6b.pdf", null, entradas)),
            "salida-rq" => client.PostAsJsonAsync("/api/v1/almacen/salidas/", new RegistrarSalidaConRequisicionCommand(d["rq"], new(2026, 10, 9), null, "DEMO P6b", salidas)),
            "salida-vale" => client.PostAsJsonAsync("/api/v1/almacen/salidas/vale", new RegistrarSalidaPorValeCommand(new(2026, 10, 9), "DEMO-P6b.pdf", null, "DEMO P6b", salidas)),
            "regularizar" => client.PostAsJsonAsync($"/api/v1/almacen/salidas/{d["vale_p7"]}/regularizar", new RegularizarValeRequest(d["rq"])),
            "reorden-detalle" => client.GetAsync($"/api/v1/almacen/reorden/{d["config_p7"]}"),
            // Otro artículo exclusivo evita duplicar la config existente del fixture.
            "reorden-crear" => client.PostAsJsonAsync("/api/v1/almacen/reorden/", new CrearConfiguracionReordenCommand(d["articulo_crear_p7"], NivelReorden.Almacen, d["almacen_p7"], 1, 10, 2, false, ObjetivoReposicion.Maximo)),
            "reorden-crear-sucursal" => client.PostAsJsonAsync("/api/v1/almacen/reorden/", new CrearConfiguracionReordenCommand(d["articulo_crear_p7"], NivelReorden.Sucursal, d["sucursal_p7"], 1, 10, 2, false, ObjetivoReposicion.Maximo)),
            "reorden-editar" => client.PatchAsJsonAsync($"/api/v1/almacen/reorden/{d["config_p7"]}", new EditarConfiguracionReordenCommand(d["config_p7"], 1, 12, 2, false, ObjetivoReposicion.Maximo)),
            "reorden-desactivar" => client.PostAsync($"/api/v1/almacen/reorden/{d["config_p7"]}/desactivar", null),
            _ => throw new ArgumentOutOfRangeException(nameof(ruta)),
        };
    }

    private async Task<Datos> PrepararP7ScopeAsync()
    {
        var datos = await PrepararAsync();
        try
        {
            // Identidades ficticias de firma, como en las pruebas P2: el capturista,
            // N1 y N2 deben ser distintos. No dependen del seed opcional de la demo.
            var jefeComprasId = Guid.CreateVersion7();
            var direccionId = Guid.CreateVersion7();
            foreach (var (d, sucursal) in new[] { (datos.Propios, Propia), (datos.Ajenos, Ajena) })
            {
                using var scope = factory.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var catalogos = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
                var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                d.Add("sucursal_p7", sucursal);
                var pza = await catalogos.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
                var art = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"P6b{Guid.NewGuid():N}"[..16], "DEMO sucursal P7", "PZA", unidadMedidaId: pza.Id);
                var artCrear = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"P6b{Guid.NewGuid():N}"[..16], "DEMO reorden P7", "PZA", unidadMedidaId: pza.Id);
                d.Add("articulo_p7", art.Id); d.Add("articulo_crear_p7", artCrear.Id);
                catalogos.AddRange(art, artCrear); await catalogos.SaveChangesAsync();
                var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"P6b{Guid.NewGuid():N}"[..16], "DEMO P6b", sucursal);
                var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "P6b", "DEMO P6b", TipoSubAlmacen.Insumos);
                var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "P6b", "DEMO P6b");
                var config = new ConfiguracionReorden(Guid.NewGuid(), art.Id, NivelReorden.Almacen, alm.Id, 1, 10, 2, false, ObjetivoReposicion.Maximo);
                d.Add("almacen_p7", alm.Id); d.Add("sub_p7", sub.Id); d.Add("bin_p7", bin.Id); d.Add("config_p7", config.Id);
                almacen.AddRange(alm, sub, bin, config, new SaldoInventario(bin.Id, sub.Id, art.Id, 100, 10),
                    new AsignacionArticuloUbicacion(Guid.NewGuid(), bin.Id, art.Id), new AsignacionArticuloUbicacion(Guid.NewGuid(), bin.Id, artCrear.Id));
                await almacen.SaveChangesAsync();
                var oc = await compras.OrdenesCompra.SingleAsync(o => o.Id == d["oc"]);
                var rq = await compras.Requisiciones.SingleAsync(r => r.Id == d["rq"]);
                var lineaRq = Guid.NewGuid(); var lineaOc = Guid.NewGuid();
                rq.AgregarLinea(lineaRq, art.Id, 10, "PZA", Money.Mxn(10));
                rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
                rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, datos.UsuarioId, DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
                rq.RegistrarCubrimiento([new CubrimientoLinea(lineaRq, 10, 0)], DateTimeOffset.UtcNow);
                // Vincula el árbol RQ → OC sin ejecutar ni alterar la bifurcación P7.
                oc.AgregarLineaDesdeRequisicion(lineaOc, art.Id, 10, "PZA", 10, rq.DepartamentoId, rq.Id, lineaRq);
                oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
                oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, jefeComprasId, DateTimeOffset.UtcNow);
                oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, direccionId, DateTimeOffset.UtcNow);
                d.Add("linea_oc_p7", lineaOc); d.Add("linea_rq_p7", lineaRq);
                await compras.SaveChangesAsync();
                var rec = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, Empresa, new(2026, 10, 9));
                rec.VincularRecepcionVarianteB(oc.Id, lineaOc, "DEMO-P6b.pdf"); rec.ConciliarConFacturaProveedor(d["factura"]);
                rec.AgregarLinea(new(Guid.NewGuid(), rec.Id, 1, art.Id, 1, "PZA", 10, ubicacionId: bin.Id, lineaOcId: lineaOc));
                var vale = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.SalidaPorVale, Empresa, new(2026, 10, 9));
                vale.VincularSalida(null, "DEMO-P6b.pdf", null); vale.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(-1));
                vale.AgregarLinea(new(Guid.NewGuid(), vale.Id, 1, art.Id, 1, "PZA", 10, ubicacionId: bin.Id));
                d.Add("recepcion_p7", rec.Id); d.Add("vale_p7", vale.Id);
                almacen.AddRange(rec, vale); await almacen.SaveChangesAsync();
            }
            return datos;
        }
        catch { await datos.DisposeAsync(); throw; }
    }
}
