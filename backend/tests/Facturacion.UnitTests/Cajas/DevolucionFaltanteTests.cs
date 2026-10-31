using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// 86e3vr1tj (junta 19-oct): el cajero devuelve el faltante de un corte anterior como depósito tipo
/// «Devolución de faltante» en Mi caja; obliga el corte de origen y publica el evento que Tesorería abona al deudor.
/// </summary>
public sealed class DevolucionFaltanteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid Otro = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 28, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    private static RegistrarCajaMovimientoHandler Movimientos(
        FacturacionDbContext db, FakeIntegrationEventPublisher eventos, Guid? usuario = null) =>
        new(db, new FakeEmpresaContext(Empresa), new FakeUserContext(usuario ?? Cajero), new FakeClock(Ahora),
            new FakeSucursalesReadPort(), new FakeCatalogosSatReadPort(), eventos);

    /// <summary>Abre una sesión con fondo 500, la cierra declarando <paramref name="declarado"/> y devuelve su Id.</summary>
    private static async Task<Guid> CorteCerrado(FacturacionDbContext db, Caja caja, decimal declarado, Guid? usuario = null)
    {
        var abrir = await new AbrirCajaSesionHandler(db, new FakeEmpresaContext(Empresa), new FakeUserContext(usuario ?? Cajero),
                new FakeClock(Ahora), new FakeSucursalesReadPort(), new FakeIntegrationEventPublisher())
            .Handle(new AbrirCajaSesionCommand(caja.Id, SucursalId, 500m), CancellationToken.None);
        var sesion = await db.CajaSesiones.SingleAsync(s => s.Id == abrir.Id);
        await new IniciarArqueoCajaSesionHandler(db, new FakeUserContext(usuario ?? Cajero))
            .Handle(new IniciarArqueoCajaSesionCommand(sesion.Id, sesion.Version), CancellationToken.None);
        await new CerrarCajaSesionHandler(db, new FakeClock(Ahora), new FakeSucursalesReadPort(), new FakeIntegrationEventPublisher())
            .Handle(new CerrarCajaSesionCommand(sesion.Id, sesion.Version, declarado), CancellationToken.None);
        return sesion.Id;
    }

    private static async Task<Guid> SesionAbierta(FacturacionDbContext db, Caja caja) =>
        (await new AbrirCajaSesionHandler(db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero),
                new FakeClock(Ahora), new FakeSucursalesReadPort(), new FakeIntegrationEventPublisher())
            .Handle(new AbrirCajaSesionCommand(caja.Id, SucursalId, 0m), CancellationToken.None)).Id;

    private static async Task<Caja> NuevaCaja(FacturacionDbContext db)
    {
        var caja = Caja.Crear(Empresa, "Caja Mostrador", null);
        caja.ReemplazarSucursales([SucursalId]);
        caja.ReemplazarUsuarios([Cajero, Otro]);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        return caja;
    }

    private static RegistrarCajaMovimientoCommand Devolucion(Guid sesion, Guid? origen, decimal importe, string? descripcion = null) =>
        new(sesion, TipoCajaMovimiento.DevolucionFaltante, "01", importe, descripcion ?? string.Empty,
            CorteOrigenSesionId: origen);

    // CA5 (lado Facturación): $50 de faltante del corte del 28-oct; el cajero deposita $50 como devolución.
    [Fact]
    public async Task Devolucion_total_registra_movimiento_y_publica_el_evento()
    {
        using var db = NewDb();
        var caja = await NuevaCaja(db);
        var origen = await CorteCerrado(db, caja, declarado: 450m); // fondo 500 → faltante 50
        (await db.CajaSesiones.SingleAsync()).Diferencia.Should().Be(-50m);
        var actual = await SesionAbierta(db, caja);
        var eventos = new FakeIntegrationEventPublisher();

        var r = await Movimientos(db, eventos).Handle(Devolucion(actual, origen, 50m), CancellationToken.None);

        r.Importe.Should().Be(50m);
        var mov = await db.CajaMovimientos.SingleAsync(m => m.Id == r.Id);
        mov.Tipo.Should().Be(TipoCajaMovimiento.DevolucionFaltante);
        mov.CorteOrigenSesionId.Should().Be(origen);
        mov.Descripcion.Should().Be("Devolución del corte del 28/10/2026");
        var ev = eventos.Publicados.Should().ContainSingle().Which.Should().BeOfType<CajaFaltanteDevueltoIntegrationEvent>().Subject;
        ev.Importe.Should().Be(50m);
        ev.CorteOrigenSesionId.Should().Be(origen);
        ev.CajaSesionId.Should().Be(actual);
        ev.CajeroId.Should().Be(Cajero);
        ev.CajaMovimientoId.Should().Be(mov.Id);
    }

    [Fact]
    public async Task Devolucion_sin_corte_de_origen_se_rechaza()
    {
        using var db = NewDb();
        var caja = await NuevaCaja(db);
        await CorteCerrado(db, caja, 450m);
        var actual = await SesionAbierta(db, caja);

        var act = () => Movimientos(db, new FakeIntegrationEventPublisher()).Handle(Devolucion(actual, null, 50m), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "MOVIMIENTO_CORTE_ORIGEN_REQUERIDO");
        var validacion = new RegistrarCajaMovimientoValidator().Validate(Devolucion(actual, null, 50m));
        validacion.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Devolucion_parcial_se_acumula_y_no_puede_exceder_el_faltante()
    {
        using var db = NewDb();
        var caja = await NuevaCaja(db);
        var origen = await CorteCerrado(db, caja, 450m);
        var actual = await SesionAbierta(db, caja);
        var eventos = new FakeIntegrationEventPublisher();

        await Movimientos(db, eventos).Handle(Devolucion(actual, origen, 20m), CancellationToken.None);
        var exceso = () => Movimientos(db, eventos).Handle(Devolucion(actual, origen, 31m), CancellationToken.None);
        await exceso.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEVOLUCION_EXCEDE_FALTANTE");
        await Movimientos(db, eventos).Handle(Devolucion(actual, origen, 30m), CancellationToken.None);

        eventos.Publicados.Should().HaveCount(2);
        var pendientes = await new FaltantesPorDevolverHandler(db, new FakeUserContext(Cajero))
            .Handle(new FaltantesPorDevolverQuery(), CancellationToken.None);
        pendientes.Should().BeEmpty();
    }

    [Fact]
    public async Task Corte_sin_faltante_o_de_otro_cajero_no_admite_devolucion()
    {
        using var db = NewDb();
        var caja = await NuevaCaja(db);
        var sinFaltante = await CorteCerrado(db, caja, declarado: 500m); // diferencia 0
        var actual = await SesionAbierta(db, caja);

        var act = () => Movimientos(db, new FakeIntegrationEventPublisher()).Handle(Devolucion(actual, sinFaltante, 10m), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORTE_ORIGEN_SIN_FALTANTE");

        var ajeno = () => Movimientos(db, new FakeIntegrationEventPublisher(), Otro).Handle(Devolucion(actual, sinFaltante, 10m), CancellationToken.None);
        await ajeno.Should().ThrowAsync<ForbiddenException>(); // la sesión actual es de Cajero
    }

    [Fact]
    public async Task Faltantes_por_devolver_lista_solo_lo_pendiente_del_cajero()
    {
        using var db = NewDb();
        var caja = await NuevaCaja(db);
        var origen = await CorteCerrado(db, caja, 450m);

        var lista = await new FaltantesPorDevolverHandler(db, new FakeUserContext(Cajero))
            .Handle(new FaltantesPorDevolverQuery(), CancellationToken.None);
        var item = lista.Should().ContainSingle().Subject;
        item.CorteSesionId.Should().Be(origen);
        item.Pendiente.Should().Be(50m);

        (await new FaltantesPorDevolverHandler(db, new FakeUserContext(Otro)).Handle(new FaltantesPorDevolverQuery(), CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public void Solo_la_devolucion_de_faltante_lleva_corte_de_origen_y_solo_en_efectivo()
    {
        var sesion = Guid.NewGuid();
        var conOrigen = () => CajaMovimiento.Crear(Empresa, sesion, TipoCajaMovimiento.Deposito, "01", 10m, Cajero, "Dep",
            corteOrigenSesionId: Guid.NewGuid());
        conOrigen.Should().Throw<BusinessRuleException>().Where(e => e.Code == "MOVIMIENTO_CORTE_ORIGEN_NO_APLICA");

        var transferencia = () => CajaMovimiento.Crear(Empresa, sesion, TipoCajaMovimiento.DevolucionFaltante, "03", 10m, Cajero, "Dev",
            corteOrigenSesionId: Guid.NewGuid());
        transferencia.Should().Throw<BusinessRuleException>().Where(e => e.Code == "MOVIMIENTO_DEVOLUCION_SOLO_EFECTIVO");
    }
}
