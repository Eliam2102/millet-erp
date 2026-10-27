using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;
using Millet.Tesoreria.Infrastructure.PublicAdapters;

namespace Millet.Tesoreria.UnitTests.Depositos;

/// <summary>
/// C7.4 · CA7.9: depósitos de cliente «sin identificar» que Tesorería expone a
/// CxC por puerto, y el sobrante de una confirmación (queda AplicadoParcial y
/// vuelve a ofrecerse para identificarlo).
/// </summary>
public sealed class DepositosPorIdentificarTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c7");
    private static readonly Guid Tesorero = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000c7");
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 10, 26);

    // --------------------------------------------------- adaptador

    [Fact]
    public async Task Adaptador_lista_ingreso_de_cliente_sin_aplicar_y_excluye_lo_no_identificable()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var cliente = Guid.NewGuid();
        var sinCliente = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente, referencia: "SPEI-50K");
        var conCliente = Ingreso(db, cuenta, 1_200m, BeneficiarioTipo.Cliente, cliente);
        Ingreso(db, cuenta, 900m, beneficiarioTipo: null);               // caja / sin clasificar
        Ingreso(db, cuenta, 800m, BeneficiarioTipo.Otro);                // viáticos u otros
        var aplicado = Ingreso(db, cuenta, 700m, BeneficiarioTipo.Cliente);
        aplicado.ActualizarEstadoAplicacion(700m);
        var revertido = Ingreso(db, cuenta, 600m, BeneficiarioTipo.Cliente);
        db.MovimientosBancarios.Add(revertido.CrearContramovimiento(600m, Fecha, Tesorero, Ahora, "Depósito duplicado"));
        var devuelto = Ingreso(db, cuenta, 500m, BeneficiarioTipo.Cliente);
        db.DevolucionesCliente.Add(DevolucionCliente.SolicitarPorDepositoErroneo(Empresa, "DEV-T-0001", devuelto, cliente,
            500m, 500m, "Depósito erróneo", new CuentaDestinoDevolucion("BBVA", "032180000118359719", "Cliente SA"), Tesorero, Ahora));
        var deCaja = Ingreso(db, cuenta, 400m, BeneficiarioTipo.Cliente);
        var expectativa = DepositoConfirmacion.CrearExpectativaCaja(Empresa, Guid.NewGuid(), 400m, Fecha);
        expectativa.Confirmar(deCaja, Tesorero, Ahora);
        db.DepositosConfirmacion.Add(expectativa);
        await db.SaveChangesAsync();

        var lista = await new DepositosPorIdentificarReadAdapter(db).ListarAsync(default);

        lista.Select(d => d.MovimientoBancarioId).Should().BeEquivalentTo([sinCliente.Id, conCliente.Id]);
        var primero = lista.Single(d => d.MovimientoBancarioId == sinCliente.Id);
        primero.Disponible.Should().Be(50_000m);
        primero.Monto.Should().Be(50_000m);
        primero.Moneda.Should().Be("MXN");
        primero.Referencia.Should().Be("SPEI-50K");
        primero.ClienteId.Should().BeNull();
        primero.Cuenta.Should().Contain("BBVA");
        lista.Single(d => d.MovimientoBancarioId == conCliente.Id).ClienteId.Should().Be(cliente);

        (await new DepositosPorIdentificarReadAdapter(db).ObtenerAsync(sinCliente.Id, default)).Should().NotBeNull();
        (await new DepositosPorIdentificarReadAdapter(db).ObtenerAsync(revertido.Id, default)).Should().BeNull();
        (await new DepositosPorIdentificarReadAdapter(db).ObtenerAsync(devuelto.Id, default)).Should().BeNull();
        (await new DepositosPorIdentificarReadAdapter(db).ObtenerAsync(deCaja.Id, default)).Should().BeNull();
    }

    // --------------------------------------------------- sobrante

    [Fact]
    public async Task Confirmar_con_sobrante_deja_AplicadoParcial_y_permite_segunda_confirmacion()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var movimiento = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        // CxC propuso el depósito completo; 45,000 a facturas y 5,000 de saldo a favor por identificar.
        var primera = DepositoDePropuesta(db, 50_000m, saldoAFavor: 5_000m);
        await db.SaveChangesAsync();
        var publisher = new FakePublisher();

        await Confirmar(db, publisher, primera, movimiento.Id);

        movimiento.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.AplicadoParcial);
        var adapter = new DepositosPorIdentificarReadAdapter(db);
        var sobrante = (await adapter.ObtenerAsync(movimiento.Id, default))!;
        sobrante.Disponible.Should().Be(5_000m);
        // E9 · C7.4-b: el ingreso llegó sin cliente; el sobrante es «sin identificar» DEL cliente de la propuesta.
        sobrante.ClienteId.Should().Be(primera.ClienteId);
        publisher.Publicados.OfType<PagoClienteConfirmadoIntegrationEvent>().Single()
            .SaldoAFavorPorIdentificar.Should().Be(5_000m);

        // CxC identifica el sobrante contra el mismo movimiento.
        var segunda = DepositoDePropuesta(db, 5_000m, saldoAFavor: 0m, movimientoSugerido: movimiento.Id);
        await db.SaveChangesAsync();
        await Confirmar(db, publisher, segunda, movimiento.Id);

        movimiento.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);
        (await adapter.ObtenerAsync(movimiento.Id, default)).Should().BeNull();
        var evento = publisher.Publicados.OfType<PagoClienteConfirmadoIntegrationEvent>().Last();
        evento.Monto.Should().Be(5_000m);
        evento.MovimientoBancarioId.Should().Be(movimiento.Id);
    }

    [Fact]
    public async Task Propuesta_ligada_por_menos_del_disponible_se_confirma_y_deja_el_resto()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var movimiento = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        var deposito = DepositoDePropuesta(db, 30_000m, saldoAFavor: 0m, movimientoSugerido: movimiento.Id);
        await db.SaveChangesAsync();

        await Confirmar(db, new FakePublisher(), deposito, movimiento.Id);

        movimiento.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.AplicadoParcial);
        (await new DepositosPorIdentificarReadAdapter(db).ObtenerAsync(movimiento.Id, default))!.Disponible.Should().Be(20_000m);
    }

    [Fact]
    public async Task Propuesta_sin_liga_por_monto_distinto_al_disponible_sigue_rechazandose()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var movimiento = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        var deposito = DepositoDePropuesta(db, 30_000m, saldoAFavor: 0m);
        await db.SaveChangesAsync();

        var act = () => Confirmar(db, new FakePublisher(), deposito, movimiento.Id);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEP_MONTO_NO_COINCIDE");
    }

    [Fact]
    public async Task Quien_propone_no_confirma_aunque_el_deposito_venga_identificado()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var movimiento = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        var deposito = DepositoDePropuesta(db, 50_000m, saldoAFavor: 0m, movimientoSugerido: movimiento.Id);
        await db.SaveChangesAsync();

        var act = () => Confirmar(db, new FakePublisher(), deposito, movimiento.Id, usuario: Proponente);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEP_MISMO_USUARIO");
        movimiento.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
    }

    [Fact]
    public async Task Rep_timbrado_con_propuesta_marca_esa_confirmacion_aunque_el_movimiento_tenga_dos()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var movimiento = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        var primera = DepositoDePropuesta(db, 50_000m, saldoAFavor: 5_000m);
        await db.SaveChangesAsync();
        await Confirmar(db, new FakePublisher(), primera, movimiento.Id);
        var segunda = DepositoDePropuesta(db, 5_000m, saldoAFavor: 0m, movimientoSugerido: movimiento.Id);
        await db.SaveChangesAsync();
        await Confirmar(db, new FakePublisher(), segunda, movimiento.Id);

        await new MarcarReppTimbradoHandler(db, new FakeClock(), Microsoft.Extensions.Logging.Abstractions.NullLogger<MarcarReppTimbradoHandler>.Instance)
            .Handle(new(Guid.NewGuid(), new ReciboPagoTimbradoPayload(Empresa, Ahora, Guid.NewGuid(), "REP-B", 5_000m,
                [new ReppFacturaPagadaPayload(Guid.NewGuid(), 5_000m)], movimiento.Id, segunda.PropuestaCxcId)), default);

        segunda.ReppTimbrado.Should().BeTrue();
        primera.ReppTimbrado.Should().BeFalse();
    }

    [Fact]
    public async Task E8_propuesta_ligada_solo_se_confirma_contra_su_movimiento()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var ligado = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        var otro = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        var deposito = DepositoDePropuesta(db, 50_000m, saldoAFavor: 0m, movimientoSugerido: ligado.Id);
        await db.SaveChangesAsync();

        var act = () => Confirmar(db, new FakePublisher(), deposito, otro.Id);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEP_MOVIMIENTO_DISTINTO_AL_LIGADO");
        otro.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
        await Confirmar(db, new FakePublisher(), deposito, ligado.Id);
        ligado.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);
    }

    [Fact]
    public async Task Bandeja_de_Tesoreria_descuenta_propuestas_ligadas_pendientes_y_respeta_sucursal()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var sucursal = Guid.NewGuid();
        var parcial = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente, sucursalId: sucursal);
        var cubierto = Ingreso(db, cuenta, 10_000m, BeneficiarioTipo.Cliente, sucursalId: sucursal);
        var otraSucursal = Ingreso(db, cuenta, 7_000m, BeneficiarioTipo.Cliente, sucursalId: Guid.NewGuid());
        DepositoDePropuesta(db, 30_000m, 0m, parcial.Id);
        DepositoDePropuesta(db, 10_000m, 0m, cubierto.Id);
        await db.SaveChangesAsync();
        var handler = new DepositosSinIdentificarHandler(db, new DepositosPorIdentificarReadAdapter(db), new ClientesVacios());

        var corporativo = await handler.Handle(new DepositosSinIdentificarQuery(), default);
        var operativo = await handler.Handle(new DepositosSinIdentificarQuery { SucursalesPermitidas = [sucursal] }, default);

        corporativo.Select(d => (d.MovimientoBancarioId, d.Disponible)).Should().BeEquivalentTo([(parcial.Id, 20_000m), (otraSucursal.Id, 7_000m)]);
        operativo.Select(d => d.MovimientoBancarioId).Should().Equal(parcial.Id);
    }

    // --------------------------------------------------- preselección

    [Fact]
    public async Task Proyeccion_preselecciona_el_movimiento_identificado_por_CxC()
    {
        await using var db = Db();
        var cuenta = Cuenta(db);
        var movimiento = Ingreso(db, cuenta, 50_000m, BeneficiarioTipo.Cliente);
        await db.SaveChangesAsync();
        var handler = new ProyectarPropuestaAplicacionHandler(db, new FakeClock());

        await handler.Handle(new(Guid.NewGuid(), Payload(movimiento.Id)), default);
        await handler.Handle(new(Guid.NewGuid(), Payload(Guid.NewGuid())), default); // movimiento inexistente: sin sugerencia

        var depositos = await db.DepositosConfirmacion.ToListAsync();
        var respuestas = depositos.Select(d => DepositoMapperProbe.ToResponse(d)).ToList();
        respuestas.Should().ContainSingle(r => r.MovimientoSugeridoId == movimiento.Id);
        respuestas.Should().ContainSingle(r => r.MovimientoSugeridoId == null);
        respuestas.Should().OnlyContain(r => r.MovimientoId == null);
    }

    // --------------------------------------------------- helpers

    private static PropuestaAplicacionCreadaPayload Payload(Guid movimientoId) => new(
        EmpresaId: Empresa, OcurridoEn: Ahora, PropuestaId: Guid.NewGuid(), ClienteId: Guid.NewGuid(),
        DepositoRef: "SPEI-50K", MontoDeposito: 50_000m, Moneda: "MXN", AjusteNoFiscal: 0m, NumeroFacturas: 1,
        Facturas: [new PropuestaFacturaPayload(Guid.NewGuid(), "VEN-1", 50_000m)],
        PropuestoPor: Proponente, SaldoAFavorPorIdentificar: 0m, MovimientoBancarioId: movimientoId);

    private static async Task Confirmar(TesoreriaDbContext db, FakePublisher publisher, DepositoConfirmacion deposito,
        Guid movimientoId, Guid? usuario = null)
    {
        var handler = new ConfirmarDepositoHandler(db, new FakeEmpresa(), new FakeUser(usuario ?? Tesorero), publisher, new FakeClock());
        await handler.Handle(new ConfirmarDepositoCommand(deposito.Id, movimientoId, deposito.Version), default);
    }

    private static DepositoConfirmacion DepositoDePropuesta(TesoreriaDbContext db, decimal monto, decimal saldoAFavor,
        Guid? movimientoSugerido = null)
    {
        var deposito = DepositoConfirmacion.CrearDesdePropuesta(Empresa, Guid.NewGuid(), Guid.NewGuid(), "SPEI-50K", monto, "MXN",
            $$"""[{"FacturaVentaId":"{{Guid.NewGuid()}}","Folio":"VEN-1","ImporteAplicado":{{monto - saldoAFavor}}}]""",
            Proponente, saldoAFavor, movimientoSugerido);
        db.DepositosConfirmacion.Add(deposito);
        return deposito;
    }

    private static CuentaBancaria Cuenta(TesoreriaDbContext db)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0123456789", null, "MXN");
        db.CuentasBancarias.Add(cuenta);
        return cuenta;
    }

    private static MovimientoBancario Ingreso(TesoreriaDbContext db, CuentaBancaria cuenta, decimal monto,
        BeneficiarioTipo? beneficiarioTipo, Guid? cliente = null, string? referencia = null, Guid? sucursalId = null)
    {
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, monto, Fecha, referencia, null,
            beneficiarioTipo, cliente, Tesorero, Ahora, sucursalId);
        db.MovimientosBancarios.Add(m);
        return m;
    }

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>()
        .UseInMemoryDatabase($"tes_c74_{Guid.NewGuid()}").Options, new FakeEmpresa());

    private static class DepositoMapperProbe
    {
        public static DepositoConfirmacionResponse ToResponse(DepositoConfirmacion d) => DepositoMapper.ToResponse(d);
    }

    private sealed class ClientesVacios : Millet.Tesoreria.Domain.Ports.DatosMaestros.IClienteReadPort
    {
        public Task<IReadOnlyDictionary<Guid, Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>> ObtenerVariosAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>>(
                new Dictionary<Guid, Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>());
        public Task<IReadOnlyList<Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>> BuscarAsync(string texto, int limite, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Millet.Tesoreria.Domain.Ports.DatosMaestros.ClienteRefDto>>([]);
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "Tesorería C7.4";
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
