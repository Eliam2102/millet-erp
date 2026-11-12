using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Conciliacion;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conciliacion;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.AppAlmacenes;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Conciliacion;

/// <summary>A4.6 (InMemory): conciliación diaria sin tolerancia; faltante = salida no planificada pendiente de aclarar. El saldo lo mueve el trigger de PG: se prueba en integración.</summary>
public class ConciliacionDiariaTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Variante = Guid.NewGuid();
    // Ayer en la zona del negocio (la conciliación compara contra el día local, no el UTC).
    private static readonly DateOnly Dia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City")).DateTime).AddDays(-1);
    private const decimal AreaHoja = 2.44m * 3.66m; // 8.9304 m²

    private static (AlmacenDbContext Db, Guid Almacen, Guid Ubi) Db(decimal hojas = 10)
    {
        var db = new AlmacenDbContext(new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"conciliacion-{Guid.NewGuid():N}").Options, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid(); var ubi = Guid.NewGuid();
        db.Almacenes.Add(new Domain.Catalogo.Almacen(alm, "MP-CNK", "Materia prima Conkal", Sucursal));
        db.SubAlmacenes.Add(new SubAlmacen(sub, alm, "VID", "Vidrio", TipoSubAlmacen.MaterialesDirectos));
        db.Ubicaciones.Add(new Ubicacion(ubi, sub, "ÚNICA", "Única", esDefault: true));
        db.SaldosInventario.Add(new SaldoInventario(ubi, sub, Articulo, hojas * AreaHoja, 250m, Variante, "L-2611"));
        db.SaveChanges();
        return (db, alm, ubi);
    }

    private static Task<(ConciliacionDiaria Corrida, IReadOnlyList<DiferenciaConciliacion> Difs)> Correr(
        AlmacenDbContext db, Guid almacen, Eventos eventos, decimal fisicoHojas, bool periodoAbierto = true) =>
        ConciliacionDiariaApp.EjecutarAsync(db, eventos, new Medidas(), new Articulos(), new PeriodoContableStub(periodoAbierto),
            EmpresaId, Usuario, almacen, Dia, ConciliacionDiaria.FuenteAw,
            [new FisicoLinea(Articulo, Variante, "L-2611", fisicoHojas)], default);

    [Fact]
    public async Task Una_hoja_de_menos_genera_salida_no_planificada_pendiente_de_aclarar()
    {
        var (db, alm, _) = Db(10);
        var eventos = new Eventos();
        var (corrida, difs) = await Correr(db, alm, eventos, 9);
        await db.SaveChangesAsync();

        corrida.Diferencias.Should().Be(1);
        var d = difs.Should().ContainSingle().Subject;
        d.Sentido.Should().Be(DiferenciaConciliacion.Faltante);
        d.Diferencia.Should().Be(-1m);
        d.Estado.Should().Be(DiferenciaConciliacion.Pendiente);
        d.FolioMovimiento.Should().StartWith("M-SNP");
        d.ValorMxn.Should().Be(Math.Round(AreaHoja * 250m, 2));

        var mov = await db.Movimientos.Include(m => m.Lineas).SingleAsync();
        mov.Tipo.Should().Be(TipoMovimiento.SalidaNoPlanificada);
        mov.Lineas.Should().ContainSingle(l => l.Cantidad == AreaHoja && l.Lote == "L-2611" && l.ProductoAwVarianteId == Variante);
        var ev = eventos.Publicados.OfType<SalidaInventarioRegistradaIntegrationEvent>().Should().ContainSingle().Subject;
        ev.Causa.Should().Be("salida_no_planificada");
    }

    [Fact]
    public async Task M225_conciliar_ayer_con_movimientos_de_hoy_usa_el_teorico_al_cierre_de_ayer()
    {
        var (db, alm, ubi) = Db(10);
        // Hoy entraron 2 hojas: el saldo actual (10) ya las incluye; ayer al cierre había 8.
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6));
        var mov = new MovimientoInventario(Guid.CreateVersion7(), TipoMovimiento.EntradaCompra, EmpresaId, Dia.AddDays(1));
        mov.AgregarLinea(new LineaMovimiento(Guid.CreateVersion7(), mov.Id, 1, Articulo, 2 * AreaHoja, "M2", 250m,
            ubicacionId: ubi, productoAwVarianteId: Variante, anchoMm: 2440m, altoMm: 3660m, lote: "L-2611"));
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, hoy.Year, 1), Usuario);
        db.Movimientos.Add(mov);
        await db.SaveChangesAsync();

        var (corrida, difs) = await Correr(db, alm, new Eventos(), 8);

        corrida.Diferencias.Should().Be(0);
        difs.Should().BeEmpty();
        db.ChangeTracker.Entries<MovimientoInventario>().Should().OnlyContain(e => e.Entity.Id == mov.Id);
    }

    [Fact]
    public async Task M225_con_un_mensaje_en_revision_del_dia_avisa_y_no_baja_el_faltante_sin_confirmar()
    {
        var (db, alm, _) = Db(10);
        db.MensajesApp.Add(new Millet.Almacen.Domain.Idempotencia.MensajeApp("ajuste-mesa-1", "AJUSTE_SALIDA", "MV-0015001", "{}",
            Millet.Almacen.Application.AppAlmacenes.SalidaAppAlmacenes.EnRevision, "REV-ajuste-mesa-1", "Sin existencia suficiente"));
        await db.SaveChangesAsync();
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City")).DateTime);
        Task<(ConciliacionDiaria, IReadOnlyList<DiferenciaConciliacion>)> CorrerHoy(bool confirmar) =>
            ConciliacionDiariaApp.EjecutarAsync(db, new Eventos(), new Medidas(), new Articulos(), new PeriodoContableStub(true),
                EmpresaId, Usuario, alm, hoy, ConciliacionDiaria.FuenteAw, [new FisicoLinea(Articulo, Variante, "L-2611", 9)], default, confirmar);

        var sinConfirmar = () => CorrerHoy(false);
        (await sinConfirmar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CONCILIACION_EVENTOS_PENDIENTES");
        db.ChangeTracker.Entries<MovimientoInventario>().Should().BeEmpty();

        (await ConciliacionDiariaApp.EventosDelDiaAsync(db, alm, hoy, default)).Pendientes.Should().ContainSingle(p => p.Referencia == "MV-0015001");
        var (_, difs) = await CorrerHoy(true);
        difs.Should().ContainSingle().Which.Sentido.Should().Be(DiferenciaConciliacion.Faltante);
    }

    [Fact]
    public async Task Un_dia_sin_diferencias_deja_la_corrida_y_ninguna_salida()
    {
        var (db, alm, _) = Db(10);
        var eventos = new Eventos();
        var (corrida, difs) = await Correr(db, alm, eventos, 10);
        await db.SaveChangesAsync();

        corrida.LineasComparadas.Should().Be(1);
        corrida.Diferencias.Should().Be(0);
        difs.Should().BeEmpty();
        (await db.ConciliacionesDiarias.CountAsync()).Should().Be(1);
        (await db.Movimientos.CountAsync()).Should().Be(0);
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Aclarar_guarda_el_motivo_y_no_descuenta_otra_vez()
    {
        var (db, alm, _) = Db(10);
        var (_, difs) = await Correr(db, alm, new Eventos(), 9);
        await db.SaveChangesAsync();

        difs[0].Aclarar(Guid.NewGuid(), "Hoja quebrada en la mesa HEGLA1 sin reporte de rotura");
        await db.SaveChangesAsync();

        difs[0].Estado.Should().Be(DiferenciaConciliacion.Aclarada);
        (await db.Movimientos.CountAsync()).Should().Be(1);
        var otra = () => difs[0].Aclarar(Guid.NewGuid(), "otra vez");
        otra.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("DIFERENCIA_YA_ACLARADA");
    }

    [Fact]
    public async Task Aclarar_sin_motivo_se_rechaza()
    {
        var (db, alm, _) = Db(10);
        var (_, difs) = await Correr(db, alm, new Eventos(), 9);
        var sin = () => difs[0].Aclarar(Guid.NewGuid(), "  ");
        sin.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("DIFERENCIA_MOTIVO_INVALIDO");
    }

    [Fact]
    public async Task Hoja_de_mas_queda_como_sobrante_sin_movimiento()
    {
        var (db, alm, _) = Db(10);
        var (_, difs) = await Correr(db, alm, new Eventos(), 11);
        await db.SaveChangesAsync();

        var d = difs.Should().ContainSingle().Subject;
        d.Sentido.Should().Be(DiferenciaConciliacion.Sobrante);
        d.MovimientoId.Should().BeNull();
        (await db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Periodo_cerrado_no_concilia()
    {
        var (db, alm, _) = Db(10);
        var intento = () => Correr(db, alm, new Eventos(), 9, periodoAbierto: false);
        await intento.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PERIODO_CONTABLE_NO_ADMITE");
    }

    [Fact]
    public async Task Fisico_con_media_hoja_o_lineas_repetidas_se_rechaza()
    {
        var (db, alm, _) = Db(10);
        var media = () => Correr(db, alm, new Eventos(), 9.5m);
        await media.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "HOJAS_ENTERAS");
        var repetida = () => ConciliacionDiariaApp.EjecutarAsync(db, new Eventos(), new Medidas(), new Articulos(),
            new PeriodoContableStub(true), EmpresaId, Usuario, alm, Dia, ConciliacionDiaria.FuenteAw,
            [new FisicoLinea(Articulo, Variante, "L-2611", 9), new FisicoLinea(Articulo, Variante, " L-2611 ", 9)], default);
        await repetida.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CONCILIACION_LINEA_DUPLICADA");
    }

    private sealed class Medidas : IVarianteVidrioReadPort
    {
        public Task<VarianteVidrioLectura> ObtenerAsync(Guid articuloId, Guid varianteId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, VarianteVidrioLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, VarianteVidrioLectura>>(ids.Where(i => i == Variante)
                .ToDictionary(i => i, i => new VarianteVidrioLectura(i, "FL6-2440x3660", 2440m, 3660m, 6m)));
        public Task<IReadOnlyList<VarianteVidrioLectura>> ListarPorArticuloAsync(Guid articuloId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Articulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
