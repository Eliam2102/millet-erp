using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Domain.Audit;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;
using Anticipo = Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
using Cargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCargo;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>PostgreSQL desechable. Solo datos FIX-P8; no escribe maestros ni modifica el calendario operativo.</summary>
public sealed class P8EndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly int[] MesesFixture = [1, 2];
    private const string Cxp = "/api/v1/cuentas-por-pagar";

    [Fact]
    public async Task Operaciones_abiertas_persisten_y_cerradas_responden_422_sin_aplicaciones_ni_outbox_nuevo()
    {
        var anio = Random.Shared.Next(2300, 2900);
        var puertos = new PuertosFixture();
        using var app = factory.ConEmpresa(EmpresaBootstrapId).WithWebHostBuilder(b => b.ConfigureTestServices(s => {
            s.RemoveAll<IProveedorReadPort>(); s.AddSingleton<IProveedorReadPort>(puertos);
            s.RemoveAll<IComprasOcReadPort>(); s.AddSingleton<IComprasOcReadPort>(puertos);
            // Fecha de operaciones sin fecha explícita (anticipo/NC/cargo): se consulta el mes FIX aislado,
            // manteniendo el reloj real de JWT y sin cerrar el calendario usado por las otras suites.
            s.RemoveAll<IPeriodoContablePort>();
            s.AddScoped<IPeriodoContablePort>(sp => new PeriodoFixturePort(sp.GetRequiredService<IPeriodoContableConsultaPort>(), new(anio, 1, 9)));
        }));
        var admin = await LoginAsync(app);
        Guid? ejercicioId = null;
        try
        {
            var ejercicio = await Json(await admin.PostAsJsonAsync(Base + "/periodos/ejercicios", new { anio }));
            ejercicioId = ejercicio.GetProperty("id").GetGuid();
            var abierto = await Send(admin, HttpMethod.Post, Base + $"/periodos/ejercicios/{ejercicioId}/abrir", new { numeros = MesesFixture }, Etag(ejercicio));
            Assert.Equal(HttpStatusCode.OK, abierto.StatusCode);
            var e = await Json(abierto);
            var enero = e.GetProperty("periodos").EnumerateArray().Single(p => p.GetProperty("numero").GetInt32() == 1);
            var fecha = new DateTimeOffset(anio, 1, 9, 0, 0, 0, TimeSpan.Zero);
            var cmd = new CapturarFacturaConOcCommand(puertos.OcId, puertos.ProveedorId, puertos.SucursalId, null, null,
                "FIX-P8-HTTP", null, fecha, fecha, new(anio, 2, 10), "MXN", null, 100, 0, 16, 0, 116,
                [new(null, "30102400", "FIX material P8", 1, "H87", null, 100, 100, null, puertos.LineaId, null)], Obra:"FIX-OBRA", ConceptoRetencion:"HONORARIOS_PF");
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(Cxp + "/facturas", cmd)).StatusCode);
            Guid facturaId; int version; Guid anticipoId; int anticipoVersion; Guid ncId; int ncVersion; Guid cargoId; int cargoVersion;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                var f = Factura.CapturarSinOc(EmpresaBootstrapId, null, null, puertos.ProveedorId, puertos.SucursalId, "FIX-P8-OPERACIONES", null,
                    fecha, fecha, new(anio, 2, 10), "MXN", null, 1000, 0, 0, 0, 1000, "FIX prueba P8", fecha);
                var anticipo = Anticipo.Capturar(EmpresaBootstrapId, null, Guid.NewGuid().ToString(), puertos.ProveedorId, "FANT", "FIX-P8", fecha, "MXN", null, 100, null, null, fecha);
                var nc = Nc.Capturar(EmpresaBootstrapId, null, Guid.NewGuid().ToString(), puertos.ProveedorId, "FIX-P8", null, fecha, "MXN", null, 100, 0, 0, 100,
                    TipoNotaCredito.Descuento, TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), f.Id, null, fecha);
                var cargo = Cargo.Crear(EmpresaBootstrapId, FolioInternoNotaCargo.FromAnioSecuencial(2026, Random.Shared.Next(800000, 999999)), puertos.ProveedorId, puertos.SucursalId,
                    "FIX-P8", null, 100, "MXN", null, f.Id, null, null, fecha); cargo.Autorizar(null, fecha);
                db.AddRange(f, anticipo, nc, cargo); await db.SaveChangesAsync();
                facturaId=f.Id; version=f.Version; anticipoId=anticipo.Id; anticipoVersion=anticipo.Version;
                ncId=nc.Id; ncVersion=nc.Version; cargoId=cargo.Id; cargoVersion=cargo.Version;
            }
            // NC en abierto: persistencia + historial, luego reutilizamos la factura con la versión actual.
            using (var req = new HttpRequestMessage(HttpMethod.Post, Cxp + $"/facturas/{facturaId}/aplicar-nc") {
                Content=JsonContent.Create(new { notaCreditoId=ncId, notaCreditoVersionEsperada=ncVersion, monto=10 }) })
            {
                req.Headers.Add("X-Expected-Version", version.ToString());
                Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(req)).StatusCode);
            }
            using (var scope = app.Services.CreateScope()) version = (await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.FindAsync(facturaId))!.Version;
            var cerrar = await Send(admin, HttpMethod.Post, Base + $"/periodos/{enero.GetProperty("id").GetGuid()}/cerrar", new { motivo="FIX cierre aislado para P8" }, Etag(enero));
            Assert.Equal(HttpStatusCode.OK, cerrar.StatusCode);
            var capturar = await admin.PostAsJsonAsync(Cxp + "/facturas", cmd);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, capturar.StatusCode); Assert.Equal("CXP_PERIODO_CERRADO", await Code(capturar));
            var acciones = new (HttpMethod Method, string Path, object Body, int Version)[] {
                (HttpMethod.Post, $"/facturas/{facturaId}/autorizar", new {}, version),
                (HttpMethod.Post, $"/facturas/{facturaId}/cancelar", new { motivo=(int)MotivoCancelacion.ErrorCaptura, texto="FIX prueba P8" }, version),
                (HttpMethod.Patch, $"/facturas/{facturaId}", new { folioProveedor="FIX-P8", fechaVencimiento=$"{anio}-02-10", fechaContabilizacion=fecha }, version),
                (HttpMethod.Post, $"/facturas/{facturaId}/aplicar-anticipo", new { anticipoId, anticipoVersionEsperada=anticipoVersion, monto=10 }, version),
                (HttpMethod.Post, $"/facturas/{facturaId}/aplicar-nc", new { notaCreditoId=ncId, notaCreditoVersionEsperada=ncVersion+1, monto=10 }, version),
                (HttpMethod.Post, $"/notas-cargo/{cargoId}/aplicar", new {}, cargoVersion)
            };
            foreach (var accion in acciones)
            {
                using var req = new HttpRequestMessage(accion.Method, Cxp+accion.Path) { Content=JsonContent.Create(accion.Body) };
                req.Headers.Add("X-Expected-Version", accion.Version.ToString());
                var r = await admin.SendAsync(req);
                var respuesta = await r.Content.ReadAsStringAsync();
                var contexto = $"Acción {accion.Method} {accion.Path}: HTTP {(int)r.StatusCode}. Respuesta: {respuesta}";
                Assert.True(r.StatusCode == HttpStatusCode.UnprocessableEntity,
                    $"Se esperaba 422 CXP_PERIODO_CERRADO. {contexto}");
                Assert.True(await Code(r) == "CXP_PERIODO_CERRADO",
                    $"Se esperaba el código CXP_PERIODO_CERRADO. {contexto}");
            }
            using (var scope = app.Services.CreateScope()) {
                var db=scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                Assert.Single(await db.MovimientosPasivo.Where(m=>m.FacturaProveedorId==facturaId).ToListAsync());
                Assert.Equal(10, (await db.FacturasProveedor.FindAsync(facturaId))!.NcAplicadasTotal);
                Assert.DoesNotContain(await db.OutboxEntries.ToListAsync(), o=>o.Payload.Contains(facturaId.ToString(), StringComparison.Ordinal));
            }
        }
        finally
        {
            using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            await db.NotasCargo.Where(n=>n.ProveedorId==puertos.ProveedorId).ExecuteDeleteAsync();
            await db.NotasCreditoProveedor.Where(n=>n.ProveedorId==puertos.ProveedorId).ExecuteDeleteAsync();
            await db.AnticiposProveedor.Where(n=>n.ProveedorId==puertos.ProveedorId).ExecuteDeleteAsync();
            await db.FacturasProveedor.Where(n=>n.ProveedorId==puertos.ProveedorId).ExecuteDeleteAsync();
            var eventos = await db.OutboxEntries.ToListAsync();
            db.OutboxEntries.RemoveRange(eventos.Where(o=>o.Payload.Contains(puertos.ProveedorId.ToString(), StringComparison.Ordinal))); await db.SaveChangesAsync();
            if (ejercicioId is Guid id) { var contab=scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await contab.Periodos.Where(p=>p.EjercicioId==id).ExecuteDeleteAsync(); await contab.Ejercicios.Where(e=>e.Id==id).ExecuteDeleteAsync(); }
            Sobre.Value=null;
        }
    }

    [Fact]
    public async Task Catalogo_HTTP_se_administra_con_version_y_auditoria_y_limpia_su_registro_FIX()
    {
        using var app=factory.ConEmpresa(EmpresaBootstrapId); var admin=await LoginAsync(app); Guid? id=null;
        try {
            var concepto="FIX-P8-"+Guid.NewGuid().ToString("N")[..8];
            var body=new {concepto,descripcion="FIX regla",impuesto="001",tasa=.1m,fuente="FIX SAT por validar",activa=true,motivo="FIX supuesto para Fiscal"};
            var creado=await admin.PostAsJsonAsync(Cxp+"/catalogos/retenciones",body); Assert.Equal(HttpStatusCode.Created,creado.StatusCode);
            var r=await Json(creado); id=r.GetProperty("id").GetGuid();
            using var req=new HttpRequestMessage(HttpMethod.Put,Cxp+$"/catalogos/retenciones/{id}") {Content=JsonContent.Create(body with { tasa=.125m, motivo="FIX ajuste revisado por Fiscal" })};
            req.Headers.Add("X-Expected-Version",r.GetProperty("version").GetInt32().ToString());
            Assert.Equal(HttpStatusCode.OK,(await admin.SendAsync(req)).StatusCode);
            using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var audit=await db.Set<AuditLogEntry>().Where(a=>a.EntidadId==id).ToListAsync();
            Assert.Contains(audit,a=>a.Cambios.Contains("FIX supuesto para Fiscal",StringComparison.Ordinal));
            Assert.Contains(audit,a=>a.UsuarioId is not null);
            Assert.Contains(audit,a=>a.Cambios.Contains("FIX ajuste revisado por Fiscal",StringComparison.Ordinal));
        } finally { if(id is Guid creadoId) {using var scope=app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().RetencionesConcepto.Where(r=>r.Id==creadoId).ExecuteDeleteAsync();} Sobre.Value=null; }
    }

    [Fact]
    public async Task Reportes_HTTP_reconstruyen_corte_pasado_en_PostgreSQL_con_monedas_obra_y_anticipo_amortizado()
    {
        var puertos = new PuertosFixture();
        using var app = factory.ConEmpresa(EmpresaBootstrapId).WithWebHostBuilder(b => b.ConfigureTestServices(services => {
            services.RemoveAll<IProveedorReadPort>(); services.AddSingleton<IProveedorReadPort>(puertos);
            services.RemoveAll<IPeriodoContablePort>(); services.AddSingleton<IPeriodoContablePort, PeriodoAbiertoFixture>();
        }));
        var admin = await LoginAsync(app);
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                var septiembre = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
                var octubre = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
                Factura Crear(decimal total, string moneda, string obra, DateTimeOffset fecha) {
                    var f = Factura.CapturarSinOc(EmpresaBootstrapId, null, null, puertos.ProveedorId, puertos.SucursalId,
                        "FIX-P8-CORTE", null, fecha, fecha, new(2026, 10, 15), moneda, null, total, 0, 0, 0, total, "FIX corte histórico", fecha);
                    f.AsignarDatosP8(obra, null); return f;
                }
                var f = Crear(1000, "MXN", "FIX-OBRA-A", septiembre); f.Autorizar(null, septiembre);
                f.RegistrarPago(100, septiembre.AddDays(14), "FIX antes de corte");
                f.RegistrarPago(200, octubre, "FIX después de corte");
                f.AplicarNotaCredito(100, new(2026, 10, 9)); f.AplicarAnticipo(100, new(2026, 10, 9));
                f.AplicarNotaCargo(50, Guid.NewGuid(), new(2026, 10, 9));
                var anticipo = Anticipo.Capturar(EmpresaBootstrapId, null, Guid.NewGuid().ToString(), puertos.ProveedorId,
                    "FANT", "FIX-P8", septiembre, "MXN", null, 100, null, null, septiembre);
                anticipo.Amortizar(100, octubre);
                var otra = Crear(200, "MXN", "FIX-OBRA-B", septiembre); otra.AplicarAnticipo(100, new(2026, 10, 9), anticipo.Id);
                db.AddRange(f, otra, Crear(50, "USD", "FIX-OBRA-B", septiembre), Crear(999, "MXN", "FIX-OBRA-A", octubre), anticipo);
                await db.SaveChangesAsync();
            }
            foreach (var nombre in ReportesHistoricos)
            {
                var response = await admin.GetAsync(Cxp + $"/reportes/{nombre}?fechaCorte=2026-09-30&proveedorId={puertos.ProveedorId}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode); var r = await Json(response);
                var filas = r.GetProperty("filas").EnumerateArray().ToArray(); Assert.Equal(2, filas.Length);
                Assert.Equal(1100m, filas.Single(f => f.GetProperty("moneda").GetString() == "MXN").GetProperty("total").GetDecimal());
                Assert.Equal(50m, filas.Single(f => f.GetProperty("moneda").GetString() == "USD").GetProperty("total").GetDecimal());
                Assert.All(filas, f => { Assert.Equal("FIX Proveedor P8", f.GetProperty("proveedor_nombre").GetString()); Assert.Equal("FIX010101ABC", f.GetProperty("rfc").GetString()); });
                Assert.DoesNotContain(r.GetProperty("columnas").EnumerateArray(), c => c.GetProperty("key").GetString() == "proveedor_id");
                Assert.Equal(2, r.GetProperty("totales").GetProperty("por_moneda").GetArrayLength());
            }
            var obras = await Json(await admin.GetAsync(Cxp+$"/reportes/pasivos-obras?obra=FIX-OBRA-A&fechaCorte=2026-09-30&proveedorId={puertos.ProveedorId}"));
            Assert.Single(obras.GetProperty("filas").EnumerateArray());
            Assert.Equal(900m, obras.GetProperty("filas")[0].GetProperty("saldo_pendiente").GetDecimal());
            var anticipos = await Json(await admin.GetAsync(Cxp+$"/reportes/antiguedad-anticipos?fechaCorte=2026-09-30&proveedorId={puertos.ProveedorId}"));
            Assert.Equal(100m, anticipos.GetProperty("filas")[0].GetProperty("saldo_amortizable").GetDecimal());
            var amortizado = await Json(await admin.GetAsync(Cxp+$"/reportes/antiguedad-anticipos?fechaCorte=2026-10-09&proveedorId={puertos.ProveedorId}"));
            Assert.Empty(amortizado.GetProperty("filas").EnumerateArray());
        }
        finally
        {
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            await db.FacturasProveedor.Where(f => f.ProveedorId == puertos.ProveedorId).ExecuteDeleteAsync();
            await db.AnticiposProveedor.Where(a => a.ProveedorId == puertos.ProveedorId).ExecuteDeleteAsync();
            Sobre.Value = null;
        }
    }
    private static readonly string[] ReportesHistoricos = ["antiguedad-saldos", "cartera", "auxiliar-proveedores"];
    private sealed class PeriodoAbiertoFixture : IPeriodoContablePort {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class PeriodoFixturePort(IPeriodoContableConsultaPort port, DateOnly fechaOperacion) : IPeriodoContablePort
    {
        public async Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) =>
            (await port.ConsultarPorFechaAsync(fecha==DateOnly.FromDateTime(DateTime.UtcNow) ? fechaOperacion : fecha,ct)).AdmiteMovimientos;
    }
    private sealed class PuertosFixture : IProveedorReadPort, IComprasOcReadPort
    {
        public Guid ProveedorId {get;}=Guid.NewGuid(); public Guid SucursalId {get;}=Guid.NewGuid();
        public Guid OcId {get;}=Guid.NewGuid(); public Guid LineaId {get;}=Guid.NewGuid();
        public Task<ProveedorDto?> ObtenerAsync(Guid id,CancellationToken ct)=>Task.FromResult<ProveedorDto?>(new(ProveedorId,"FIX010101ABC","FIX Proveedor P8",null,false,true));
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string r,CancellationToken ct)=>ObtenerAsync(ProveedorId,ct);
        public Task<IReadOnlyDictionary<Guid,string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids,CancellationToken ct)=>Task.FromResult<IReadOnlyDictionary<Guid,string>>(ids.ToDictionary(id=>id,_=>"FIX Proveedor P8"));
        Task<OrdenCompraDto?> IComprasOcReadPort.ObtenerAsync(Guid id,CancellationToken ct)=>Task.FromResult<OrdenCompraDto?>(
            new(OcId,"FIX-OC-P8",ProveedorId,EmpresaBootstrapId,SucursalId,116,"Autorizada",[new(LineaId,Guid.NewGuid(),100,100,0,100)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(true && ids.Contains(OcId)
                ? new Dictionary<Guid, string> { [OcId] = "FIX-OC-P8" } : new Dictionary<Guid, string>());
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id,CancellationToken ct)=>Task.FromResult<IReadOnlyList<OrdenCompraDto>>([]);
    }
}
