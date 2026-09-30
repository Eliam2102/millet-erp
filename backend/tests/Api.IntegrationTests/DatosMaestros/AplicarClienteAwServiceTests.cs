using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// ADM-06 Entrega B3: <see cref="AplicarClienteAwService"/> contra PostgreSQL
/// real (BD migrada por tools/validate-integration-isolated.sh, incluida la
/// migración ClienteSincronizacionAw). Datos 100 % sintéticos; cada test usa
/// una referencia única para no depender del orden ni del estado previo.
/// </summary>
public class AplicarClienteAwServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Rfc = "XAXX010101000";
    private readonly WebApplicationFactory<Program> _factory;

    public AplicarClienteAwServiceTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string NuevaRef() => "T" + Guid.NewGuid().ToString("N")[..8];

    private static AplicarClienteAwSnapshot Snap(string referencia, string razon = "CLIENTE DEMO 001") => new(
        referencia, razon, DateTime.UtcNow, "1", "0-borrador",
        Rfc: Rfc, CodigoPostalFiscal: "06600",
        MandantOrigen: 1, NombreComercialOrigen: "DEMO NAME1",
        DomicilioCalle: "CALLE DEMO 1", DomicilioCiudad: "CIUDAD DEMO", DomicilioCp: "06600",
        DomicilioProvincia: "PROV", DomicilioPais: "MX",
        CondicionCodigoOrigen: "Z030", CondicionNumeroOrigen: 30, DiasNominalesOrigen: 30,
        MonedaCodigoOrigen: "MXN", MonedaNormalizada: "MXN", MonedaDefault: "MXN",
        CreditoReferenciaLimite: 1000m, CreditoReferenciaLimite1: 500m, CreditoReferenciaNet: 30d);

    private async Task<T> ConScopeAsync<T>(Func<CompartidoDbContext, AplicarClienteAwService, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<CompartidoDbContext>(),
            scope.ServiceProvider.GetRequiredService<AplicarClienteAwService>());
    }

    private Task<AplicarClienteAwResultado> Aplicar(AplicarClienteAwSnapshot s) =>
        ConScopeAsync((_, svc) => svc.AplicarAsync(s, CancellationToken.None));

    private Task<(List<Cliente> Clientes, List<ClienteSincronizacionAw> Registros)> Leer(string referencia) =>
        ConScopeAsync(async (db, _) => (
            await db.Clientes.AsNoTracking().Where(c => c.ReferenciaExterna == referencia).ToListAsync(),
            await db.Set<ClienteSincronizacionAw>().AsNoTracking().Where(r => r.ReferenciaExterna == referencia).ToListAsync()));

    private Task<int> Editar(string referencia, Action<Cliente> editar) =>
        ConScopeAsync(async (db, _) =>
        {
            var c = await db.Clientes.SingleAsync(x => x.ReferenciaExterna == referencia);
            editar(c);
            await db.SaveChangesAsync();
            return 0;
        });

    [Fact]
    public async Task Alta_crea_cliente_Aw_y_registro_en_una_transaccion()
    {
        var r = NuevaRef();
        var res = await Aplicar(Snap(r));

        Assert.Equal(AplicarClienteAwAccion.Creado, res.Accion);
        Assert.Equal(ResultadoSincronizacionAw.Aplicado, res.Resultado);
        var (cs, regs) = await Leer(r);
        var c = Assert.Single(cs);
        var reg = Assert.Single(regs);
        Assert.Equal(OrigenMaster.Aw, c.Origen);
        Assert.Equal($"AW-{r}", c.Clave);
        Assert.Equal(c.Id, reg.ClienteId);
        Assert.Equal("Z030", reg.CondicionCodigoOrigen);
        Assert.Equal(64, reg.HashOrigen.Length);
    }

    [Fact]
    public async Task Repeticion_identica_devuelve_SinCambios_y_no_duplica()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        var res = await Aplicar(Snap(r));

        Assert.Equal(AplicarClienteAwAccion.SinCambios, res.Accion);
        var (cs, regs) = await Leer(r);
        Assert.Single(cs);
        Assert.Single(regs);
    }

    [Fact]
    public async Task Cambio_comercial_actualiza_registro_y_deja_intactos_RazonSocial_Rfc_y_CP()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        var res = await Aplicar(Snap(r, razon: "CLIENTE DEMO CAMBIADO") with
        {
            NombreComercialOrigen = "DEMO NAME1 NUEVO",
            DomicilioCalle = "CALLE DEMO 99",
            CondicionCodigoOrigen = "Z060", CondicionNumeroOrigen = 60, DiasNominalesOrigen = 60,
        });

        Assert.Equal(AplicarClienteAwAccion.Actualizado, res.Accion);
        var (cs, regs) = await Leer(r);
        Assert.Equal("CLIENTE DEMO 001", cs[0].RazonSocial);
        Assert.Equal(Rfc, cs[0].Rfc);
        Assert.Equal("06600", cs[0].CodigoPostalFiscal);
        Assert.Null(cs[0].RegimenFiscal);
        Assert.Equal("DEMO NAME1 NUEVO", regs[0].NombreComercialOrigen);
        Assert.Equal("CALLE DEMO 99", regs[0].DomicilioOrigenCalle);
        Assert.Equal(60, regs[0].CondicionNumeroOrigen);
    }

    [Fact]
    public async Task Lectura_atrasada_con_datos_distintos_no_pisa_el_registro_vigente()
    {
        var r = NuevaRef();
        var ahora = DateTime.UtcNow;
        await Aplicar(Snap(r) with { LeidoEnUtc = ahora });
        await Aplicar(Snap(r) with { LeidoEnUtc = ahora.AddMinutes(1), DiasNominalesOrigen = 60, CondicionCodigoOrigen = "Z060", CondicionNumeroOrigen = 60 });

        var res = await Aplicar(Snap(r) with { LeidoEnUtc = ahora.AddSeconds(-30), DiasNominalesOrigen = 15, CondicionCodigoOrigen = "Z015", CondicionNumeroOrigen = 15 });

        Assert.Equal(AplicarClienteAwAccion.SinCambios, res.Accion);
        var (cs, regs) = await Leer(r);
        Assert.Single(cs);
        Assert.Equal(60, Assert.Single(regs).DiasNominalesOrigen);
        Assert.Equal("Z060", regs[0].CondicionCodigoOrigen);
    }

    [Fact]
    public async Task Baja_explicita_conserva_identidad_y_lectura_historica_y_la_sincronizacion_no_reactiva()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        var (antes, _) = await Leer(r);
        var id = antes[0].Id;

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(new DesactivarClienteCommand(id));

        // Releer con cambio comercial: no reactiva, no toca fiscales, mantiene la identidad.
        var res = await Aplicar(Snap(r) with
        {
            LeidoEnUtc = DateTime.UtcNow.AddMinutes(1),
            CondicionCodigoOrigen = "Z060", CondicionNumeroOrigen = 60, DiasNominalesOrigen = 60,
        });

        Assert.Equal(AplicarClienteAwAccion.Actualizado, res.Accion);
        var (cs, regs) = await Leer(r);
        Assert.Equal(id, Assert.Single(cs).Id);
        Assert.Equal(EstatusCatalogo.Inactivo, cs[0].Estatus);
        Assert.Equal(Rfc, cs[0].Rfc);
        Assert.Equal(60, Assert.Single(regs).DiasNominalesOrigen);

        using var scope2 = _factory.Services.CreateScope();
        var port = scope2.ServiceProvider.GetRequiredService<IClientesReadPort>();
        // Cartera/documentos históricos: el puerto sigue devolviendo al cliente inactivo.
        Assert.NotNull(await port.ObtenerAsync(id, CancellationToken.None));
        Assert.NotNull(await port.ResolverPorReferenciaAsync(r, CancellationToken.None));
        // Uso nuevo (selector de emisión): ya no aparece.
        var busqueda = await port.BuscarAsync(null, cs[0].RazonSocial, 50, CancellationToken.None);
        Assert.DoesNotContain(busqueda, b => b.ClienteId == id);

        // Un pedido posterior por la misma referencia no crea otro cliente.
        var prov = await Provisionar(new ProvisionarClienteDesdeAwCommand(r, "OTRA", Rfc, null, "06600", null, null, null, "MXN"));
        Assert.False(prov.Creado);
        Assert.Equal(id, prov.ClienteId);
    }

    [Fact]
    public async Task Correccion_fiscal_local_posterior_al_alta_sobrevive_a_nueva_lectura()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        await Editar(r, c => c.ActualizarDatos(rfc: "AAA010101AAA", regimenFiscal: "601", codigoPostalFiscal: "44100"));

        var res = await Aplicar(Snap(r, razon: "OTRO NAME") with
        {
            Rfc = Rfc, CodigoPostalFiscal = "99999", DomicilioCp = "99999", NombreComercialOrigen = "OTRO",
        });

        Assert.Equal(AplicarClienteAwAccion.Actualizado, res.Accion);
        var (cs, _) = await Leer(r);
        Assert.Equal("AAA010101AAA", cs[0].Rfc);
        Assert.Equal("601", cs[0].RegimenFiscal);
        Assert.Equal("44100", cs[0].CodigoPostalFiscal);
        Assert.Equal("CLIENTE DEMO 001", cs[0].RazonSocial);
    }

    [Fact]
    public async Task Telefono_y_Email_solo_se_llenan_si_estaban_vacios()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        await Aplicar(Snap(r) with { Telefono = "5550001", Email = "a@demo.test" });
        var (cs1, _) = await Leer(r);
        Assert.Equal("5550001", cs1[0].Telefono);
        Assert.Equal("a@demo.test", cs1[0].Email);

        await Aplicar(Snap(r) with { Telefono = "5559999", Email = "b@demo.test" });
        var (cs2, _) = await Leer(r);
        Assert.Equal("5550001", cs2[0].Telefono);
        Assert.Equal("a@demo.test", cs2[0].Email);
    }

    [Fact]
    public async Task Dos_referencias_con_el_mismo_RFC_generico_crean_dos_clientes()
    {
        var r1 = NuevaRef();
        var r2 = NuevaRef();
        await Aplicar(Snap(r1));
        await Aplicar(Snap(r2));

        var (c1, _) = await Leer(r1);
        var (c2, _) = await Leer(r2);
        Assert.NotEqual(c1[0].Id, c2[0].Id);
        Assert.Equal(c1[0].Rfc, c2[0].Rfc);
    }

    [Fact]
    public async Task Moneda_sin_equivalencia_no_crea_el_cliente_ni_asume_MXN()
    {
        var r = NuevaRef();
        var res = await Aplicar(Snap(r) with
        {
            MonedaCodigoOrigen = "<indf>", MonedaNormalizada = null, MonedaDefault = null,
        });

        Assert.Equal(AplicarClienteAwAccion.NoCreado, res.Accion);
        Assert.Null(res.Cliente);
        Assert.Equal(ResultadoSincronizacionAw.Pendiente, res.Resultado);
        var (cs, regs) = await Leer(r);
        Assert.Empty(cs);
        Assert.Empty(regs);

        // Al resolverse el mapeo, el reintento sí lo crea.
        var ok = await Aplicar(Snap(r));
        Assert.Equal(AplicarClienteAwAccion.Creado, ok.Accion);
    }

    [Fact]
    public async Task Condicion_sin_coincidencia_deja_Pendiente_sin_bloquear_el_alta()
    {
        var r = NuevaRef();
        var res = await Aplicar(Snap(r) with
        {
            CondicionCodigoOrigen = "ZXXX", CondicionNumeroOrigen = null, DiasNominalesOrigen = null,
        });

        Assert.Equal(AplicarClienteAwAccion.Creado, res.Accion);
        Assert.Equal(ResultadoSincronizacionAw.Pendiente, res.Resultado);
        var (cs, regs) = await Leer(r);
        Assert.Single(cs);
        Assert.Null(regs[0].CondicionNumeroOrigen);
        Assert.Equal(ResultadoSincronizacionAw.Pendiente, regs[0].Resultado);
    }

    [Fact]
    public async Task Lo_recibido_que_no_se_aplica_queda_como_diferencia_en_el_registro()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r) with { Telefono = "5550001", Email = "a@demo.test" });
        var res = await Aplicar(Snap(r, razon: "OTRA RAZON") with
        {
            MonedaDefault = "USD", MonedaNormalizada = "USD", MonedaCodigoOrigen = "USD",
            Telefono = "5559999", Email = "A@DEMO.TEST",
        });

        Assert.Equal(AplicarClienteAwAccion.Actualizado, res.Accion);
        var (cs, regs) = await Leer(r);
        Assert.Equal("MXN", cs[0].MonedaDefault);
        var dif = DiferenciaAplicacionAw.Leer(regs[0].Diferencias);
        Assert.Equal(["Razón social", "Moneda", "Teléfono"], dif.Select(d => d.Campo));
        Assert.Equal("USD", dif.Single(d => d.Campo == "Moneda").Recibido);
        Assert.Equal("MXN", dif.Single(d => d.Campo == "Moneda").Conservado);

        // Una lectura que ya no difiere limpia las diferencias.
        await Aplicar(Snap(r, razon: "CLIENTE DEMO 001") with { Telefono = "5550001", Email = "a@demo.test", NombreComercialOrigen = "OTRO" });
        var (_, regs2) = await Leer(r);
        Assert.Empty(DiferenciaAplicacionAw.Leer(regs2[0].Diferencias));
    }

    [Fact]
    public async Task Nulo_es_distinto_de_cero_en_limite_de_credito_y_dias_nominales()
    {
        var rNulo = NuevaRef();
        var rCero = NuevaRef();
        await Aplicar(Snap(rNulo) with { CreditoReferenciaLimite = null, CreditoReferenciaNet = null, DiasNominalesOrigen = null });
        await Aplicar(Snap(rCero) with { CreditoReferenciaLimite = 0m, CreditoReferenciaNet = 0d, DiasNominalesOrigen = 0 });

        var (_, n) = await Leer(rNulo);
        var (_, z) = await Leer(rCero);
        Assert.Null(n[0].CreditoReferenciaLimite);
        Assert.Null(n[0].CreditoReferenciaNet);
        Assert.Null(n[0].DiasNominalesOrigen);
        Assert.Equal(0m, z[0].CreditoReferenciaLimite);
        Assert.Equal(0d, z[0].CreditoReferenciaNet);
        Assert.Equal(0, z[0].DiasNominalesOrigen);
        Assert.NotEqual(n[0].HashOrigen, z[0].HashOrigen);
    }

    [Fact]
    public async Task Estado_de_origen_presente_deja_Pendiente_y_no_cambia_Estatus()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        var (antes, _) = await Leer(r);

        var res = await Aplicar(Snap(r) with { EstadoOrigenCrudo = 2 });

        Assert.Equal(ResultadoSincronizacionAw.Pendiente, res.Resultado);
        var (cs, regs) = await Leer(r);
        Assert.Equal(antes[0].Estatus, cs[0].Estatus);
        Assert.Equal(2, regs[0].EstadoOrigenCrudo);
        Assert.Equal(ResultadoSincronizacionAw.Pendiente, regs[0].Resultado);
    }

    [Fact]
    public async Task Cliente_manual_con_la_misma_referencia_da_Conflicto_sin_escrituras()
    {
        var r = NuevaRef();
        await ConScopeAsync(async (db, _) =>
        {
            db.Clientes.Add(new Cliente(Guid.CreateVersion7(), $"M-{r}", "CLIENTE MANUAL", referenciaExterna: r));
            await db.SaveChangesAsync();
            return 0;
        });

        var res = await Aplicar(Snap(r) with { Telefono = "5550002" });

        Assert.Equal(AplicarClienteAwAccion.Conflicto, res.Accion);
        Assert.Equal(ResultadoSincronizacionAw.Conflicto, res.Resultado);
        var (cs, regs) = await Leer(r);
        Assert.Single(cs);
        Assert.Empty(regs);
        Assert.Null(cs[0].Telefono);
        Assert.Equal("CLIENTE MANUAL", cs[0].RazonSocial);
    }

    [Fact]
    public async Task SoloCrear_no_toca_un_registro_existente()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        var (_, antes) = await Leer(r);

        var res = await Aplicar(Snap(r) with { SoloCrear = true, NombreComercialOrigen = "OTRO", Telefono = "5550003" });

        Assert.Equal(AplicarClienteAwAccion.SinCambios, res.Accion);
        var (cs, despues) = await Leer(r);
        Assert.Equal(antes[0].HashOrigen, despues[0].HashOrigen);
        Assert.Equal(antes[0].Version, despues[0].Version);
        Assert.Equal("DEMO NAME1", despues[0].NombreComercialOrigen);
        Assert.Null(cs[0].Telefono);
    }

    [Fact]
    public async Task Clave_AW_ya_usada_por_cliente_manual_lanza_CLIENTE_CLAVE_DUPLICADA()
    {
        var r = NuevaRef();
        await ConScopeAsync(async (db, _) =>
        {
            db.Clientes.Add(new Cliente(Guid.CreateVersion7(), $"AW-{r}", "CLIENTE MANUAL CLAVE"));
            await db.SaveChangesAsync();
            return 0;
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Aplicar(Snap(r)));

        Assert.Equal("CLIENTE_CLAVE_DUPLICADA", ex.Code);
        var (cs, regs) = await Leer(r);
        Assert.Empty(cs);
        Assert.Empty(regs);
    }

    [Fact]
    public async Task Concurrencia_8_altas_paralelas_de_la_misma_referencia_dejan_un_cliente_y_un_registro()
    {
        var r = NuevaRef();
        var tareas = Enumerable.Range(0, 8).Select(_ => Task.Run(() => Aplicar(Snap(r)))).ToArray();

        var resultados = await Task.WhenAll(tareas); // cualquier excepción falla el test

        Assert.Equal(8, resultados.Length);
        Assert.Contains(resultados, x => x.Accion == AplicarClienteAwAccion.Creado);
        var (cs, regs) = await Leer(r);
        Assert.Single(cs);
        Assert.Single(regs);
    }

    [Fact]
    public async Task Hash_mismo_snapshot_mismo_hash_y_cambio_de_condicion_resuelta_cambia_el_hash()
    {
        // CalcularHash es internal: se observa a través de HashOrigen persistido.
        var r1 = NuevaRef(); var r2 = NuevaRef(); var r3 = NuevaRef();
        await Aplicar(Snap(r1));
        await Aplicar(Snap(r2) with { LeidoEnUtc = DateTime.UtcNow.AddDays(1), EjecucionId = Guid.NewGuid() });
        await Aplicar(Snap(r3) with { CondicionNumeroOrigen = 45 }); // mismo ZAHLBED, BRUTTOTAGE distinto

        var h1 = (await Leer(r1)).Registros[0].HashOrigen;
        var h2 = (await Leer(r2)).Registros[0].HashOrigen;
        var h3 = (await Leer(r3)).Registros[0].HashOrigen;
        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);
    }

    [Fact]
    public async Task Provisionar_handler_existente_se_devuelve_sin_tocar_y_nuevo_es_Creado()
    {
        var r = NuevaRef();
        var cmd = new ProvisionarClienteDesdeAwCommand(r, "CLIENTE DEMO PED", Rfc, "5550004", "06600", null, null, null, "MXN");

        var nuevo = await Provisionar(cmd);
        Assert.True(nuevo.Creado);
        await Editar(r, c => c.ActualizarDatos(razonSocial: "EDITADO A MANO", regimenFiscal: "601"));

        var existente = await Provisionar(cmd with { RazonSocial = "OTRA", Telefono = "5559999" });

        Assert.False(existente.Creado);
        Assert.Equal(nuevo.ClienteId, existente.ClienteId);
        Assert.Equal("EDITADO A MANO", existente.RazonSocial);
        Assert.Equal("601", existente.RegimenFiscal);
        var (cs, regs) = await Leer(r);
        Assert.Equal("5550004", cs[0].Telefono);
        Assert.Single(regs);
    }

    private async Task<ProvisionarClienteDesdeAwResponse> Provisionar(ProvisionarClienteDesdeAwCommand cmd)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(cmd);
    }
}
