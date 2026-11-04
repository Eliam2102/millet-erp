using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Compras.Application.Oc.ActualizarNumeroPedimento;
using Millet.Compras.Application.Oc.Autorizar;
using Millet.Compras.Domain;
using Millet.Compras.Domain.AlmCompra;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.Integraciones.Aw.Infrastructure.AlmCompra;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// 86e3jvu83 (C3.2): la OC que surte una RQ de la app de Almacenes deja COMPRA_RECIBIDA al autorizarse, COMPRA_MODIFICADA al
/// editar el pedimento y COMPRA_CANCELADA al cancelarse, y la bandeja se vacía contra el transporte simulado (por omisión).
/// Sin cleanup, igual que el resto de la clase: folios y claves A+W son únicos por corrida.
/// </summary>
public partial class OrdenesCompraEndpointsTests
{
    private sealed record OcApp(Guid OcId, string FolioOc, string FolioRq, Guid LineaOcId);

    private async Task<OcApp> SembrarOcAppAutorizadaAsync(HttpClient client, bool recibido = false)
    {
        var s = await SembrarProveedorYArticuloAppAsync();
        var response = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        response.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<ComprasDbContext>();
        var compartido = sp.GetRequiredService<CompartidoDbContext>();
        var articuloId = await (from a in compartido.Articulos
                                join p in compartido.ProductosAw on a.ProductoAwId equals p.Id
                                where p.ReferenciaExterna == s.ArticuloAw select a.Id).SingleAsync();
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == id);
        db.Entry(oc).Property(o => o.CompradorTitularId).CurrentValue = Guid.NewGuid();
        db.Entry(oc).Property(o => o.ProveedorId).CurrentValue = s.ProveedorId;
        var folioRq = $"RQ-{Guid.NewGuid():N}"[..20];
        var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        var rq = Requisicion.CrearDesdeAlmacenesApp(Guid.NewGuid(), oc.EmpresaId,
            Millet.Compras.Domain.Folio.Parse($"MID2026-{numero:D6}"), 2026, oc.SucursalDestinoId, Guid.NewGuid(), null,
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, folioRq, s.ProveedorId,
            [new LineaAppRequisicion(articuloId, 13, "PZA", Money.Mxn(100), 13, 3302, 6100)]);
        var lr = rq.Lineas.Single();
        rq.RegistrarCubrimiento([new CubrimientoLinea(lr.Id, 0, 13)], DateTimeOffset.UtcNow);
        var lo = oc.AgregarLineaDesdeRequisicion(Guid.NewGuid(), articuloId, 13, "PZA", 100, rq.DepartamentoId, rq.Id, lr.Id,
            centroCostoId: CentroCostoSeedId);
        rq.ComprometerEnOc(id);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (recibido) oc.RegistrarRecepcionLinea(lo.Id, 4, DateTimeOffset.UtcNow);
        db.Requisiciones.Add(rq);
        await db.SaveChangesAsync();
        // N2 por el handler real: publica OrdenCompraAutorizadaEvent antes de guardar (el encolador va en la misma transacción).
        await new AutorizarOrdenCompraHandler(db, compartido, new UsuarioP2(Guid.NewGuid()), sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPublisher>()).Handle(new(id, NivelAutorizacion.Nivel2, null), default);
        return new(id, oc.Folio.Valor, folioRq, lo.Id);
    }

    private async Task<List<AlmCompraEnvio>> EnviosAsync(Guid ocId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().AlmCompraEnvios.AsNoTracking()
            .Where(e => e.OrdenCompraId == ocId).OrderBy(e => e.Secuencia).ToListAsync();
    }

    private async Task<AlmCompraTick> ProcesarBandejaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AlmCompraProcesador>().ProcesarAsync(default);
    }

    [Fact]
    public async Task AlmCompra_al_autorizar_la_OC_de_la_app_sale_COMPRA_RECIBIDA_con_el_contrato_v6_y_el_simulado_la_procesa_una_vez()
    {
        var client = await CreateSuperAdminClientAsync();
        var oc = await SembrarOcAppAutorizadaAsync(client);

        var r = Assert.Single(await EnviosAsync(oc.OcId));
        Assert.Equal(("COMPRA_RECIBIDA", "PENDIENTE", oc.FolioOc, $"ERP-{oc.FolioOc}-R1"), (r.Tipo, r.Estado, r.Referencia, r.MensajeId));
        using (var doc = JsonDocument.Parse(r.Sobre))
        {
            var datos = doc.RootElement.GetProperty("datos");
            Assert.Equal(oc.FolioOc, datos.GetProperty("folio").GetString());
            Assert.Equal("MXN", datos.GetProperty("moneda").GetString());
            Assert.False(datos.TryGetProperty("tipoCambio", out _));
            var l = Assert.Single(datos.GetProperty("lineas").EnumerateArray());
            Assert.Equal((oc.FolioRq, 1, 3302, 6100, 13), (l.GetProperty("requisicion").GetString(), l.GetProperty("posicion").GetInt32(),
                l.GetProperty("ancho").GetInt32(), l.GetProperty("alto").GetInt32(), l.GetProperty("piezas").GetInt32()));
            Assert.Equal(100m, l.GetProperty("costoPieza").GetDecimal());
            Assert.False(l.TryGetProperty("costoMedioM2", out _)); // fórmula pendiente: no se manda
            Assert.False(l.TryGetProperty("articuloAlmacenAw", out _));
        }

        await ProcesarBandejaAsync();
        await ProcesarBandejaAsync(); // segundo ciclo: nada nuevo
        var procesada = Assert.Single(await EnviosAsync(oc.OcId));
        Assert.Equal("PROCESADO", procesada.Estado);
        Assert.NotNull(procesada.CompraIdAlmacenes);
        var sim = _factory.Services.GetRequiredService<SimuladoAlmCompraTransporte>();
        Assert.Single(sim.Escrituras, x => x == r.MensajeId);

        // El mismo evento de autorización llegando otra vez no duplica ni crea una MODIFICADA sin cambios.
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<AlmCompraEncolador>().OcAutorizadaAsync(oc.OcId, default);
            await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().SaveChangesAsync();
        }
        Assert.Single(await EnviosAsync(oc.OcId));
    }

    [Fact]
    public async Task AlmCompra_el_pedimento_viaja_en_COMPRA_MODIFICADA_completa_solo_si_cambia()
    {
        var client = await CreateSuperAdminClientAsync();
        var oc = await SembrarOcAppAutorizadaAsync(client);
        await ProcesarBandejaAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ActualizarNumeroPedimentoCommand(oc.OcId, "26 47 3456 6001234"));
            await mediator.Send(new ActualizarNumeroPedimentoCommand(oc.OcId, "26 47 3456 6001234")); // sin cambio
        }
        var filas = await EnviosAsync(oc.OcId);
        Assert.Equal(["COMPRA_RECIBIDA", "COMPRA_MODIFICADA"], filas.Select(f => f.Tipo));
        var m = filas[1];
        Assert.Equal($"ERP-{oc.FolioOc}-M1", m.MensajeId);
        using (var doc = JsonDocument.Parse(m.Sobre))
        {
            var datos = doc.RootElement.GetProperty("datos");
            Assert.Equal(oc.FolioOc, datos.GetProperty("folio").GetString()); // completa, no en diferencias
            Assert.Equal("26 47 3456 6001234", datos.GetProperty("lineas")[0].GetProperty("pedimento").GetString());
        }
        await ProcesarBandejaAsync();
        Assert.Equal("PROCESADO", (await EnviosAsync(oc.OcId))[1].Estado);
    }

    [Fact]
    public async Task AlmCompra_cancelar_manda_COMPRA_CANCELADA_y_con_material_recibido_queda_en_ERROR_local_reintentable_por_el_endpoint()
    {
        var client = await CreateSuperAdminClientAsync();
        var limpia = await SembrarOcAppAutorizadaAsync(client);
        var conEntrada = await SembrarOcAppAutorizadaAsync(client, recibido: true);
        await ProcesarBandejaAsync();

        foreach (var oc in new[] { limpia, conEntrada })
        {
            using var scope = _factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(new OrdenCompraCanceladaEvent(oc.OcId,
                Guid.NewGuid(), oc.FolioOc, Millet.Compras.Domain.Oc.EstadoOrdenCompra.Autorizada, Guid.NewGuid(), "Cambio de proveedor",
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));
            await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().SaveChangesAsync();
        }

        var c = (await EnviosAsync(limpia.OcId))[1];
        Assert.Equal(("COMPRA_CANCELADA", "PENDIENTE", $"ERP-{limpia.FolioOc}-C1"), (c.Tipo, c.Estado, c.MensajeId));
        await ProcesarBandejaAsync();
        Assert.Equal("PROCESADO", (await EnviosAsync(limpia.OcId))[1].Estado);

        var local = (await EnviosAsync(conEntrada.OcId))[1];
        Assert.Equal(("COMPRA_CANCELADA", "ERROR", ""), (local.Tipo, local.Estado, local.Sobre));
        Assert.Contains("COMPRA_CON_ENTRADA", local.UltimoError);

        var lista = await client.GetAsync($"/api/v1/compras/integraciones/alm-compra/envios?ordenCompraId={conEntrada.OcId}");
        lista.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(lista);
        Assert.Contains(json.EnumerateArray(), e => e.GetProperty("estado").GetString() == "ERROR"
            && e.GetProperty("ultimoError").GetString()!.Contains("COMPRA_CON_ENTRADA"));
        Assert.DoesNotContain("sobre", await lista.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var reintento = await client.PostAsync($"/api/v1/compras/integraciones/alm-compra/envios/{local.Id}/reintentar", null);
        Assert.Equal(HttpStatusCode.NoContent, reintento.StatusCode);
        Assert.Equal("PENDIENTE", (await EnviosAsync(conEntrada.OcId))[1].Estado);
    }
}
