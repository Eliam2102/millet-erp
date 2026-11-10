using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Conciliaciones;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

/// <summary>86e3ke6yn: la importación del extracto confirma sola, manda a revisión o deja sin identificar.</summary>
public sealed class ImportacionDepositoAutomaticoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Importador = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    private const string Csv =
        "FECHA,CONCEPTO,REFERENCIA,CARGO,ABONO,SALDO\n" +
        "01/09/2026,SPEI RECIBIDO CLIENTE A,REF-A,,48500.00,48500.00\n" +
        "02/09/2026,CHEQUE DEPOSITADO CLIENTE B,REF-B,,250000.00,298500.00\n" +
        "03/09/2026,SPEI RECIBIDO CLIENTE C,REF-C,,10000.00,308500.00\n" +
        "04/09/2026,SPEI RECIBIDO CLIENTE D,REF-D,,7000.00,315500.00\n" +
        "05/09/2026,SPEI SIN PROPUESTA,REF-X,,3000.00,318500.00\n";

    private sealed class Escenario(TesoreriaDbContext db, Conciliacion conc, DepositoConfirmacion a,
        DepositoConfirmacion b, DepositoConfirmacion c, DepositoConfirmacion d1, DepositoConfirmacion d2) : IAsyncDisposable
    {
        public TesoreriaDbContext Db { get; } = db;
        public Conciliacion Conc { get; } = conc;
        public FakePublisher Publisher { get; } = new();
        public DepositoConfirmacion A { get; } = a;
        public DepositoConfirmacion B { get; } = b;
        public DepositoConfirmacion C { get; } = c;
        public DepositoConfirmacion D1 { get; } = d1;
        public DepositoConfirmacion D2 { get; } = d2;
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static string Facturas(decimal importe, decimal? saldo = 0m) =>
        JsonSerializer.Serialize(new[] { new PropuestaFacturaPayload(Guid.NewGuid(), "VEN-1", importe, saldo) });

    private static DepositoConfirmacion Propuesta(string referencia, decimal monto, string? facturas = null) =>
        DepositoConfirmacion.CrearDesdePropuesta(Empresa, Guid.NewGuid(), Guid.NewGuid(), referencia, monto, "MXN",
            facturas ?? Facturas(monto), Proponente);

    private static async Task<Escenario> CrearAsync()
    {
        var db = new TesoreriaDbContext(
            new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TestEmpresa());
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0123456789", null, "MXN");
        cuenta.RegistrarSaldoInicial(0m, new DateOnly(2026, 8, 31), "Apertura");
        db.CuentasBancarias.Add(cuenta);
        var conc = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 9);
        db.Conciliaciones.Add(conc);

        var a = Propuesta("REF-A", 48_500m);
        var b = Propuesta("REF-B", 250_000m);
        var c = Propuesta("REF-C", 10_000m, facturas: "[]");
        var d1 = Propuesta("REF-D", 7_000m);
        var d2 = Propuesta("REF-D", 7_000m);
        db.DepositosConfirmacion.AddRange(a, b, c, d1, d2);
        await db.SaveChangesAsync();
        return new Escenario(db, conc, a, b, c, d1, d2);
    }

    private static Task<ConciliacionResumenResponse> Importar(
        Escenario e, Guid conciliacionId, bool puedeConfirmar = true, string contenido = Csv, string? formato = null)
    {
        var user = new TestUser(Importador);
        var clock = new TestClock();
        var procesador = new DepositoAutomaticoProcesador(e.Db, e.Publisher, clock, user, new TestEmpresa(),
            new FakePermisos(puedeConfirmar), new FakePeriodos(), new FakeSucursales(),
            Options.Create(new DepositoAutomaticoOptions { PalabrasCheque = ["CH ", "CHEQUE"] }));
        var handler = new CargarExtractoHandler(e.Db, user, clock, new ExtractoParserFactory([new BbvaExtractoParser()]), procesador);
        return handler.Handle(new CargarExtractoCommand(conciliacionId, contenido, formato), default);
    }

    // ---- M-190: auxiliar Santander 8138 con el formato real (datos anonimizados) ----

    private static async Task<Conciliacion> ConciliacionSantanderAsync(Escenario e)
    {
        var cuenta = new CuentaBancaria(Empresa, "SANTANDER", "65500000001", null, "MXN", perfilExtracto: "SANTANDER");
        var conc = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 10);
        e.Db.CuentasBancarias.Add(cuenta);
        e.Db.Conciliaciones.Add(conc);
        await e.Db.SaveChangesAsync();
        return conc;
    }

    [Fact]
    public async Task Santander_con_el_perfil_de_la_cuenta_carga_266_y_al_reimportar_son_266_duplicados()
    {
        await using var e = await CrearAsync();
        var conc = await ConciliacionSantanderAsync(e);

        var primera = await Importar(e, conc.Id, contenido: ExtractosFixtures.Santander());
        var segunda = await Importar(e, conc.Id, contenido: ExtractosFixtures.Santander());

        primera.LineasNuevas.Should().Be(266);
        segunda.LineasNuevas.Should().Be(0);
        segunda.LineasDuplicadas.Should().Be(266);
        (await e.Db.ExtractoLineas.CountAsync(l => l.ConciliacionId == conc.Id)).Should().Be(266);
        (await e.Db.Conciliaciones.SingleAsync(c => c.Id == conc.Id)).SaldoExtracto.Should().Be(ExtractosFixtures.SaldoFinalSantander);
    }

    [Fact]
    public async Task Santander_cargo_con_misma_referencia_e_importe_en_libros_se_empareja_solo_y_el_resto_queda_pendiente()
    {
        await using var e = await CrearAsync();
        var conc = await ConciliacionSantanderAsync(e);
        var cuenta = await e.Db.CuentasBancarias.SingleAsync(c => c.Id == conc.CuentaBancariaId);
        var libros = MovimientoBancario.RegistrarPagoProveedor(Empresa, cuenta, Guid.NewGuid(),
            ExtractosFixtures.ImporteSantander(2), new DateOnly(2026, 10, 1), ExtractosFixtures.ReferenciaSantander(2), null, Importador, Ahora);
        e.Db.MovimientosBancarios.Add(libros);
        await e.Db.SaveChangesAsync();

        await Importar(e, conc.Id, contenido: ExtractosFixtures.Santander());

        var lineas = await e.Db.ExtractoLineas.Where(l => l.ConciliacionId == conc.Id).ToListAsync();
        var cargo = lineas.Single(l => l.Referencia == ExtractosFixtures.ReferenciaSantander(2));
        cargo.Estado.Should().Be(EstadoExtractoLinea.Emparejada);
        cargo.MatchMovimientoId.Should().Be(libros.Id);
        lineas.Where(l => l.Cargo.HasValue && l.Id != cargo.Id).Should().OnlyContain(l => l.Estado == EstadoExtractoLinea.Pendiente);
    }

    [Fact]
    public async Task Archivo_que_no_es_santander_en_cuenta_santander_se_rechaza_sin_carga_parcial()
    {
        await using var e = await CrearAsync();
        var conc = await ConciliacionSantanderAsync(e);

        var act = () => Importar(e, conc.Id, contenido: ExtractosFixtures.BbvaAuxiliar(1_000m, out _));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("Formato no reconocido para el perfil SANTANDER");
        (await e.Db.ExtractoLineas.CountAsync(l => l.ConciliacionId == conc.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Importar_confirma_una_manda_tres_a_revision_y_deja_una_sin_identificar()
    {
        await using var e = await CrearAsync();
        var res = await Importar(e, e.Conc.Id);

        res.Depositos.Should().Be(new ResumenDepositosAutomaticos(1, 3, 1));

        var a = await e.Db.DepositosConfirmacion.SingleAsync(d => d.Id == e.A.Id);
        a.Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        a.ConfirmacionAutomatica.Should().BeTrue();
        a.ReglaAutomatica.Should().NotBeNullOrWhiteSpace();
        a.ExtractoLineaId.Should().NotBeNull();
        a.ResueltaPor.Should().Be(Importador);

        e.Publisher.Eventos.OfType<PagoClienteConfirmadoIntegrationEvent>().Should().ContainSingle()
            .Which.PropuestaId.Should().Be(e.A.PropuestaCxcId);

        (await e.Db.DepositosConfirmacion.SingleAsync(d => d.Id == e.B.Id)).MotivoRevision.Should().Contain("Cheque");
        (await e.Db.DepositosConfirmacion.SingleAsync(d => d.Id == e.C.Id)).MotivoRevision.Should().Contain("Anticipo");
        foreach (var d in new[] { e.D1, e.D2 })
        {
            var fila = await e.Db.DepositosConfirmacion.SingleAsync(x => x.Id == d.Id);
            fila.Estado.Should().Be(EstadoDepositoConfirmacion.Pendiente);
            fila.MotivoRevision.Should().Contain("2 propuestas");
            fila.ExtractoLineaId.Should().NotBeNull();
        }

        var bitacora = await e.Db.BitacorasConciliacion.Where(b => b.Accion == "Regla automática").ToListAsync();
        bitacora.Should().ContainSingle().Which.Motivo.Should()
            .Contain(e.A.PropuestaCxcId!.Value.ToString()).And.Contain("VEN-1");

        // Ingresos creados: el de la propuesta (con sucursal) y el sin identificar (tipo Cliente).
        var movs = await e.Db.MovimientosBancarios.ToListAsync();
        movs.Should().HaveCount(2);
        movs.Single(m => m.Monto == 48_500m).SucursalId.Should().Be(Sucursal);
        movs.Single(m => m.Monto == 3_000m).BeneficiarioTipo.Should().Be(BeneficiarioTipo.Cliente);
    }

    [Fact]
    public async Task Reimportar_el_mismo_archivo_no_cambia_nada()
    {
        await using var e = await CrearAsync();
        await Importar(e, e.Conc.Id);
        var res = await Importar(e, e.Conc.Id);

        res.TotalLineas.Should().Be(5);
        res.LineasNuevas.Should().Be(0);
        res.LineasDuplicadas.Should().Be(5);
        (await e.Db.ExtractoLineas.MaxAsync(l => l.NumeroLinea)).Should().Be(5); // reimportar no consume números
        res.Depositos.Should().Be(new ResumenDepositosAutomaticos(0, 0, 0));
        e.Publisher.Eventos.OfType<PagoClienteConfirmadoIntegrationEvent>().Should().ContainSingle();
        (await e.Db.MovimientosBancarios.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Misma_linea_en_otra_conciliacion_de_la_cuenta_se_omite()
    {
        await using var e = await CrearAsync();
        await Importar(e, e.Conc.Id);
        var otra = Conciliacion.Crear(Empresa, e.Conc.CuentaBancariaId, 2026, 10);
        e.Db.Conciliaciones.Add(otra);
        await e.Db.SaveChangesAsync();

        var res = await Importar(e, otra.Id);

        res.TotalLineas.Should().Be(0);
        res.LineasDuplicadas.Should().Be(5);
        e.Publisher.Eventos.OfType<PagoClienteConfirmadoIntegrationEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Sin_permiso_de_confirmar_la_propuesta_va_a_revision()
    {
        await using var e = await CrearAsync();
        var res = await Importar(e, e.Conc.Id, puedeConfirmar: false);

        res.Depositos!.AutoConfirmados.Should().Be(0);
        (await e.Db.DepositosConfirmacion.SingleAsync(d => d.Id == e.A.Id)).MotivoRevision.Should().Contain("permiso");
        e.Publisher.Eventos.Should().BeEmpty();
    }

    private sealed class TestEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class TestUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "Importador";
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePermisos(bool permitido) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(permitido);
    }

    private sealed class FakePeriodos : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FakeSucursales : ITesoreriaSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentoSucursales>>([]);

        public Task<DocumentoSucursales?> ObtenerAsync(string tipo, Guid id, CancellationToken ct) =>
            Task.FromResult<DocumentoSucursales?>(new DocumentoSucursales(id, [Sucursal]));
    }
}
