using Millet.Almacen.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// Validación de servidor del vínculo CFDI en la recepción variante A (F3/F4):
/// <c>RECEPCION_SIN_CFDI</c> cuando no hay CFDI ni UUID, y los códigos
/// <c>RECEPCION_CFDI_*</c> cuando el CFDI referido no es vinculable.
/// </summary>
public class RecepcionCfdiValidacionTests
{
    private static readonly Guid ArticuloId = Guid.NewGuid();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid RackId = Guid.NewGuid();

    private static async Task<AlmacenDbContext> NuevaDbConRackAsync()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-recep-cfdi-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        var sub = Guid.NewGuid();
        db.Ubicaciones.Add(new Ubicacion(RackId, sub, "R-1", "Rack 1", esDefault: false));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), RackId, ArticuloId));
        await db.SaveChangesAsync();
        return db;
    }

    private static RegistrarRecepcionConFacturaHandler NuevoHandler(AlmacenDbContext db, ICxpDocumentosReadPort cxp, IProveedorReadPort proveedores) =>
        new(db, new FakeOc(), new FakeEvents(),
            new FakeUser(), new FakeEmpresa(EmpresaId), new FakeDecimales(), new PeriodoContableStub(true), new P7Support.Conversion(),
            cxp, proveedores);

    private static RegistrarRecepcionConFacturaCommand Comando(Guid? cfdiRecibidoId, string? uuidFiscal = null) => new(
        OrdenCompraId: Guid.NewGuid(),
        FechaMovimiento: new DateOnly(2026, 5, 23),
        CfdiRecibidoId: cfdiRecibidoId,
        CfdiUuidFiscal: uuidFiscal,
        Observaciones: null,
        Lineas: [new RegistrarRecepcionLineaInput(ArticuloId, ArticuloId, 5m, null, null, RackId)]);

    [Fact]
    public async Task Sin_cfdi_ni_uuid_lanza_RECEPCION_SIN_CFDI()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(cfdiRecibidoId: null);

        var act = () => NuevoHandler(db, CfdiFake.Valido(), new ProveedorRfcFake()).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_SIN_CFDI");
    }

    [Fact]
    public async Task Uuid_solo_espacios_sin_cfdi_lanza_RECEPCION_SIN_CFDI()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(cfdiRecibidoId: null, uuidFiscal: "   ");

        var act = () => NuevoHandler(db, CfdiFake.Valido(), new ProveedorRfcFake()).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_SIN_CFDI");
    }

    [Fact]
    public async Task Cfdi_inexistente_lanza_RECEPCION_CFDI_NO_ENCONTRADO()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());

        var act = () => NuevoHandler(db, new CfdiFake(null), new ProveedorRfcFake()).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_CFDI_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Cfdi_descartado_lanza_RECEPCION_CFDI_NO_VINCULABLE()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());
        var cfdi = new CfdiFake(new CfdiRecibidoLectura(Guid.NewGuid(), "AAA010101AAA", "Descartado", "Ingreso"));

        var act = () => NuevoHandler(db, cfdi, new ProveedorRfcFake()).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_CFDI_NO_VINCULABLE");
    }

    [Fact]
    public async Task Cfdi_de_egreso_lanza_RECEPCION_CFDI_NO_VINCULABLE()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());
        var cfdi = new CfdiFake(new CfdiRecibidoLectura(Guid.NewGuid(), "AAA010101AAA", "PorProcesar", "Egreso"));

        var act = () => NuevoHandler(db, cfdi, new ProveedorRfcFake()).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_CFDI_NO_VINCULABLE");
    }

    [Fact]
    public async Task Cfdi_de_otro_rfc_lanza_RECEPCION_CFDI_PROVEEDOR_DISTINTO()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());

        var act = () => NuevoHandler(db, CfdiFake.Valido("BBB010101BBB"), new ProveedorRfcFake("AAA010101AAA")).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_CFDI_PROVEEDOR_DISTINTO");
    }

    [Fact]
    public async Task Cfdi_valido_del_rfc_del_proveedor_registra_recepcion()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());

        var resp = await NuevoHandler(db, CfdiFake.Valido("AAA010101AAA"), new ProveedorRfcFake("AAA010101AAA"))
            .Handle(cmd, CancellationToken.None);
        resp.RecepcionId.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Proveedor_sin_rfc_lanza_RECEPCION_CFDI_PROVEEDOR_DISTINTO_con_sin_registrar(string rfcProveedor)
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());

        var act = () => NuevoHandler(db, CfdiFake.Valido(), new ProveedorRfcFake(rfcProveedor)).Handle(cmd, CancellationToken.None);
        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        ex.Code.Should().Be("RECEPCION_CFDI_PROVEEDOR_DISTINTO");
        ex.Message.Should().Contain("sin registrar");
    }

    [Fact]
    public async Task Variante_A_con_folio_fiscal_a_mano_registra_recepcion_sin_consultar_cfdi()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(cfdiRecibidoId: null, uuidFiscal: "11111111-2222-3333-4444-555555555555");

        var resp = await NuevoHandler(db, new CfdiFake(null), new ProveedorRfcFake())
            .Handle(cmd, CancellationToken.None);
        resp.RecepcionId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Rfc_del_cfdi_con_espacios_y_minusculas_registra_recepcion()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());

        var resp = await NuevoHandler(db, CfdiFake.Valido("  aaa010101aaa "), new ProveedorRfcFake("AAA010101AAA"))
            .Handle(cmd, CancellationToken.None);
        resp.RecepcionId.Should().NotBeEmpty();
    }

    // 86e3tabdp: el CFDI de una recepción cancelada ya es una factura (ConvertidoEnPasivo) y debe poder ligarse a la recepción correcta.

    [Fact]
    public async Task Cfdi_convertido_en_pasivo_cuya_factura_de_la_misma_OC_quedo_sin_recepcion_se_vincula()
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());
        var cfdi = new CfdiFake(new CfdiRecibidoLectura(Guid.NewGuid(), "AAA010101AAA", "ConvertidoEnPasivo", "Ingreso", cmd.OrdenCompraId));

        var resp = await NuevoHandler(db, cfdi, new ProveedorRfcFake()).Handle(cmd, CancellationToken.None);

        resp.RecepcionId.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)] // la factura del CFDI es de otra OC (o ya tiene recepción: CxP no manda OC)
    [InlineData(true)]  // otra recepción vigente ya usa el CFDI
    public async Task Cfdi_convertido_en_pasivo_no_se_vincula_si_no_quedo_libre(bool recepcionVigenteLoUsa)
    {
        await using var db = await NuevaDbConRackAsync();
        var cmd = Comando(Guid.NewGuid());
        var ajeno = new CfdiRecibidoLectura(Guid.NewGuid(), "AAA010101AAA", "ConvertidoEnPasivo", "Ingreso",
            recepcionVigenteLoUsa ? cmd.OrdenCompraId : Guid.NewGuid());
        if (recepcionVigenteLoUsa)
            await NuevoHandler(db, CfdiFake.Valido(), new ProveedorRfcFake()).Handle(cmd with { OrdenCompraId = Guid.NewGuid() }, CancellationToken.None);

        var act = () => NuevoHandler(db, new CfdiFake(ajeno), new ProveedorRfcFake()).Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_CFDI_NO_VINCULABLE");
    }

    // ─── Fakes (copia de RegistrarRecepcionMultiSubAlmacenTests) ─────────────

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeOc : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) =>
            Task.FromResult<OcLectura?>(new(ocId, "OC-CFDI", EmpresaId, EmpresaId, "Autorizada", [new(ArticuloId, ArticuloId, "PZA", 100, 0, 10)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeArticulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(
                new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class FakeEvents : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => EmpresaId;
        public string? UserName => "test";
    }

    private sealed class FakeDecimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
