using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Contabilidad.Application.ConciliacionAuxiliares;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// F1-CON-09 contra el API y Postgres reales. Las pólizas de factura de proveedor se registran con el puerto del motor
/// (IPolizaContablePort) en una cuenta de control de proveedores propia; el auxiliar de CxP lo da un doble del puerto (las facturas
/// no existen en CxP). El reporte se filtra por la sucursal del escenario, así que no lo afectan los datos de otras pruebas.
/// CA10.11 (cuadra) y CON-09-a (factura sin póliza con su causa).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ConciliacionAuxiliaresHttpTests(WebApplicationFactory<Program> factory)
{
    private const string Reporte = Base + "/reportes/conciliacion-auxiliares";

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed class ProveedoresDoble : IAuxiliarProveedoresReadPort
    {
        public ConcurrentBag<DocumentoProveedorAlCorte> Documentos { get; } = [];

        public Task<AuxiliarProveedoresAlCorte> LeerAsync(DateOnly corte, Guid? sucursalId, CancellationToken ct) =>
            Task.FromResult(new AuxiliarProveedoresAlCorte(
                [.. Documentos.Where(d => sucursalId is null || d.SucursalId == sucursalId)], 0));
    }

    private sealed class CarteraVacia : ISaldoCarteraAlCorteReadPort
    {
        public Task<IReadOnlyList<SaldoCarteraMoneda>> LeerAsync(DateOnly corte, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SaldoCarteraMoneda>>([]);
    }

    private sealed class BancosVacios : ISaldosBancariosAlCorteReadPort
    {
        public Task<IReadOnlyList<SaldoBancoAlCorte>> LeerAsync(DateOnly corte, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SaldoBancoAlCorte>>([]);
    }

    private sealed record Escenario(string Suf, WebApplicationFactory<Program> F, HttpClient Admin, Guid Sucursal, Guid Proveedores,
        Guid Gasto, Guid Tipo, string Clave, int Anio, ProveedoresDoble Cxp);

    private async Task<Escenario> CrearEscenarioAsync()
    {
        var suf = Sufijo();
        var anio = Random.Shared.Next(2100, 2990);
        var cxp = new ProveedoresDoble();
        var f = factory.ConEmpresa(EmpresaBootstrapId).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IAuxiliarProveedoresReadPort>();
            s.AddSingleton<IAuxiliarProveedoresReadPort>(cxp);
            s.RemoveAll<ISaldoCarteraAlCorteReadPort>();
            s.AddSingleton<ISaldoCarteraAlCorteReadPort, CarteraVacia>();
            s.RemoveAll<ISaldosBancariosAlCorteReadPort>();
            s.AddSingleton<ISaldosBancariosAlCorteReadPort, BancosVacios>();
        }));
        var admin = await LoginAsync(f);
        await AsegurarPeriodosAbiertosAsync(admin, new DateOnly(anio, 1, 15));
        var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"CA{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursal.EnsureSuccessStatusCode();
        var gastos = await CrearCuenta(admin, Codigo(suf, "6"), "FIX gastos");
        var gasto = await CrearCuenta(admin, Codigo(suf, "6.1"), "FIX gasto", Id(gastos));
        var pasivo = await CrearCuenta(admin, Codigo(suf, "2"), "FIX pasivo", naturaleza: "Acreedora");
        var proveedores = await CrearCuenta(admin, Codigo(suf, "2.1"), "FIX proveedores CON-09", Id(pasivo), naturaleza: "Acreedora", control: "Proveedores");
        var clave = $"FIX-{suf}";
        var tipo = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave, nombre = $"FIX Conciliación {suf}", esPrueba = true, tipoSat = "Diario" });
        Assert.Equal(HttpStatusCode.Created, tipo.StatusCode);
        return new(suf, f, admin, Id(await Json(sucursal)), Id(proveedores), Id(gasto), Id(await Json(tipo)), clave, anio, cxp);
    }

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
        await Limpiar(factory.Services, e.Suf);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", e.Anio);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", e.Anio);
        e.F.Dispose();
    }

    /// <summary>Póliza de la factura de proveedor como la del motor: gasto contra proveedores, con el proveedor como auxiliar.</summary>
    private static async Task FacturaConPolizaAsync(Escenario e, DateOnly fecha, Guid factura, Guid proveedor, decimal total)
    {
        Sobre.Value = EmpresaBootstrapId;
        using var scope = e.F.Services.CreateScope();
        var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
        var r = await puerto.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica(
            e.Clave, fecha, $"FIX factura proveedor {e.Suf}", new DocumentoOrigenPoliza("cuentas_por_pagar", "factura_proveedor", factura, null, null),
            $"fix.con09:{factura}",
            [
                new PartidaRequest(e.Gasto, null, total, 0m, e.Sucursal),
                new PartidaRequest(e.Proveedores, null, 0m, total, e.Sucursal, ProveedorId: proveedor, Origen: OrigenMovimiento.AuxiliarCxP),
            ]), default);
        Assert.Equal(EstadoPoliza.Autorizada, r.Estado);
    }

    private static DocumentoProveedorAlCorte Documento(Escenario e, Guid factura, Guid proveedor, string folio, decimal saldo) =>
        new(factura, proveedor, e.Sucursal, folio, null, "MXN", saldo, saldo);

    private static async Task<JsonElement> GetAsync(Escenario e, string url)
    {
        var r = await e.Admin.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static JsonElement Control(JsonElement resumen, string control) =>
        resumen.GetProperty("controles").EnumerateArray().Single(c => c.GetProperty("control").GetString() == control);

    [Fact]
    public async Task CA10_11_el_saldo_de_proveedores_al_31_ene_cuadra_con_el_auxiliar_de_CxP()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var (proveedor, _) = await MotorTerceros.SembrarAsync(factory);
            var factura = Guid.NewGuid();
            await FacturaConPolizaAsync(e, new DateOnly(e.Anio, 1, 15), factura, proveedor, 1_160m);
            e.Cxp.Documentos.Add(Documento(e, factura, proveedor, "FIX-A-1", 1_160m));
            var corte = new DateOnly(e.Anio, 1, 31);

            var resumen = await GetAsync(e, $"{Reporte}?corte={corte:yyyy-MM-dd}&sucursalId={e.Sucursal}");

            var prov = Control(resumen, "Proveedores");
            Assert.Equal("Cuadra", prov.GetProperty("estado").GetString());
            Assert.Equal(1_160m, prov.GetProperty("saldoContable").GetDecimal());
            Assert.Equal(1_160m, prov.GetProperty("saldoAuxiliar").GetDecimal());
            Assert.Equal(0m, prov.GetProperty("diferencia").GetDecimal());
            Assert.Contains(prov.GetProperty("cuentas").EnumerateArray(), c => c.GetProperty("id").GetGuid() == e.Proveedores);
            // Con filtro de sucursal la cartera y los libros de bancos no se comparan; no se reportan como «Cuadra».
            Assert.NotEqual("Cuadra", Control(resumen, "Clientes").GetProperty("estado").GetString());
            Assert.False(resumen.GetProperty("todoCuadra").GetBoolean());

            // Antes de la factura no hay saldo en ningún lado: también cuadra.
            var antes = await GetAsync(e, $"{Reporte}?corte={new DateOnly(e.Anio, 1, 10):yyyy-MM-dd}&sucursalId={e.Sucursal}");
            Assert.Equal(0m, Control(antes, "Proveedores").GetProperty("saldoContable").GetDecimal());
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task CON_09_a_factura_de_proveedor_sin_poliza_aparece_como_diferencia_con_su_documento()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var (conPoliza, _) = await MotorTerceros.SembrarAsync(factory);
            var (sinPoliza, _) = await MotorTerceros.SembrarAsync(factory);
            var facturaA = Guid.NewGuid();
            var facturaB = Guid.NewGuid();
            await FacturaConPolizaAsync(e, new DateOnly(e.Anio, 1, 15), facturaA, conPoliza, 1_160m);
            e.Cxp.Documentos.Add(Documento(e, facturaA, conPoliza, "FIX-A-1", 1_160m));
            e.Cxp.Documentos.Add(Documento(e, facturaB, sinPoliza, "FIX-B-7", 580m)); // su evento no generó póliza
            var corte = new DateOnly(e.Anio, 1, 31);

            var resumen = await GetAsync(e, $"{Reporte}?corte={corte:yyyy-MM-dd}&sucursalId={e.Sucursal}");
            var prov = Control(resumen, "Proveedores");
            Assert.Equal("Diferencia", prov.GetProperty("estado").GetString());
            Assert.Equal(-580m, prov.GetProperty("diferencia").GetDecimal());

            var detalle = await GetAsync(e, $"{Reporte}/proveedores?corte={corte:yyyy-MM-dd}&sucursalId={e.Sucursal}");
            var filas = detalle.GetProperty("filas").EnumerateArray().ToList();
            var documento = Assert.Single(filas, f => f.GetProperty("tipo").GetString() == "DocumentoSinPoliza");
            Assert.Equal(facturaB, documento.GetProperty("documentoId").GetGuid());
            Assert.Equal("FIX-B-7", documento.GetProperty("folio").GetString());
            Assert.Equal(sinPoliza, documento.GetProperty("terceroId").GetGuid());
            Assert.Equal(580m, documento.GetProperty("saldoAuxiliar").GetDecimal());
            Assert.StartsWith("El evento no generó póliza", documento.GetProperty("causa").GetString());
            var tercero = Assert.Single(filas, f => f.GetProperty("tipo").GetString() == "Proveedor");
            Assert.Equal(sinPoliza, tercero.GetProperty("terceroId").GetGuid());
            Assert.DoesNotContain(filas, f => f.TryGetProperty("documentoId", out var d) && d.ValueKind == JsonValueKind.String && d.GetGuid() == facturaA);
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task Detalle_de_clientes_no_existe_aqui_y_sin_permiso_no_se_ve()
    {
        await using var f = factory.ConEmpresa(EmpresaBootstrapId);
        var admin = await LoginAsync(f);
        var clientes = await admin.GetAsync($"{Reporte}/clientes");
        Assert.Equal(HttpStatusCode.BadRequest, clientes.StatusCode);
        var invalido = await admin.GetAsync($"{Reporte}/inventario");
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);

        var sinPermiso = await ClienteConPermisosAsync(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync(Reporte)).StatusCode);
    }
}
