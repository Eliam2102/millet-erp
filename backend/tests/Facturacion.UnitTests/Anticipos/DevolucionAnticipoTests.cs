using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoDevolucionAnticipo;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>F6.3 · CA6.17: devolución en dinero de un anticipo no aplicado con NC y solicitud de reembolso a Tesorería.</summary>
public sealed class DevolucionAnticipoTests
{
    private const string Permiso = "facturacion.notas-credito.reembolso-solicitar";
    private const string ClabeValida = "032180000118359719";
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _cliente = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly FakeIntegrationEventPublisher _eventos = new();

    // ---- Dominio del anticipo ----

    private static Anticipo Nuevo(decimal cobrado = 1000m) => Anticipo.Crear(Guid.NewGuid(), Guid.NewGuid(), "AAA010101AAA",
        TipoAnticipo.ClientesMxp, "MXN", cobrado, Guid.NewGuid());

    [Fact]
    public void Devolver_parte_del_anticipo_baja_el_saldo_y_sigue_abierto()
    {
        var a = Nuevo();
        a.RegistrarDevolucion(400m);
        a.MontoDevuelto.Should().Be(400m);
        a.Saldo.Should().Be(600m);
        a.SaldoDisponible.Should().Be(600m);
        a.Estado.Should().Be(EstadoAnticipo.Abierto);
    }

    [Fact]
    public void Devolver_todo_deja_el_anticipo_sin_saldo()
    {
        var a = Nuevo();
        a.RegistrarDevolucion(1000m);
        a.Saldo.Should().Be(0m);
        a.Estado.Should().Be(EstadoAnticipo.Amortizado);
    }

    [Fact]
    public void No_se_devuelve_lo_comprometido_en_una_vinculacion()
    {
        var a = Nuevo();
        a.Vincular(Guid.NewGuid(), 700m, Ahora);
        var act = () => a.RegistrarDevolucion(400m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
    }

    [Fact]
    public void Amortizar_despues_de_devolver_respeta_lo_devuelto()
    {
        var a = Nuevo();
        a.RegistrarDevolucion(300m);
        a.RegistrarAmortizacion(Guid.NewGuid(), Guid.NewGuid(), 700m, Ahora);
        a.Saldo.Should().Be(0m);
        a.Estado.Should().Be(EstadoAnticipo.Amortizado);
    }

    [Fact]
    public void La_nc_de_devolucion_es_relacion_01_sin_factura_y_con_reembolso_programado()
    {
        var nc = NotaCredito.CrearDevolucionAnticipo(Guid.NewGuid(), "NC-1", 1, Guid.NewGuid(), null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"), "03", "MXN", null, 2026, 10, 1,
            Guid.NewGuid(), 116m, 0.16m, "BBVA", ClabeValida, "Cliente SA");

        nc.Motivo.Should().Be(MotivoNotaCredito.DevolucionAnticipo);
        nc.Motivo.ClaveTipoRelacion().Should().Be("01");
        nc.FacturaRelacionadaId.Should().BeNull("no toca la cartera de ninguna factura");
        nc.AnticipoOrigenId.Should().NotBeNull();
        nc.ReembolsoAlTimbrar.Should().BeTrue();
        nc.Total.Should().Be(116m);
    }

    // ---- Handler (CA6.17) ----

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    private async Task<Guid> AnticipoTimbradoAsync(FacturacionDbContext db)
    {
        var emitir = new EmitirFacturaAnticipoHandler(db, new FakeSender(new ReservarFolioResponse("FANT-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(_empresa, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeEmpresaContext(_empresa), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));
        await emitir.Handle(new EmitirFacturaAnticipoCommand(Guid.NewGuid(), _cliente, "AAA010101AAA", "Cliente Maquila",
            "601", "97000", "G03", "MEX", "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, 1000m,
            0.16m, null, null, "10425042", null, null), default);
        return (await db.Anticipos.SingleAsync()).Id;
    }

    private EmitirNotaCreditoDevolucionAnticipoHandler Handler(FacturacionDbContext db, params string[] permisos) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-000007", 7, "")), new FakePeriodoContablePort(),
            new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(), _eventos, new FakeEmpresaContext(_empresa),
            new FakeUserContext(_usuario), new FakeCurrentUserPermissions(permisos), new FakeClock(Ahora));

    private static ReembolsoClienteRequest Destino() => new("BBVA", ClabeValida, "Cliente Maquila");

    [Fact]
    public async Task CA6_17_la_nc_por_anticipo_no_aplicado_al_timbrar_manda_la_solicitud_de_reembolso_a_tesoreria()
    {
        using var db = Db();
        var anticipoId = await AnticipoTimbradoAsync(db);

        var resp = await Handler(db, Permiso).Handle(
            new EmitirNotaCreditoDevolucionAnticipoCommand(anticipoId, 580m, 0.16m, Destino()), default);

        resp.Estado.Should().Be("Timbrado");
        resp.SaldoAnticipo.Should().Be(580m);
        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        nc.Motivo.Should().Be(MotivoNotaCredito.DevolucionAnticipo);
        nc.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "01");
        nc.EstadoReembolso.Should().Be(EstadoReembolsoNotaCredito.Solicitado);

        var solicitud = _eventos.Publicados.OfType<NotaCreditoReembolsoSolicitadoIntegrationEvent>().Single();
        solicitud.NotaCreditoId.Should().Be(nc.Id);
        solicitud.ClienteId.Should().Be(_cliente);
        solicitud.Importe.Should().Be(580m);
        solicitud.DestinoClabe.Should().Be(ClabeValida);

        var timbrada = _eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Single();
        timbrada.FacturaRelacionadaId.Should().BeNull("en CxC queda informativa: no se aplica a ninguna factura");
        timbrada.AnticipoOrigenId.Should().Be(anticipoId);
        (await db.Anticipos.SingleAsync()).MontoDevuelto.Should().Be(580m);
    }

    [Fact]
    public async Task Mas_que_el_disponible_se_rechaza_sin_gastar_folio()
    {
        using var db = Db();
        var anticipoId = await AnticipoTimbradoAsync(db);

        var act = () => Handler(db, Permiso).Handle(
            new EmitirNotaCreditoDevolucionAnticipoCommand(anticipoId, 1160.01m, 0.16m, Destino()), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
        (await db.NotasCredito.AnyAsync()).Should().BeFalse();
        _eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_permiso_de_reembolso_responde_prohibido()
    {
        using var db = Db();
        var anticipoId = await AnticipoTimbradoAsync(db);

        var act = () => Handler(db).Handle(
            new EmitirNotaCreditoDevolucionAnticipoCommand(anticipoId, 100m, 0.16m, Destino()), default);

        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "NC_REEMBOLSO_SIN_PERMISO");
    }
}
