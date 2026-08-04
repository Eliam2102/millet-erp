using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// Capa B de Cajas (CAJAS-PR3, 12-cajas.md §5): FSM de la sesión de efectivo,
/// autorización consumible, drenado de ajustes pendientes, día de operación
/// por zona horaria y bloqueo de día anterior.
/// </summary>
public sealed class CajaSesionesTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();

    // 18:00 UTC = 12:00 en Mérida (UTC-6): pleno día de operación.
    private static readonly DateTimeOffset Ahora = new(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    private static Caja CajaConCajero(params Guid[] usuarios)
    {
        var caja = Caja.Crear(Empresa, "Caja Mostrador", null);
        caja.ReemplazarSucursales([SucursalId]);
        if (usuarios.Length > 0) caja.ReemplazarUsuarios(usuarios);
        return caja;
    }

    private static AbrirCajaSesionHandler AbrirHandler(
        FacturacionDbContext db,
        DateTimeOffset? ahora = null,
        Guid? usuario = null,
        FakeIntegrationEventPublisher? eventos = null) =>
        new(db, new FakeEmpresaContext(Empresa), new FakeUserContext(usuario ?? Cajero),
            new FakeClock(ahora ?? Ahora), new FakeSucursalesReadPort(), eventos ?? new FakeIntegrationEventPublisher());

    private static async Task<CajaSesionMutadaResponse> AbrirSesionAsync(
        FacturacionDbContext db, Guid cajaId, decimal fondo = 500m,
        FakeIntegrationEventPublisher? eventos = null) =>
        await AbrirHandler(db, eventos: eventos)
            .Handle(new AbrirCajaSesionCommand(cajaId, SucursalId, fondo), CancellationToken.None);

    // ---- Apertura ----

    [Fact]
    public async Task Abrir_crea_sesion_con_fondo_y_dia_de_operacion_local()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();

        var eventos = new FakeIntegrationEventPublisher();
        var response = await AbrirSesionAsync(db, caja.Id, eventos: eventos);

        response.Estado.Should().Be("Abierta");
        response.DiaOperacion.Should().Be(new DateOnly(2026, 7, 10)); // 12:00 local Mérida

        var sesion = await db.CajaSesiones.SingleAsync();
        sesion.ResponsableUsuarioId.Should().Be(Cajero);
        sesion.FondoApertura.Should().Be(500m);

        var fondo = await db.CajaMovimientos.SingleAsync();
        fondo.Tipo.Should().Be(TipoCajaMovimiento.FondoApertura);
        fondo.FormaPago.Should().Be("01");
        fondo.Importe.Should().Be(500m);

        eventos.Publicados.Should().ContainSingle().Which.Should().BeOfType<CajaSesionAbiertaIntegrationEvent>();
    }

    [Fact]
    public async Task Abrir_caja_ajena_exige_autorizacion_y_la_consume()
    {
        using var db = NewDb();
        var caja = CajaConCajero(); // sin usuarios relacionados
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();

        var sinAutorizacion = () => AbrirSesionAsync(db, caja.Id);
        await sinAutorizacion.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "SESION_AUTORIZACION_REQUERIDA");

        var autorizacion = AutorizacionAperturaCaja.Crear(
            Empresa, caja.Id, Cajero, Guid.NewGuid(), "Cubre ausencia", Ahora, TimeSpan.FromMinutes(30));
        db.AutorizacionesAperturaCaja.Add(autorizacion);
        await db.SaveChangesAsync();

        var response = await AbrirHandler(db).Handle(
            new AbrirCajaSesionCommand(caja.Id, SucursalId, 100m, autorizacion.Id), CancellationToken.None);

        response.Estado.Should().Be("Abierta");
        autorizacion.Estado.Should().Be(EstadoAutorizacionAperturaCaja.Usada);
        autorizacion.CajaSesionId.Should().Be(response.Id);
    }

    [Fact]
    public async Task Abrir_con_autorizacion_vencida_falla()
    {
        using var db = NewDb();
        var caja = CajaConCajero();
        var autorizacion = AutorizacionAperturaCaja.Crear(
            Empresa, caja.Id, Cajero, Guid.NewGuid(), "Cubre ausencia",
            Ahora.AddHours(-2), TimeSpan.FromMinutes(30));
        db.Cajas.Add(caja);
        db.AutorizacionesAperturaCaja.Add(autorizacion);
        await db.SaveChangesAsync();

        var act = () => AbrirHandler(db).Handle(
            new AbrirCajaSesionCommand(caja.Id, SucursalId, 100m, autorizacion.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_VENCIDA");
    }

    [Fact]
    public async Task Abrir_con_sesion_no_cerrada_de_la_caja_responde_409()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        await AbrirSesionAsync(db, caja.Id);

        var otroCajero = Guid.NewGuid();
        caja.ReemplazarUsuarios([Cajero, otroCajero]);
        await db.SaveChangesAsync();

        var act = () => AbrirHandler(db, usuario: otroCajero)
            .Handle(new AbrirCajaSesionCommand(caja.Id, SucursalId, 0m), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "SESION_CAJA_YA_ABIERTA");
    }

    [Fact]
    public async Task Abrir_drena_los_ajustes_pendientes_de_la_caja()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        var ajuste = CajaAjustePendiente.Crear(
            Empresa, caja.Id, Guid.NewGuid(), -150m, "01", "Cancelación de cobro C-1", Guid.NewGuid());
        db.Cajas.Add(caja);
        db.CajaAjustesPendientes.Add(ajuste);
        await db.SaveChangesAsync();

        var response = await AbrirSesionAsync(db, caja.Id, fondo: 500m);

        ajuste.AplicadoEnSesionId.Should().Be(response.Id);
        var movimientos = await db.CajaMovimientos.ToListAsync();
        movimientos.Should().HaveCount(2); // fondo + ajuste
        movimientos.Single(m => m.Tipo == TipoCajaMovimiento.AjusteCorreccion).Importe.Should().Be(-150m);
    }

    [Fact]
    public async Task Abrir_con_sesion_de_dia_anterior_pendiente_se_bloquea()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        await AbrirSesionAsync(db, caja.Id); // día 2026-07-10

        var otraCaja = CajaConCajero(Cajero);
        otraCaja.Actualizar("Caja 2", null);
        db.Cajas.Add(otraCaja);
        await db.SaveChangesAsync();

        // Al día siguiente, con la sesión del 10 sin cerrar:
        var act = () => AbrirHandler(db, ahora: Ahora.AddDays(1))
            .Handle(new AbrirCajaSesionCommand(otraCaja.Id, SucursalId, 0m), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "SESION_DIA_ANTERIOR_PENDIENTE");
    }

    // ---- Movimientos manuales ----

    [Fact]
    public async Task Movimiento_manual_aplica_signo_y_exige_responsable()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        var sesion = await AbrirSesionAsync(db, caja.Id);

        var handler = new RegistrarCajaMovimientoHandler(
            db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero),
            new FakeClock(Ahora), new FakeSucursalesReadPort());

        var retiro = await handler.Handle(new RegistrarCajaMovimientoCommand(
            sesion.Id, TipoCajaMovimiento.Retiro, "01", 200m, "Retiro parcial"), CancellationToken.None);
        retiro.Importe.Should().Be(-200m);

        var ajeno = new RegistrarCajaMovimientoHandler(
            db, new FakeEmpresaContext(Empresa), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), new FakeSucursalesReadPort());
        var act = () => ajeno.Handle(new RegistrarCajaMovimientoCommand(
            sesion.Id, TipoCajaMovimiento.Deposito, "01", 50m, "Depósito"), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "SESION_RESPONSABLE_DISTINTO");
    }

    [Fact]
    public async Task Movimiento_en_sesion_de_dia_anterior_se_bloquea()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        var sesion = await AbrirSesionAsync(db, caja.Id);

        var handler = new RegistrarCajaMovimientoHandler(
            db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero),
            new FakeClock(Ahora.AddDays(1)), new FakeSucursalesReadPort());

        var act = () => handler.Handle(new RegistrarCajaMovimientoCommand(
            sesion.Id, TipoCajaMovimiento.Deposito, "01", 50m, "Depósito tardío"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SESION_DIA_ANTERIOR");
    }

    // ---- Arqueo y cierre ----

    [Fact]
    public async Task Arqueo_y_cierre_congelan_cortes_y_diferencia_de_efectivo()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        var abierta = await AbrirSesionAsync(db, caja.Id, fondo: 500m);

        var movimientos = new RegistrarCajaMovimientoHandler(
            db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero),
            new FakeClock(Ahora), new FakeSucursalesReadPort());
        await movimientos.Handle(new RegistrarCajaMovimientoCommand(
            abierta.Id, TipoCajaMovimiento.Deposito, "03", 1000m, "Transferencia recibida"), CancellationToken.None);
        await movimientos.Handle(new RegistrarCajaMovimientoCommand(
            abierta.Id, TipoCajaMovimiento.Retiro, "01", 100m, "Retiro parcial"), CancellationToken.None);

        // OJO: el interceptor de Version no corre bajo InMemory; se lee la
        // versión real en vez de asumir incrementos.
        var version = (await db.CajaSesiones.AsNoTracking().SingleAsync()).Version;
        var arqueo = await new IniciarArqueoCajaSesionHandler(db, new FakeUserContext(Cajero))
            .Handle(new IniciarArqueoCajaSesionCommand(abierta.Id, version), CancellationToken.None);
        arqueo.Estado.Should().Be("EnArqueo");

        var sesion = await db.CajaSesiones.Include(s => s.Cortes).SingleAsync();
        sesion.Cortes.Should().HaveCount(2);
        sesion.Cortes.Single(c => c.FormaPago == "01").MontoSistema.Should().Be(400m);  // 500 − 100
        sesion.Cortes.Single(c => c.FormaPago == "03").MontoSistema.Should().Be(1000m);

        var eventos = new FakeIntegrationEventPublisher();
        var cierre = await new CerrarCajaSesionHandler(db, new FakeClock(Ahora), new FakeSucursalesReadPort(), eventos)
            .Handle(new CerrarCajaSesionCommand(abierta.Id, sesion.Version, EfectivoDeclarado: 380m, "faltante"), CancellationToken.None);

        cierre.Estado.Should().Be("Cerrada");
        sesion.EfectivoTeorico.Should().Be(400m);
        sesion.Diferencia.Should().Be(-20m);
        sesion.CierreExtemporaneo.Should().BeFalse();

        var evento = eventos.Publicados.Should().ContainSingle()
            .Which.Should().BeOfType<CajaSesionCerradaIntegrationEvent>().Subject;
        evento.Diferencia.Should().Be(-20m);
        evento.Cortes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cierre_al_dia_siguiente_queda_extemporaneo()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        var abierta = await AbrirSesionAsync(db, caja.Id, fondo: 0m);

        var version = (await db.CajaSesiones.AsNoTracking().SingleAsync()).Version;
        await new IniciarArqueoCajaSesionHandler(db, new FakeUserContext(Cajero))
            .Handle(new IniciarArqueoCajaSesionCommand(abierta.Id, version), CancellationToken.None);
        var sesion = await db.CajaSesiones.SingleAsync();

        await new CerrarCajaSesionHandler(
                db, new FakeClock(Ahora.AddDays(1)), new FakeSucursalesReadPort(), new FakeIntegrationEventPublisher())
            .Handle(new CerrarCajaSesionCommand(abierta.Id, sesion.Version, 0m), CancellationToken.None);

        sesion.CierreExtemporaneo.Should().BeTrue();
        sesion.Estado.Should().Be(EstadoCajaSesion.Cerrada);
    }

    [Fact]
    public async Task Reabrir_devuelve_EnArqueo_a_Abierta_y_el_arqueo_recalcula()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        var abierta = await AbrirSesionAsync(db, caja.Id, fondo: 100m);

        var version = (await db.CajaSesiones.AsNoTracking().SingleAsync()).Version;
        await new IniciarArqueoCajaSesionHandler(db, new FakeUserContext(Cajero))
            .Handle(new IniciarArqueoCajaSesionCommand(abierta.Id, version), CancellationToken.None);
        var sesion = await db.CajaSesiones.SingleAsync();

        var reabierta = await new ReabrirCajaSesionHandler(db)
            .Handle(new ReabrirCajaSesionCommand(abierta.Id, sesion.Version), CancellationToken.None);
        reabierta.Estado.Should().Be("Abierta");

        // Nada nuevo que registrar: re-arqueo conserva el esperado.
        await new IniciarArqueoCajaSesionHandler(db, new FakeUserContext(Cajero))
            .Handle(new IniciarArqueoCajaSesionCommand(abierta.Id, sesion.Version), CancellationToken.None);
        sesion.Cortes.Single(c => c.FormaPago == "01").MontoSistema.Should().Be(100m);
    }

    // ---- Dominio ----

    [Fact]
    public void Cerrar_solo_procede_desde_EnArqueo()
    {
        var sesion = CajaSesion.Abrir(Empresa, Guid.NewGuid(), SucursalId, Cajero, 0m,
            new DateOnly(2026, 7, 10), Ahora, null);

        var act = () => sesion.Cerrar(0m, null, false, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "SESION_NO_EN_ARQUEO");
    }

    [Fact]
    public void Autorizacion_solo_la_consume_el_cajero_beneficiario_en_su_caja()
    {
        var cajaId = Guid.NewGuid();
        var autorizacion = AutorizacionAperturaCaja.Crear(
            Empresa, cajaId, Cajero, Guid.NewGuid(), "Motivo", Ahora, TimeSpan.FromMinutes(30));

        var otro = () => autorizacion.Consumir(Guid.NewGuid(), Guid.NewGuid(), cajaId, Ahora);
        otro.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_CAJERO_DISTINTO");

        var otraCaja = () => autorizacion.Consumir(Guid.NewGuid(), Cajero, Guid.NewGuid(), Ahora);
        otraCaja.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_CAJA_DISTINTA");

        autorizacion.Consumir(Guid.NewGuid(), Cajero, cajaId, Ahora);
        var reuso = () => autorizacion.Consumir(Guid.NewGuid(), Cajero, cajaId, Ahora);
        reuso.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_NO_DISPONIBLE");
    }

    [Fact]
    public void DiaOperacion_usa_la_zona_horaria_de_la_sucursal()
    {
        // 05:30 UTC: Cancún (UTC-5) ya es día 10; Mérida (UTC-6) sigue en día 9.
        var ahora = new DateTimeOffset(2026, 7, 10, 5, 30, 0, TimeSpan.Zero);
        DiaOperacion.HoyLocal(ahora, "America/Cancun").Should().Be(new DateOnly(2026, 7, 10));
        DiaOperacion.HoyLocal(ahora, "America/Merida").Should().Be(new DateOnly(2026, 7, 9));
        DiaOperacion.HoyLocal(ahora, null).Should().Be(new DateOnly(2026, 7, 9)); // default Yucatán
    }

    // ---- Queries ----

    [Fact]
    public async Task SesionActual_reporta_la_sesion_vigente_y_el_bloqueo_de_dia_anterior()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        await AbrirSesionAsync(db, caja.Id, fondo: 250m);

        var hoy = await new SesionActualHandler(db, new FakeUserContext(Cajero), new FakeClock(Ahora), new FakeSucursalesReadPort())
            .Handle(new SesionActualQuery(), CancellationToken.None);
        hoy.Sesion.Should().NotBeNull();
        hoy.Sesion!.CajaNombre.Should().Be("Caja Mostrador");
        hoy.Sesion.TotalesPorForma.Single(t => t.FormaPago == "01").MontoSistema.Should().Be(250m);
        hoy.DiaAnteriorPendiente.Should().BeFalse();

        var manana = await new SesionActualHandler(db, new FakeUserContext(Cajero), new FakeClock(Ahora.AddDays(1)), new FakeSucursalesReadPort())
            .Handle(new SesionActualQuery(), CancellationToken.None);
        manana.DiaAnteriorPendiente.Should().BeTrue();

        var sinSesion = await new SesionActualHandler(db, new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), new FakeSucursalesReadPort())
            .Handle(new SesionActualQuery(), CancellationToken.None);
        sinSesion.Sesion.Should().BeNull();
    }

    [Fact]
    public async Task Listar_y_detalle_exigen_supervisar_o_administrar()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();
        var sesion = await AbrirSesionAsync(db, caja.Id);

        var sinPermiso = () => new ListarCajaSesionesHandler(db, new FakeCurrentUserPermissions())
            .Handle(new ListarCajaSesionesQuery(caja.Id, 0, 50), CancellationToken.None);
        await sinPermiso.Should().ThrowAsync<ForbiddenException>();

        var items = await new ListarCajaSesionesHandler(db, new FakeCurrentUserPermissions("facturacion.caja.supervisar"))
            .Handle(new ListarCajaSesionesQuery(caja.Id, 0, 50), CancellationToken.None);
        items.Should().ContainSingle().Which.Id.Should().Be(sesion.Id);

        var detalle = await new CajaSesionDetalleHandler(db, new FakeCurrentUserPermissions("facturacion.caja.administrar"))
            .Handle(new CajaSesionDetalleQuery(sesion.Id), CancellationToken.None);
        detalle.Movimientos.Should().ContainSingle(); // fondo
    }

    // ---- Autorización: command ----

    [Fact]
    public async Task CrearAutorizacion_usa_la_vigencia_configurada()
    {
        using var db = NewDb();
        var caja = CajaConCajero(Cajero);
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();

        var handler = new CrearAutorizacionAperturaHandler(
            db, new FakeEmpresaContext(Empresa), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), Options.Create(new CajasOptions { VigenciaAutorizacionApertura = TimeSpan.FromMinutes(45) }));

        var response = await handler.Handle(
            new CrearAutorizacionAperturaCommand(caja.Id, Cajero, "Cubre turno"), CancellationToken.None);

        response.VigenteHasta.Should().Be(Ahora.AddMinutes(45));
        (await db.AutorizacionesAperturaCaja.SingleAsync()).Estado
            .Should().Be(EstadoAutorizacionAperturaCaja.Autorizada);
    }
}
