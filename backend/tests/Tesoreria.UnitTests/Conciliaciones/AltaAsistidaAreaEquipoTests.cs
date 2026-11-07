using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Conciliaciones;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

/// <summary>
/// H1 paso 3 (86e3z5w25): en la alta desde el extracto el usuario captura el área (y el equipo) cuando la cuenta del concepto los
/// exige (regla C-03). Contabilidad los valida con la sucursal de la cuenta bancaria; el centro va solo en el evento de la base.
/// Un dato inválido rechaza la alta; sin dato la alta procede (la póliza queda en Rechazos). Datos ficticios.
/// </summary>
public sealed class AltaAsistidaAreaEquipoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Usuario = Guid.NewGuid(), Sucursal = Guid.NewGuid();
    private static readonly Guid Area = Guid.NewGuid(), Equipo = Guid.NewGuid();
    private static readonly Guid ComisionId = Guid.Parse("0000000b-1001-0000-0000-000000000004");
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);

    private static TesoreriaDbContext Db() => new(
        new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestEmpresaContext());

    /// <summary>Cuenta con una línea de comisión de 116 en el extracto; con <paramref name="conSucursal"/>, un depósito previo le da sucursal.</summary>
    private static async Task<(Conciliacion Conciliacion, ExtractoLinea Linea)> PrepararAsync(TesoreriaDbContext db, bool conSucursal)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0123456789", null, "MXN");
        cuenta.RegistrarSaldoInicial(10_000m, new DateOnly(2026, 10, 31), "Saldo apertura");
        db.CuentasBancarias.Add(cuenta);
        foreach (var (id, nombre) in new[] { (ComisionId, "Comisión bancaria"), (AltaAsistidaDesdeExtractoHandler.ConceptoImpuestosId, "Impuestos") })
        {
            var concepto = new ConceptoMovimiento(nombre, ClasificacionFlujo.Operacion);
            typeof(ConceptoMovimiento).GetProperty("Id")!.SetValue(concepto, id);
            db.ConceptosMovimiento.Add(concepto);
        }
        if (conSucursal)
            db.MovimientosBancarios.Add(MovimientoBancario.RegistrarMovimientoConciliacion(Empresa, cuenta, SentidoMovimiento.Ingreso, 700m,
                new DateOnly(2026, 11, 2), "DEP-FIX", ComisionId, Usuario, Ahora, Sucursal));
        var conciliacion = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 11, 10_000m);
        var linea = ExtractoLinea.Crear(conciliacion.Id, 1, new DateOnly(2026, 11, 6), "COMISION SPEI", "COM-FIX", cargo: 116m, abono: null);
        conciliacion.CargarLineas([linea], 10_000m);
        db.Conciliaciones.Add(conciliacion);
        await db.SaveChangesAsync();
        return (conciliacion, linea);
    }

    private static AltaAsistidaDesdeExtractoCommand Alta(Conciliacion c, ExtractoLinea l, Guid? area, Guid? equipo) =>
        new(c.Id, l.Id, ComisionId, IncluyeIva: true, MontoBase: 100m, MontoIva: 16m, AreaId: area, EquipoId: equipo);

    [Fact]
    public async Task El_equipo_validado_por_Contabilidad_va_en_el_evento_de_la_base_y_no_en_el_del_IVA()
    {
        await using var db = Db();
        var (c, l) = await PrepararAsync(db, conSucursal: true);
        var publisher = new Publisher();
        var puerto = new PuertoFalso();

        await new AltaAsistidaDesdeExtractoHandler(db, new TestUser(Usuario), new TestClock(), publisher, puerto).Handle(Alta(c, l, Area, Equipo), default);

        puerto.Recibido.Should().Match<MovimientoCaptura>(m => m.EmpresaId == Empresa && m.SucursalId == Sucursal && m.Campo == "Base"
            && m.Clasificador == "Comisión bancaria" && m.TipoEvento == MovimientoBancarioRegistradoIntegrationEvent.WireEventType);
        var eventos = publisher.Eventos.OfType<MovimientoBancarioRegistradoIntegrationEvent>().ToList();
        eventos.Should().HaveCount(2);
        eventos.Single(x => x.MovimientoBaseId is null).CentroCostoId.Should().Be(Equipo);
        eventos.Single(x => x.MovimientoBaseId is not null).CentroCostoId.Should().BeNull();
    }

    [Fact]
    public async Task Un_area_invalida_rechaza_la_alta_sin_movimientos()
    {
        await using var db = Db();
        var (c, l) = await PrepararAsync(db, conSucursal: true);
        var publisher = new Publisher();

        var act = () => new AltaAsistidaDesdeExtractoHandler(db, new TestUser(Usuario), new TestClock(), publisher,
            new PuertoFalso("El área elegida no es de la sucursal del movimiento.")).Handle(Alta(c, l, Area, null), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("TES_DIMENSION_INVALIDA");
        publisher.Eventos.Should().BeEmpty();
        (await db.MovimientosBancarios.CountAsync()).Should().Be(1, "solo el depósito previo");
    }

    [Fact]
    public async Task Sin_area_la_alta_procede_sin_centro_y_sin_consultar_a_Contabilidad()
    {
        await using var db = Db();
        var (c, l) = await PrepararAsync(db, conSucursal: false);
        var publisher = new Publisher();
        var puerto = new PuertoFalso();

        await new AltaAsistidaDesdeExtractoHandler(db, new TestUser(Usuario), new TestClock(), publisher, puerto).Handle(Alta(c, l, null, null), default);

        puerto.Recibido.Should().BeNull();
        publisher.Eventos.OfType<MovimientoBancarioRegistradoIntegrationEvent>().Should().OnlyContain(x => x.CentroCostoId == null);
    }

    [Fact]
    public async Task Con_area_pero_sin_sucursal_conocida_la_alta_se_rechaza()
    {
        await using var db = Db();
        var (c, l) = await PrepararAsync(db, conSucursal: false);

        var act = () => new AltaAsistidaDesdeExtractoHandler(db, new TestUser(Usuario), new TestClock(), new Publisher(), new PuertoFalso())
            .Handle(Alta(c, l, Area, null), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("TES_DIMENSION_SIN_SUCURSAL");
    }

    private sealed class TestEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class TestUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "UsuarioPrueba";
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class Publisher : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>Contabilidad: acepta (equipo si viene; si no, el área) o devuelve el error dado.</summary>
    private sealed class PuertoFalso(string? error = null) : IDimensionesCapturaPort
    {
        public MovimientoCaptura? Recibido { get; private set; }

        public Task<ExigenciaDimensiones> ExigenciasAsync(MovimientoCaptura movimiento, CancellationToken ct) =>
            Task.FromResult(new ExigenciaDimensiones(true, false, "601.FIX"));

        public Task<IReadOnlyList<CentroOpcionDto>> CentrosAsync(Guid empresaId, Guid sucursalId, bool equipos, Guid? areaId, string? q, int limite,
            CancellationToken ct) => Task.FromResult<IReadOnlyList<CentroOpcionDto>>([]);

        public Task<DimensionesCapturaResultado> ValidarAsync(MovimientoCaptura movimiento, Guid? areaId, Guid? equipoId, CancellationToken ct)
        {
            Recibido = movimiento;
            var exigencia = new ExigenciaDimensiones(true, false, "601.FIX");
            return Task.FromResult(error is null
                ? new DimensionesCapturaResultado(null, null, equipoId ?? areaId, exigencia)
                : new DimensionesCapturaResultado(error, null, null, exigencia));
        }
    }
}
