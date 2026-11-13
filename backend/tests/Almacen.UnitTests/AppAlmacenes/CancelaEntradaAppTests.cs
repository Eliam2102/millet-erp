using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.AppAlmacenes;

/// <summary>
/// v9 (Fernando Alejos, 10-nov): SALIDA_MANUAL con <c>origen.tipo = CANCELACION_ENTRADA</c> sobre una entrada que fue recepción de una OC
/// del ERP la revierte como la pantalla (CEN al mismo costo, entrada Cancelada, evento de cancelación), no como ajuste negativo.
/// EF InMemory no corre el trigger de saldos: cada prueba siembra el saldo como queda tras la salida CEN.
/// </summary>
public class CancelaEntradaAppTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Variante = Guid.NewGuid();
    private static readonly Guid OcId = Guid.NewGuid();
    private static readonly Guid LineaOc = Guid.NewGuid();
    private const string FolioOc = "OC-CNK2026-000123";
    private const decimal M2Hoja = 20.1422m;
    private const decimal Precio = 285.40m;

    private sealed class Mundo
    {
        public required InMemoryAlmacenDbContext Db { get; init; }
        public required Guid Ubicacion { get; init; }
        public required Guid Sub { get; init; }
        public Captura Eventos { get; } = new();
        public DateOnly MesCerrado { get; set; }
        public bool CerrarMesCerrado { get; set; }

        /// <summary>Hoy del ERP: dos meses después de la entrada, para que su mes cerrado no sea el de la cancelación.</summary>
        public DateTimeOffset Hoy { get; } = DateTimeOffset.UtcNow.AddMonths(2);

        public async Task<string?> EntrarAsync(string entrada, string? compra = FolioOc, decimal hojas = 4)
        {
            // Saldo como queda tras la salida CEN (sin trigger en InMemory): otras 10 hojas que ya había, al mismo costo de la OC.
            Db.SaldosInventario.Add(new SaldoInventario(Ubicacion, Sub, Articulo, 10 * M2Hoja, Precio, Variante, "L-1"));
            await Db.SaveChangesAsync();
            var (_, folio, error) = await EntradaAppAlmacenes.RegistrarAsync(Db, new EntradaAppPuertos(new Ocs(), new FactorUno(),
                new Periodo(this), Eventos), EmpresaId, Usuario, Sucursal, $"App Almacenes ENTRADA_ALMACEN {entrada}", compra, entrada,
                [new EntradaApp(Articulo, "M2", Variante, hojas * M2Hoja, 3302m, 6100m, "L-1")], default);
            error.Should().BeNull();
            await Db.SaveChangesAsync();
            Eventos.Publicados.Clear();
            return folio;
        }

        public SalidaAppAlmacenesGuardado Guardado()
        {
            var handler = new CancelarRecepcionHandler(Db, new Ocs(), Eventos, new SinUsuario(), new Empresa(), new Periodo(this), new FactorUno(),
                CfdiFake.Valido(), new Reloj(Hoy));
            return new SalidaAppAlmacenesGuardado(Db, new Empresa(), Eventos, new Ocs(), new FactorUno(), new Periodo(this), handler);
        }

        public Task<(string Estado, string? Folio, string? Error, bool Duplicado)?> CancelarAsync(string entrada, string mensajeId, string? motivo = "Se capturó dos veces") =>
            Guardado().CancelarEntradaAsync(mensajeId, "SALIDA_MANUAL", entrada, "{}", EmpresaId, Usuario, entrada, motivo, default);
    }

    private static async Task<Mundo> NuevoAsync()
    {
        var db = new InMemoryAlmacenDbContext(new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"cancela-entrada-app-{Guid.NewGuid():N}").Options, new Empresa());
        await db.Database.EnsureCreatedAsync();
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid(); var rev = Guid.NewGuid(); var unica = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "ALM-G", "General", Sucursal));
        db.SubAlmacenes.Add(new SubAlmacen(sub, alm, "DIR", "Directos", TipoSubAlmacen.MaterialesDirectos));
        db.SubAlmacenes.Add(new SubAlmacen(rev, alm, "REV", "Revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(unica, sub, "ÚNICA", "Única", esDefault: true));
        db.Ubicaciones.Add(new Ubicacion(Guid.NewGuid(), rev, "ÚNICA", "Única", esDefault: true));
        await db.SaveChangesAsync();
        return new Mundo { Db = db, Ubicacion = unica, Sub = sub };
    }

    [Fact]
    public async Task Cancelar_una_entrada_que_es_recepcion_del_ERP_la_revierte_al_mismo_costo_y_publica_la_cancelacion()
    {
        var m = await NuevoAsync();
        await m.EntrarAsync("EN-000820");

        var r = await m.CancelarAsync("EN-000820", "msg-c1");

        r!.Value.Should().Match<(string Estado, string? Folio, string? Error, bool Duplicado)>(x =>
            x.Estado == SalidaAppAlmacenes.Recibido && x.Folio!.StartsWith("M-CEN") && x.Error == null && !x.Duplicado);
        var movs = await m.Db.Movimientos.Include(x => x.Lineas).ToListAsync();
        var entrada = movs.Single(x => x.Tipo == TipoMovimiento.EntradaCompra);
        entrada.Estado.Should().Be(EstadoMovimiento.Cancelado);
        entrada.Motivo.Should().Be("OTRO: Se capturó dos veces");
        var cen = movs.Single(x => x.Tipo == TipoMovimiento.CancelacionEntradaCompra);
        cen.Folio.Should().Be(r.Value.Folio);
        cen.RecepcionOrigenId.Should().Be(entrada.Id);
        cen.Lineas.Single().Should().Match<LineaMovimiento>(l => l.CostoUnitarioMxn == Precio && l.Cantidad == 4 * M2Hoja);
        movs.Should().NotContain(x => x.Tipo == TipoMovimiento.AjusteNegativo);
        var e = m.Eventos.Publicados.OfType<OcRecepcionCanceladaIntegrationEvent>().Single();
        (e.RecepcionId, e.OrdenCompraId, e.MotivoCodigo, e.MotivoTexto).Should().Be((entrada.Id, OcId, "OTRO", "Se capturó dos veces"));
        (await m.Db.SaldosInventario.AsNoTracking().SingleAsync()).CostoPromedioMxn.Should().Be(Precio);
        (await m.Db.MensajesApp.SingleAsync(x => x.MensajeId == "msg-c1")).Estado.Should().Be(SalidaAppAlmacenes.Recibido);
    }

    [Fact]
    public async Task Sin_motivo_usa_el_texto_por_omision_y_lo_trunca_al_maximo()
    {
        var m = await NuevoAsync();
        await m.EntrarAsync("EN-1");
        await m.CancelarAsync("EN-1", "msg-1", motivo: null);
        (await m.Db.Movimientos.SingleAsync(x => x.Tipo == TipoMovimiento.EntradaCompra)).Motivo
            .Should().Be("OTRO: Cancelación de entrada desde la app de Almacenes");

        var largo = await NuevoAsync();
        await largo.EntrarAsync("EN-2");
        await largo.CancelarAsync("EN-2", "msg-2", new string('x', 900));
        largo.Eventos.Publicados.OfType<OcRecepcionCanceladaIntegrationEvent>().Single().MotivoTexto
            .Should().HaveLength(MotivosCancelacionRecepcion.TextoMaximo);
    }

    [Fact]
    public async Task Entrada_ya_cancelada_es_DUPLICADO_con_los_folios_y_sin_otro_CEN_ni_evento()
    {
        var m = await NuevoAsync();
        await m.EntrarAsync("EN-3");
        var primera = (await m.CancelarAsync("EN-3", "msg-a"))!.Value;
        m.Eventos.Publicados.Clear();

        var otra = (await m.CancelarAsync("EN-3", "msg-b"))!.Value;

        (otra.Estado, otra.Folio, otra.Error).Should().Be(("DUPLICADO", primera.Folio, (string?)null));
        (await m.Db.Movimientos.CountAsync(x => x.Tipo == TipoMovimiento.CancelacionEntradaCompra)).Should().Be(1);
        m.Eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Entrada_de_un_mes_cerrado_se_revierte_con_fecha_de_hoy_en_su_propio_movimiento()
    {
        var m = await NuevoAsync();
        await m.EntrarAsync("EN-4");
        m.MesCerrado = (await m.Db.Movimientos.SingleAsync()).FechaMovimiento;
        m.CerrarMesCerrado = true;

        var r = (await m.CancelarAsync("EN-4", "msg-4"))!.Value;

        r.Estado.Should().Be(SalidaAppAlmacenes.Recibido);
        var cen = await m.Db.Movimientos.SingleAsync(x => x.Tipo == TipoMovimiento.CancelacionEntradaCompra);
        cen.FechaMovimiento.Should().Be(FechaNegocio.Hoy(m.Hoy)).And.NotBe(m.MesCerrado);
    }

    [Fact]
    public async Task Con_hoy_cerrado_es_ERROR_con_folio_de_revision_y_no_mueve_nada()
    {
        var m = await NuevoAsync();
        await m.EntrarAsync("EN-5");
        m.MesCerrado = FechaNegocio.Hoy(m.Hoy);
        m.CerrarMesCerrado = true;

        var r = (await m.CancelarAsync("EN-5", "msg-5"))!.Value;

        (r.Estado, r.Folio).Should().Be(("ERROR", SalidaAppAlmacenes.FolioRevision("msg-5")));
        r.Error.Should().NotBeNullOrEmpty();
        (await m.Db.Movimientos.SingleAsync()).Estado.Should().Be(EstadoMovimiento.Registrado);
        m.Eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Entrada_sin_recepcion_del_ERP_devuelve_null_para_seguir_como_salida_manual()
    {
        var m = await NuevoAsync();
        await m.EntrarAsync("EN-6", compra: "OC-DE-OTRO-SISTEMA"); // sin OC del ERP: entra al promedio, no es recepción

        (await m.CancelarAsync("EN-6", "msg-6")).Should().BeNull();
        (await m.CancelarAsync("EN-NUNCA-LLEGO", "msg-7")).Should().BeNull();
        (await m.Db.Movimientos.SingleAsync()).Estado.Should().Be(EstadoMovimiento.Registrado);
        m.Eventos.Publicados.Should().BeEmpty();
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private static OcLectura Oc() => new(OcId, FolioOc, Guid.NewGuid(), EmpresaId, "Autorizada",
        [new OcLineaLectura(LineaOc, Articulo, "M2", 201.422m, 0m, Precio, ProductoAwVarianteId: Variante, AnchoMm: 3302m, AltoMm: 6100m)],
        SucursalDestinoId: Sucursal);

    private sealed class Ocs : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult(ocId == OcId ? Oc() : null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<Guid?> ObtenerIdPorFolioAsync(string folio, CancellationToken ct) => Task.FromResult(folio == FolioOc ? (Guid?)OcId : null);
    }

    private sealed class FactorUno : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? unidadCapturada, string unidadDocumento, CancellationToken ct) =>
            Task.FromResult(new ConversionUnidad(cantidad, unidadDocumento, cantidad, 1m, unidadDocumento, cantidad));
    }

    private sealed class Periodo(Mundo mundo) : IPeriodoContableReadPort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) =>
            Task.FromResult(!(mundo.CerrarMesCerrado && mundo.MesCerrado.Year == año && mundo.MesCerrado.Month == mes));
    }

    private sealed class Captura : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    // Sin usuario autenticado, como en el lector de alm_mensaje: la cancelación de la app usa el usuario de servicio explícito.
    private sealed class SinUsuario : ICurrentUserContext
    {
        public Guid? UserId => null;
        public string? UserName => null;
    }

    private sealed class Reloj(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }
}
