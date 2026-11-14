using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Anticipos.RegistrarAnticipoExterno;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>P45: anticipo timbrado fuera del ERP (datos inventados).</summary>
public sealed class AnticipoExternoTests
{
    private const string Rfc = "AAA010101AAA";
    private const string Uuid = "11111111-2222-4333-8444-555555555555";
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(EmpresaId));

    private static RegistrarAnticipoExternoHandler Handler(FacturacionDbContext db, string? rfcCatalogo = Rfc) =>
        new(db,
            new FakeClientesReadPort(new ClienteFiscalLectura(ClienteId, rfcCatalogo, "Cliente de prueba", "601", "97000", "G03", "03", "PUE",
                "MXN", false)),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(EmpresaId, "MIL010101AAA", "Empresa prueba", "601", 0.16m, "76120")),
            new FakeEmpresaContext(EmpresaId),
            new FakeUserContext(Guid.NewGuid()));

    private static RegistrarAnticipoExternoCommand Cmd(string uuid = Uuid, string rfc = Rfc, decimal saldo = 600m, string folio = "A-100") =>
        new(ClienteId, rfc, Guid.NewGuid(), uuid, folio, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), "MXN", null, "03",
            1160m, 0.16m, saldo, "90000001");

    [Fact]
    public async Task Registra_factura_timbrada_externa_y_saldo_vivo_ligado_al_pedido()
    {
        await using var db = NewDb();
        var r = await Handler(db).Handle(Cmd(), CancellationToken.None);

        var factura = await db.FacturasAnticipo.SingleAsync();
        Assert.True(factura.OrigenExterno);
        Assert.Equal(EstadoTimbrado.Timbrado, factura.Estado);
        Assert.Equal(Uuid.ToUpperInvariant(), factura.Uuid);
        Assert.Equal(1000m, factura.Subtotal);
        Assert.Equal(160m, factura.ImpuestosTrasladados);
        Assert.StartsWith(RegistrarAnticipoExternoHandler.PrefijoFolio, factura.Folio);

        var anticipo = await db.Anticipos.SingleAsync();
        Assert.Equal(1160m, anticipo.MontoCobrado);
        Assert.Equal(560m, anticipo.MontoAmortizado);
        Assert.Equal(600m, anticipo.Saldo);
        Assert.Equal(600m, anticipo.SaldoDisponible);
        Assert.Equal(EstadoAnticipo.Abierto, anticipo.Estado);
        Assert.Equal("90000001", anticipo.PedidoOrigenRef);
        Assert.Equal(factura.Id, anticipo.FacturaAnticipoId);
        Assert.Equal(600m, r.Saldo);
    }

    [Fact]
    public async Task Rechaza_uuid_ya_registrado_aunque_cambie_mayusculas()
    {
        await using var db = NewDb();
        await Handler(db).Handle(Cmd(), CancellationToken.None);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db).Handle(Cmd(uuid: Uuid.ToUpperInvariant(), folio: "A-101"), CancellationToken.None));
        Assert.Equal("ANTICIPO_EXTERNO_UUID_DUPLICADO", ex.Code);
    }

    [Fact]
    public async Task Rechaza_rfc_distinto_al_del_cliente()
    {
        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db).Handle(Cmd(rfc: "BBB010101BBB"), CancellationToken.None));
        Assert.Equal("ANTICIPO_EXTERNO_RFC_DISTINTO", ex.Code);
        Assert.Empty(db.Anticipos);
    }

    [Fact]
    public async Task Rechaza_cliente_sin_rfc_en_catalogo()
    {
        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Handler(db, rfcCatalogo: null).Handle(Cmd(), CancellationToken.None));
        Assert.Equal("ANTICIPO_EXTERNO_CLIENTE_SIN_RFC", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1160.01)]
    public void Saldo_vivo_fuera_de_rango_se_rechaza(decimal saldo)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Anticipo.CrearExterno(EmpresaId, ClienteId, Rfc, TipoAnticipo.ClientesMxp,
            "MXN", 1160m, saldo, Guid.NewGuid(), null, Guid.NewGuid()));
        Assert.Equal("ANTICIPO_EXTERNO_SALDO_INVALIDO", ex.Code);
    }

    private static FacturaAnticipo Externa() => FacturaAnticipo.RegistrarExterna(
        EmpresaId, "EXT-A-1", Guid.NewGuid(), null,
        new DatosFiscalesReceptor(Rfc, "Cliente de prueba", "601", "97000", "G03", "MEX", false),
        new DatosFiscalesEmisor("MIL010101AAA", "Empresa prueba", "601", "76120"),
        "03", "MXN", null, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), TipoAnticipo.ClientesMxp, null,
        Guid.NewGuid(), Uuid, 1160m, 0.16m);

    [Fact]
    public void Nunca_se_timbra_cancela_ni_reenvia_desde_el_erp()
    {
        var f = Externa();
        Assert.Equal("COMPROBANTE_EXTERNO", Assert.Throws<BusinessRuleException>(f.MarcarTimbradoEnProceso).Code);
        Assert.Equal("COMPROBANTE_EXTERNO", Assert.Throws<BusinessRuleException>(f.ReabrirParaReintentoTimbrado).Code);
        Assert.Equal("COMPROBANTE_EXTERNO", Assert.Throws<BusinessRuleException>(f.MarcarCancelacionPendiente).Code);
        Assert.Equal("COMPROBANTE_EXTERNO", Assert.Throws<BusinessRuleException>(f.MarcarEnviadoCorreo).Code);
        Assert.Equal(EstadoTimbrado.Timbrado, f.Estado);
        Assert.False(f.EnviadoCorreo);
    }

    [Fact]
    public void Uuid_invalido_se_rechaza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => FacturaAnticipo.RegistrarExterna(
            EmpresaId, "EXT-A-2", Guid.NewGuid(), null,
            new DatosFiscalesReceptor(Rfc, "Cliente de prueba", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Empresa prueba", "601", "76120"),
            "03", "MXN", null, DateTimeOffset.UtcNow, TipoAnticipo.ClientesMxp, null, Guid.NewGuid(), "no-es-uuid", 100m, null));
        Assert.Equal("ANTICIPO_EXTERNO_UUID_INVALIDO", ex.Code);
    }

    [Fact]
    public void Un_comprobante_del_erp_sigue_timbrandose_normal()
    {
        var f = FacturaAnticipo.CrearBorrador(EmpresaId, "FANT-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor(Rfc, "Cliente de prueba", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Empresa prueba", "601", "76120"),
            "03", "MXN", null, 2026, 9, TipoAnticipo.ClientesMxp, null, Guid.NewGuid(), 1000m, 0.16m);
        Assert.False(f.EsOrigenExterno);
        f.MarcarTimbradoEnProceso();
        Assert.Equal(EstadoTimbrado.TimbradoEnProceso, f.Estado);
    }

    [Fact]
    public async Task Solicitar_cancelacion_de_un_anticipo_externo_se_rechaza_antes_del_pac()
    {
        await using var db = NewDb();
        var r = await Handler(db).Handle(Cmd(), CancellationToken.None);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new SolicitarCancelacionHandler(db, new FakeEmpresaContext(EmpresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(DateTimeOffset.UtcNow))
                .Handle(new SolicitarCancelacionCommand(r.FacturaAnticipoId, "02", null), CancellationToken.None));
        Assert.Equal("COMPROBANTE_EXTERNO", ex.Code);
    }
}
