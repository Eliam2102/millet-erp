using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// 86e3tabdp con Postgres real y los triggers del ledger: cancelar una recepción (principal + MAT-REV) deja existencia y costo
/// promedio como antes, crea los movimientos CEN, publica el evento y es idempotente; con pago aplicado o el mismo usuario se rechaza
/// sin tocar saldos; sin el permiso <c>almacen.recepciones.cancelar</c> la API responde 403. Reusa el fixture aislado de ALM-04.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class CancelarRecepcionTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    private static readonly Guid OtroUsuario = Guid.NewGuid();

    private static async Task<Guid> RecibirAsync(AlmacenDbContext db, Ctx ctx, decimal cantidad, decimal danada, decimal precio)
    {
        var r = await HandlerRecepcionConPrecio(db, new CapturaEventos(), ctx, precio).Handle(
            new RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null, "AD662D33-6934-459C-A128-BDF0393E0062", null,
                [new RegistrarRecepcionLineaInput(ctx.ArticuloId, ctx.LineaOcId, cantidad, null, null, ctx.RackId,
                    CantidadDanada: danada, MotivoDano: danada > 0 ? "Hojas rotas" : null)]),
            CancellationToken.None);
        return r.RecepcionId;
    }

    // Misma construcción que CuarentenaRecepcionTests.HandlerRecepcion, con precio de OC configurable para probar el costo promedio.
    private static RegistrarRecepcionConFacturaHandler HandlerRecepcionConPrecio(AlmacenDbContext db, CapturaEventos eventos, Ctx ctx, decimal precio) =>
        new(db, new OcPrecio(ctx, precio), eventos, new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
            new DecimalesNulo(), ctx.Periodos, new ConversionIdentidad(), new CxpLigadas(), new ProveedorRfc());

    private static CancelarRecepcionHandler HandlerCancelar(AlmacenDbContext db, CapturaEventos eventos, Ctx ctx, Guid usuario, CxpLigadas? cxp = null) =>
        new(db, new OcPrecio(ctx, 100m), eventos, new UserFake(usuario), new EmpresaFake(ctx.EmpresaId), ctx.Periodos,
            new ConversionIdentidad(), cxp ?? new CxpLigadas(), new RelojFijo());

    private static async Task<decimal?> CostoPromedioAsync(AlmacenDbContext db, Guid ubicacion, Guid articulo)
    {
        db.ChangeTracker.Clear();
        return await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == ubicacion && s.ArticuloId == articulo)
            .Select(s => (decimal?)s.CostoPromedioMxn).FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Cancelar_revierte_existencia_y_costo_promedio_es_idempotente_y_publica_el_evento()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirAsync(db, ctx, 4m, 0m, 100m);                       // 4 @100
            var mala = await RecibirAsync(db, ctx, 4m, 1m, 130m);            // 3 @130 en rack + 1 dañada en MAT-REV
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(7m);
            (await CostoPromedioAsync(db, ctx.RackId, ctx.ArticuloId)).Should().BeApproximately(100m + 3m * 30m / 7m, 0.0001m);

            var eventos = new CapturaEventos();
            var r = await HandlerCancelar(db, eventos, ctx, OtroUsuario).Handle(new CancelarRecepcionCommand(mala, "articulo", "Era otro artículo"), default);

            r.FoliosCancelacion.Should().HaveCount(2).And.OnlyContain(f => f.StartsWith("M-CEN"));
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(4m);
            (await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId)).Should().Be(0m);
            (await CostoPromedioAsync(db, ctx.RackId, ctx.ArticuloId)).Should().BeApproximately(100m, 0.0001m);
            db.ChangeTracker.Clear();
            (await db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Cancelado)).Should().Be(2);
            var e = eventos.Unico<OcRecepcionCanceladaIntegrationEvent>();
            e.RecepcionId.Should().Be(mala);
            e.Lineas.Sum(l => l.Cantidad).Should().Be(4m);
            e.EventType.Should().Be("almacen.recepcion_oc.cancelada.v1");

            var otraVez = await HandlerCancelar(db, new CapturaEventos(), ctx, OtroUsuario).Handle(new CancelarRecepcionCommand(mala, "ARTICULO", "x"), default);
            otraVez.FoliosCancelacion.Should().BeEquivalentTo(r.FoliosCancelacion);
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(4m);
        });
    }

    [Fact]
    public async Task Mismo_usuario_o_factura_con_pago_se_rechazan_sin_tocar_saldos()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var id = await RecibirAsync(db, ctx, 5m, 0m, 100m);

            var mismo = () => HandlerCancelar(db, new CapturaEventos(), ctx, ctx.UserId).Handle(new CancelarRecepcionCommand(id, "OC", "x"), default);
            (await mismo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_CANCELA_MISMO_USUARIO");

            var cxp = new CxpLigadas { Facturas = [new FacturaLigadaRecepcionLectura(Guid.NewGuid(), "F-9", "Autorizada", 50m)] };
            var pagada = () => HandlerCancelar(db, new CapturaEventos(), ctx, OtroUsuario, cxp).Handle(new CancelarRecepcionCommand(id, "OC", "x"), default);
            (await pagada.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_FACTURA_PAGADA");

            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(5m);
            db.ChangeTracker.Clear();
            (await db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.CancelacionEntradaCompra)).Should().Be(0);
        });
    }

    [Fact]
    public async Task Con_salida_posterior_se_rechaza_con_RECEPCION_CON_SALIDAS()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var id = await RecibirAsync(db, ctx, 5m, 0m, 100m);
            var salida = new MovimientoInventario(Guid.CreateVersion7(), TipoMovimiento.SalidaConsumo, ctx.EmpresaId, Fecha);
            salida.AgregarLinea(new LineaMovimiento(Guid.CreateVersion7(), salida.Id, 1, ctx.ArticuloId, 1m, "PZA", 100m, ubicacionId: ctx.RackId));
            salida.Registrar(FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, 2027, Random.Shared.Next(900000, 999999)), ctx.UserId);
            db.Movimientos.Add(salida);
            await db.SaveChangesAsync();

            var act = () => HandlerCancelar(db, new CapturaEventos(), ctx, OtroUsuario).Handle(new CancelarRecepcionCommand(id, "OC", "x"), default);
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_CON_SALIDAS");
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(4m);
        });
    }

    [Fact]
    public async Task Sin_permiso_cancelar_la_API_responde_403()
    {
        var operativo = await ContabTestKit.ClienteConPermisosAsync(factory,
            PermisosCanonicos.AlmacenEntradasLeer, PermisosCanonicos.AlmacenEntradasRegistrar);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/almacen/recepciones/{Guid.NewGuid()}/cancelar")
        { Content = JsonContent.Create(new { motivoCodigo = "OC", motivoTexto = "x" }) };
        using var resp = await operativo.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─────────────────────────── Dobles ───────────────────────────

    private sealed class RelojFijo : IClock
    {
        public DateTimeOffset UtcNow => new(Fecha.Year, Fecha.Month, Fecha.Day, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class OcPrecio(Ctx ctx, decimal precio) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(
            new(ocId, "OC-CANCELAR", Guid.NewGuid(), ctx.EmpresaId, "Autorizada",
                [new OcLineaLectura(ctx.LineaOcId, ctx.ArticuloId, "PZA", 10m, 0m, precio)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class CxpLigadas : ICxpDocumentosReadPort
    {
        public List<FacturaLigadaRecepcionLectura> Facturas { get; init; } = [];
        public Task<CfdiRecibidoLectura?> ObtenerCfdiAsync(Guid id, CancellationToken ct) => Task.FromResult<CfdiRecibidoLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyList<FacturaLigadaRecepcionLectura>> ObtenerFacturasLigadasRecepcionAsync(
            IReadOnlyCollection<Guid> recepcionIds, IReadOnlyCollection<Guid> facturaIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaLigadaRecepcionLectura>>(Facturas);
        private static Task<IReadOnlyDictionary<Guid, string>> Vacio() => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class ProveedorRfc : IProveedorReadPort
    {
        public Task<ProveedorLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ProveedorLectura?>(new ProveedorLectura(id, "P", "Proveedor", "AAA010101AAA", true));
        public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(new Dictionary<Guid, ProveedorLectura>());
    }

    private sealed class ConversionIdentidad : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? capturada, string documento, CancellationToken ct)
        {
            var unidad = string.IsNullOrWhiteSpace(documento) ? "PZA" : documento;
            return Task.FromResult(new ConversionUnidad(cantidad, unidad, cantidad, 1, unidad, cantidad));
        }
    }
}
