using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Almacén-por-línea PR5: red de seguridad de la <b>ubicación nivel 4 por
/// línea de salida</b>. La persistencia ya existía (C7.2b) pero no tenía
/// ninguna prueba que la sostuviera; PR6 va a hacer <c>SET NOT NULL</c> sobre
/// <c>almacen.lineas_movimiento.ubicacion_id</c> y necesita esta base.
///
/// <para>Cubre los dos flujos de salida (<c>RegistrarSalidaConRequisicion</c> y
/// <c>RegistrarSalidaPorVale</c>) en sus dos caminos: con bin elegido y sin él.
/// El caso NULL es el crítico — los validators de salida NO exigen ubicación
/// (a diferencia de los de entrada), así que null es un valor legítimo que el
/// evento debe propagar <b>tal cual</b>, sin sustituirlo por el fallback de
/// costeo. Confundir "no eligió" con "eligió la ÚNICA" corrompería el backfill
/// de PR6.</para>
///
/// <para>Fixture aislado por GUIDs vía SQL crudo con cleanup en <c>finally</c>,
/// mismo molde que <see cref="TriggerBinExplicitoTests"/> — no toca el seed de
/// millet_dev. Se siembra saldo en el rack y en la ÚNICA porque el trigger
/// aborta la salida si no hay existencia (<c>SALDO_INEXISTENTE</c>).</para>
/// </summary>
public class SalidaUbicacionPorLineaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SalidaUbicacionPorLineaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SalidaConRq_con_bin_elegido_persiste_y_publica_ese_bin()
    {
        await EjecutarConFixtureAsync(async (db, empresaCtx, ctx) =>
        {
            var eventos = new CapturaEventos();
            var resp = await HandlerRq(db, eventos, ctx).Handle(
                ComandoRq(ctx, ubicacionId: ctx.RackId), CancellationToken.None);

            var linea = await LeerLineaUnicaAsync(db, resp.SalidaId);
            linea.UbicacionId.Should().Be(ctx.RackId);

            var payload = eventos.UnicaLineaDeSalida();
            payload.UbicacionId.Should().Be(ctx.RackId);
            payload.LineaSalidaId.Should().Be(linea.Id);

            // Candado de regresión de costeo (salida-por-línea C2): se congela el
            // CPP del bin ELEGIDO (rack = 25.5), NO el de la ÚNICA (99.0). El
            // refactor solo cambió la RESOLUCIÓN del bin, no la fórmula. Mutación:
            // apuntar el costeo a otro bin → estas dos aserciones fallan.
            linea.CostoUnitarioMxn.Should().Be(CostoRack);
            payload.CostoUnitarioMxn.Should().Be(CostoRack);

            // El saldo del rack elegido se descuenta (el trigger PG lo hizo).
            var saldoRack = await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId);
            saldoRack!.Cantidad.Should().Be(CantidadInicial - CantidadSalida);
        });
    }

    [Fact]
    public async Task SalidaPorVale_con_bin_elegido_persiste_y_publica_ese_bin()
    {
        await EjecutarConFixtureAsync(async (db, empresaCtx, ctx) =>
        {
            var eventos = new CapturaEventos();
            var resp = await HandlerVale(db, eventos, ctx).Handle(
                ComandoVale(ctx, ubicacionId: ctx.RackId), CancellationToken.None);

            var linea = await LeerLineaUnicaAsync(db, resp.SalidaId);
            linea.UbicacionId.Should().Be(ctx.RackId);

            var payload = eventos.UnicaLineaDeSalida();
            payload.UbicacionId.Should().Be(ctx.RackId);

            // Candado de regresión de costeo del VALE: congela el CPP del bin
            // ELEGIDO (rack = 25.5), NO el de la ÚNICA (99.0). El refactor solo
            // cambió la resolución del bin, no la fórmula. Mutación: apuntar el
            // costeo del vale a otro bin → estas dos aserciones fallan.
            linea.CostoUnitarioMxn.Should().Be(CostoRack);
            payload.CostoUnitarioMxn.Should().Be(CostoRack);
        });
    }

    /// <summary>
    /// Salida-por-línea C2 (invierte el caso PR6a): sin bin, la salida-con-RQ se
    /// RECHAZA (antes coalesce a la ÚNICA). El bin es obligatorio; el validator
    /// lo exige y el handler lo re-valida (defensa fuera del pipeline) con
    /// SALIDA_UBICACION_REQUERIDA. El vale (variante B) conserva su coalesce.
    /// </summary>
    [Fact]
    public async Task SalidaConRq_sin_bin_es_rechazada()
    {
        await EjecutarConFixtureAsync(async (db, empresaCtx, ctx) =>
        {
            var eventos = new CapturaEventos();
            var act = async () => await HandlerRq(db, eventos, ctx).Handle(
                ComandoRq(ctx, ubicacionId: null), CancellationToken.None);
            (await act.Should().ThrowAsync<BusinessRuleException>())
                .Which.Code.Should().Be("SALIDA_UBICACION_REQUERIDA");
        });
    }

    /// <summary>
    /// Salida-por-línea C2: si las líneas resuelven a sub-almacenes DISTINTOS
    /// (bins en subs diferentes), el handler aborta con SALIDA_MULTI_SUBALMACEN
    /// antes de tocar la BD. El trigger PG (MOVIMIENTO_MULTI_SUBALMACEN) es el
    /// backstop; acá se anticipa con error de dominio legible. Mutación: quitar
    /// el throw del handler → el comando llega al SaveChanges y el trigger lo
    /// aborta con otro código.
    /// </summary>
    [Fact]
    public async Task SalidaConRq_bins_de_distinto_sub_es_rechazada()
    {
        await EjecutarConFixtureAsync(async (db, empresaCtx, ctx) =>
        {
            // Segundo sub + rack (mismo almacén), para forzar el cruce.
            var subB = Guid.NewGuid();
            var rackB = Guid.NewGuid();
            var claveB = "SB" + subB.ToString("N")[..8];
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subB}, {ctx.AlmacenId}, {claveB}, 'Sub B PR5', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackB}, {subB}, 'RACK-B5', 'Rack B PR5', 0, false, 0, NOW(), NOW())");

            var eventos = new CapturaEventos();
            var cmd = new RegistrarSalidaConRequisicionCommand(
                RequisicionId: Guid.NewGuid(),
                FechaMovimiento: Fecha,
                PersonaDestinatariaId: null,
                Observaciones: "PR5 multi-sub",
                Lineas: new[]
                {
                    LineaInput(ctx, ctx.RackId),  // subA
                    LineaInput(ctx, rackB),       // subB
                });

            var act = async () => await HandlerRq(db, eventos, ctx).Handle(
                cmd, CancellationToken.None);
            (await act.Should().ThrowAsync<BusinessRuleException>())
                .Which.Code.Should().Be("SALIDA_MULTI_SUBALMACEN");

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE id = {rackB}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE id = {subB}");
        });
    }

    /// <summary>
    /// El vale no nace de una RQ: el evento va con <c>RqId = null</c> y
    /// <c>EsPorVale = true</c>. Salida-por-línea (vale): con bin viaja el bin;
    /// SIN bin ahora se RECHAZA (antes coalesce a la ÚNICA) — el bin es
    /// obligatorio, igual que la salida-con-RQ.
    /// </summary>
    [Fact]
    public async Task SalidaPorVale_sin_rq_con_bin_publica_y_sin_bin_rechaza()
    {
        await EjecutarConFixtureAsync(async (db, empresaCtx, ctx) =>
        {
            var conBin = new CapturaEventos();
            await HandlerVale(db, conBin, ctx).Handle(
                ComandoVale(ctx, ubicacionId: ctx.RackId), CancellationToken.None);

            var eventoConBin = conBin.UnicoEventoDeSalida();
            eventoConBin.RqId.Should().BeNull();
            eventoConBin.EsPorVale.Should().BeTrue();
            eventoConBin.Lineas.Single().UbicacionId.Should().Be(ctx.RackId);

            // Sin bin → rechazo (SALIDA_UBICACION_REQUERIDA); ya no coalesce ÚNICA.
            var sinBin = new CapturaEventos();
            var act = async () => await HandlerVale(db, sinBin, ctx).Handle(
                ComandoVale(ctx, ubicacionId: null), CancellationToken.None);
            (await act.Should().ThrowAsync<BusinessRuleException>())
                .Which.Code.Should().Be("SALIDA_UBICACION_REQUERIDA");
        });
    }

    /// <summary>
    /// Salida-por-línea (vale): líneas con bins en sub-almacenes DISTINTOS → el
    /// handler del vale aborta con SALIDA_MULTI_SUBALMACEN (mismo molde que la RQ).
    /// Mutación: quitar el throw del handler del vale → el comando llega al
    /// SaveChanges y el trigger lo aborta con otro código.
    /// </summary>
    [Fact]
    public async Task SalidaPorVale_bins_de_distinto_sub_es_rechazada()
    {
        await EjecutarConFixtureAsync(async (db, empresaCtx, ctx) =>
        {
            var subB = Guid.NewGuid();
            var rackB = Guid.NewGuid();
            var claveB = "VB" + subB.ToString("N")[..8];
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subB}, {ctx.AlmacenId}, {claveB}, 'Sub B vale', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackB}, {subB}, 'RACK-VB', 'Rack B vale', 0, false, 0, NOW(), NOW())");

            var eventos = new CapturaEventos();
            var cmd = new RegistrarSalidaPorValeCommand(
                FechaMovimiento: Fecha,
                ValeBlobRef: "vales/pr5-multisub.pdf",
                PersonaDestinatariaId: null,
                Observaciones: "PR5 vale multi-sub",
                Lineas: new[]
                {
                    LineaInput(ctx, ctx.RackId),  // subA
                    LineaInput(ctx, rackB),       // subB
                });

            var act = async () => await HandlerVale(db, eventos, ctx).Handle(
                cmd, CancellationToken.None);
            (await act.Should().ThrowAsync<BusinessRuleException>())
                .Which.Code.Should().Be("SALIDA_MULTI_SUBALMACEN");

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE id = {rackB}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE id = {subB}");
        });
    }

    // ─────────────────────────── Fixture ───────────────────────────

    private const decimal CantidadInicial = 100m;
    private const decimal CantidadSalida = 4m;
    // Salida-por-línea C2: CPP DISTINTO por bin, para que el candado de costeo
    // distinga que se congela el del bin ELEGIDO (rack), no el de la ÚNICA.
    private const decimal CostoRack = 25.5m;
    private const decimal CostoUnica = 99.0m;
    private static readonly DateOnly Fecha = new(2027, 3, 10);

    private sealed record Ctx(
        Guid EmpresaId,
        Guid UserId,
        Guid AlmacenId,
        Guid SubAlmacenId,
        Guid UnicaId,
        Guid RackId,
        Guid ArticuloId);

    private async Task EjecutarConFixtureAsync(
        Func<AlmacenDbContext, ICurrentEmpresaContext, Ctx, Task> cuerpo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        var ctx = new Ctx(
            EmpresaId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            AlmacenId: Guid.NewGuid(),
            SubAlmacenId: Guid.NewGuid(),
            UnicaId: Guid.NewGuid(),
            RackId: Guid.NewGuid(),
            ArticuloId: Guid.NewGuid());
        var clave = $"P5{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await SembrarAsync(db, ctx, clave);
            // Los movimientos son IPerteneceAEmpresa y el empresaId del fixture
            // es sintético: sin bypass el filtro global bloquea el SaveChanges.
            using (empresaCtx.Bypass())
            {
                await cuerpo(db, empresaCtx, ctx);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
            // PR6a: el movimiento ya no lleva sub en cabecera; se borra vía la
            // ubicación de sus líneas.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM almacen.movimientos_inventario WHERE id IN (
                     SELECT lm.movimiento_id FROM almacen.lineas_movimiento lm
                     JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                     WHERE u.sub_almacen_id = {ctx.SubAlmacenId})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id = {ctx.SubAlmacenId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id = {ctx.SubAlmacenId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE id = {ctx.SubAlmacenId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.almacenes WHERE id = {ctx.AlmacenId}");
        }
    }

    private static async Task SembrarAsync(AlmacenDbContext db, Ctx ctx, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({ctx.AlmacenId}, {clave}, 'Test PR5', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({ctx.SubAlmacenId}, {ctx.AlmacenId}, {clave}, 'Sub test PR5', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ctx.UnicaId}, {ctx.SubAlmacenId}, 'UNICA', 'Unica PR5', 0, true, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ctx.RackId}, {ctx.SubAlmacenId}, 'RACK-P5', 'Rack PR5', 0, false, 0, NOW(), NOW())");

        // Saldo en ambos bins con CPP distinto: el rack (bin elegido) y la ÚNICA
        // (bin del vale sin bin). El costeo debe tomar el del bin efectivo.
        await SembrarSaldoAsync(db, ctx, ctx.RackId, CostoRack);
        await SembrarSaldoAsync(db, ctx, ctx.UnicaId, CostoUnica);
    }

    private static Task<int> SembrarSaldoAsync(
        AlmacenDbContext db, Ctx ctx, Guid ubicacionId, decimal costo) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario
                (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
            VALUES ({ubicacionId}, {ctx.SubAlmacenId}, {ctx.ArticuloId}, {CantidadInicial}, {costo}, NOW())");

    private static RegistrarSalidaConRequisicionHandler HandlerRq(
        AlmacenDbContext db, IIntegrationEventPublisher eventos, Ctx ctx) =>
        new(db, new RqPortNulo(), eventos, new FakeUserCtx(ctx.UserId),
            new FakeEmpresaCtx(ctx.EmpresaId), new DecimalesGuardNulo());

    private static RegistrarSalidaPorValeHandler HandlerVale(
        AlmacenDbContext db, IIntegrationEventPublisher eventos, Ctx ctx) =>
        new(db, eventos, new FakeUserCtx(ctx.UserId),
            new FakeEmpresaCtx(ctx.EmpresaId), new DecimalesGuardNulo());

    private static RegistrarSalidaConRequisicionCommand ComandoRq(Ctx ctx, Guid? ubicacionId) =>
        new(RequisicionId: Guid.NewGuid(),
            FechaMovimiento: Fecha,
            PersonaDestinatariaId: null,
            Observaciones: "PR5 regresión",
            Lineas: new[] { LineaInput(ctx, ubicacionId) });

    private static RegistrarSalidaPorValeCommand ComandoVale(Ctx ctx, Guid? ubicacionId) =>
        new(FechaMovimiento: Fecha,
            ValeBlobRef: "vales/pr5-test.pdf",
            PersonaDestinatariaId: null,
            Observaciones: "PR5 regresión vale",
            Lineas: new[] { LineaInput(ctx, ubicacionId) });

    private static RegistrarSalidaLineaInput LineaInput(Ctx ctx, Guid? ubicacionId) =>
        new(ArticuloId: ctx.ArticuloId,
            LineaRqId: null,
            Cantidad: CantidadSalida,
            CentroCostoId: null,
            ProyectoId: null,
            UbicacionReferencia: null,
            Comentario: null,
            UbicacionId: ubicacionId);

    private static async Task<LineaMovimiento> LeerLineaUnicaAsync(
        AlmacenDbContext db, Guid salidaId)
    {
        db.ChangeTracker.Clear();
        var movimiento = await db.Movimientos.AsNoTracking()
            .IgnoreQueryFilters()
            .Include(m => m.Lineas)
            .FirstAsync(m => m.Id == salidaId);
        return movimiento.Lineas.Single();
    }

    private static Task<SaldoInventario?> LeerSaldoAsync(
        AlmacenDbContext db, Guid ubicacionId, Guid articuloId) =>
        db.SaldosInventario.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UbicacionId == ubicacionId && s.ArticuloId == articuloId);

    // ─────────────────────────── Dobles ───────────────────────────

    /// <summary>Captura los eventos publicados para poder assertar el payload.</summary>
    private sealed class CapturaEventos : IIntegrationEventPublisher
    {
        private readonly List<object> _publicados = new();

        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            _publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }

        public SalidaRequisicionRegistradaIntegrationEvent UnicoEventoDeSalida() =>
            _publicados.OfType<SalidaRequisicionRegistradaIntegrationEvent>().Single();

        public LineaSalidaPayload UnicaLineaDeSalida() =>
            UnicoEventoDeSalida().Lineas.Single();
    }

    /// <summary>
    /// Devuelve null: el handler trata la RQ inexistente como "no valida
    /// estado" y sigue. Aísla el test del módulo Compras.
    /// </summary>
    private sealed class RqPortNulo : IComprasRequisicionReadPort
    {
        public Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken ct) =>
            Task.FromResult<RequisicionLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> rqIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                new Dictionary<Guid, string>());
    }

    /// <summary>ADR-0046 se prueba en su propia suite; aquí no debe interferir.</summary>
    private sealed class DecimalesGuardNulo : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeUserCtx(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? UserName => "Test PR5";
    }

    private sealed class FakeEmpresaCtx(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
