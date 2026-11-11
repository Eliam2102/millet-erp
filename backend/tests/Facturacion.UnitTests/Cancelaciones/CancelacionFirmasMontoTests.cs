using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cancelaciones.AutorizarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.Firmas;
using Millet.Facturacion.Application.Cancelaciones.ModificarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.Queries;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

/// <summary>
/// M-235: firmas por tipo de operación y monto (una o dos) y validación de versión: al ejecutar
/// se compara que la solicitud (motivo, sustituto, importe) no cambió desde que se autorizó.
/// </summary>
public sealed class CancelacionFirmasMontoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ingresos = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();
    private static readonly Guid Direccion = Guid.NewGuid();

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    /// <summary>PAC que cuenta las llamadas.</summary>
    private sealed class FakeFiscalContador : ICfdiTimbradoPort
    {
        private readonly FakeFiscalApiClient _interno = new();
        public int Cancelaciones { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => _interno.TimbrarAsync(emision, ct);
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct)
        {
            Cancelaciones++;
            return _interno.CancelarAsync(s, ct);
        }
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) => _interno.ConsultarEstatusAsync(s, ct);
    }

    private static async Task<FacturaVenta> SembrarFacturaAsync(FacturacionDbContext db, Guid empresaId, decimal precio = 100m)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 11,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, precio, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        pedido.MarcarFacturado(fv.Id);
        db.PedidosFacturables.Add(pedido);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    private static Task<IReadOnlyList<ReglaFirmaCancelacionDto>> GuardarReglasAsync(FacturacionDbContext db, Guid empresaId, params ReglaFirmaCancelacionDto[] reglas) =>
        new ReglasFirmaCancelacionHandler(db, new FakeEmpresaContext(empresaId))
            .Handle(new GuardarReglasFirmaCancelacionCommand(reglas), CancellationToken.None);

    private static Task<SolicitarCancelacionResponse> SolicitarAsync(FacturacionDbContext db, Guid empresaId, Guid id, string motivo = "02") =>
        new SolicitarCancelacionHandler(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Ingresos, "Ingresos"), new FakeClock(Ahora))
            .Handle(new SolicitarCancelacionCommand(id, motivo, null), CancellationToken.None);

    private static Task<SolicitarCancelacionResponse> AutorizarAsync(
        FacturacionDbContext db, Guid usuario, Guid id, string? huella, ICfdiTimbradoPort? fiscal = null) =>
        new AutorizarCancelacionHandler(db, fiscal ?? new FakeFiscalApiClient(), new FakeIntegrationEventPublisher(),
                new FakeUserContext(usuario, usuario.ToString()), new FakeClock(Ahora))
            .Handle(new AutorizarCancelacionCommand(id, huella), CancellationToken.None);

    private static Task<SolicitarCancelacionResponse> ModificarAsync(FacturacionDbContext db, Guid id, string motivo, string? sustituto = null) =>
        new ModificarCancelacionHandler(db, new FakeUserContext(Ingresos, "Ingresos"))
            .Handle(new ModificarCancelacionCommand(id, motivo, sustituto), CancellationToken.None);

    [Fact]
    public void Sin_reglas_basta_una_firma_y_gana_la_regla_mas_exigente_que_aplique()
    {
        var empresa = Guid.NewGuid();
        ReglaFirmaCancelacion.FirmasPara([], "FacturaVenta", 1_000_000m).Should().Be(1);

        var reglas = new[]
        {
            ReglaFirmaCancelacion.Crear(empresa, null, 50_000m, 2),
            ReglaFirmaCancelacion.Crear(empresa, "NotaCredito", 10_000m, 2),
        };
        ReglaFirmaCancelacion.FirmasPara(reglas, "FacturaVenta", 50_000m).Should().Be(1, "hasta el umbral basta una firma");
        ReglaFirmaCancelacion.FirmasPara(reglas, "FacturaVenta", 50_000.01m).Should().Be(2);
        ReglaFirmaCancelacion.FirmasPara(reglas, "NotaCredito", 10_000.01m).Should().Be(2);
        ReglaFirmaCancelacion.FirmasPara(reglas, "FacturaAnticipo", 10_000.01m).Should().Be(1);
    }

    [Theory]
    [InlineData("Inventada", 0, (short)2, "CANCELACION_REGLA_TIPO_INVALIDO")]
    [InlineData(null, -1, (short)2, "CANCELACION_REGLA_UMBRAL_INVALIDO")]
    [InlineData(null, 0, (short)3, "CANCELACION_REGLA_FIRMAS_INVALIDAS")]
    public void Regla_invalida_se_rechaza(string? tipo, decimal umbral, short firmas, string codigo)
    {
        var act = () => ReglaFirmaCancelacion.Crear(Guid.NewGuid(), tipo, umbral, firmas);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == codigo);
    }

    [Fact]
    public async Task Debajo_del_umbral_una_firma_envia_al_PAC_como_hoy()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var fv = await SembrarFacturaAsync(db, empresa); // 116 MXN
        await GuardarReglasAsync(db, empresa, new ReglaFirmaCancelacionDto(null, 1_000m, 2));
        var solicitud = await SolicitarAsync(db, empresa, fv.Id);
        solicitud.FirmasRequeridas.Should().Be(1);
        var fiscal = new FakeFiscalContador();

        var resp = await AutorizarAsync(db, Contador, fv.Id, solicitud.Huella, fiscal);

        fiscal.Cancelaciones.Should().Be(1);
        resp.EstadoSolicitud.Should().Be("Aceptada");
    }

    [Fact]
    public async Task Arriba_del_umbral_la_primera_firma_no_envia_y_la_segunda_de_otra_persona_si()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var fv = await SembrarFacturaAsync(db, empresa);
        await GuardarReglasAsync(db, empresa, new ReglaFirmaCancelacionDto("FacturaVenta", 100m, 2));
        var solicitud = await SolicitarAsync(db, empresa, fv.Id);
        solicitud.FirmasRequeridas.Should().Be(2);
        var fiscal = new FakeFiscalContador();

        var primera = await AutorizarAsync(db, Contador, fv.Id, solicitud.Huella, fiscal);
        primera.EstadoSolicitud.Should().Be("PendienteAutorizacion");
        primera.FirmasDadas.Should().Be(1);
        fiscal.Cancelaciones.Should().Be(0);

        var mismaPersona = () => AutorizarAsync(db, Contador, fv.Id, solicitud.Huella, fiscal);
        await mismaPersona.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_FIRMA_MISMA_PERSONA");

        var segunda = await AutorizarAsync(db, Direccion, fv.Id, solicitud.Huella, fiscal);
        segunda.EstadoSolicitud.Should().Be("Aceptada");
        segunda.FirmasDadas.Should().Be(2);
        fiscal.Cancelaciones.Should().Be(1);
        var s = await db.SolicitudesCancelacion.SingleAsync();
        (s.PrimeraFirmaPorId, s.DecididaPorId).Should().Be((Contador, Direccion));
    }

    [Fact]
    public async Task Si_cambia_el_motivo_despues_de_autorizar_la_ejecucion_se_bloquea_y_pide_nueva_autorizacion()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var fv = await SembrarFacturaAsync(db, empresa);
        await GuardarReglasAsync(db, empresa, new ReglaFirmaCancelacionDto(null, 0m, 2));
        var solicitud = await SolicitarAsync(db, empresa, fv.Id, "02");
        var vista = solicitud.Huella;
        await AutorizarAsync(db, Contador, fv.Id, vista);

        var modificada = await ModificarAsync(db, fv.Id, "03");
        modificada.FirmasDadas.Should().Be(0, "la primera firma se anula al cambiar la solicitud");
        modificada.Huella.Should().NotBe(vista);

        var fiscal = new FakeFiscalContador();
        var conVersionVieja = () => AutorizarAsync(db, Direccion, fv.Id, vista, fiscal);
        await conVersionVieja.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_VERSION_CAMBIO");
        fiscal.Cancelaciones.Should().Be(0);

        // Nueva autorización completa sobre la versión vigente.
        var consulta = await new ConsultarCancelacionHandler(db).Handle(new ConsultarCancelacionQuery(fv.Id), CancellationToken.None);
        consulta.FirmaAnuladaMotivo.Should().NotBeNull();
        await AutorizarAsync(db, Direccion, fv.Id, consulta.Huella, fiscal);
        fiscal.Cancelaciones.Should().Be(0);
        var final = await AutorizarAsync(db, Contador, fv.Id, consulta.Huella, fiscal);
        final.EstadoSolicitud.Should().Be("Aceptada");
        fiscal.Cancelaciones.Should().Be(1);
    }

    [Fact]
    public void Primera_firma_que_ya_no_corresponde_a_la_solicitud_bloquea_la_segunda()
    {
        var s = SolicitudCancelacion.Crear(Guid.NewGuid(), Guid.NewGuid(), "02", null, Ahora, Ingresos, "Ingresos", 500m, 2);
        s.Autorizar(Contador, "Contador", Ahora).Should().BeFalse();

        s.ActualizarImporte(600m).Should().BeTrue();

        s.PrimeraFirmaPorId.Should().BeNull();
        s.FirmaAnuladaMotivo.Should().Contain("importe");
        s.Estado.Should().Be(EstadoSolicitudCancelacion.PendienteAutorizacion);
    }

    [Fact]
    public async Task Solo_quien_solicito_puede_modificar_y_solo_mientras_esta_pendiente()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var fv = await SembrarFacturaAsync(db, empresa);
        await SolicitarAsync(db, empresa, fv.Id);

        var otro = () => new ModificarCancelacionHandler(db, new FakeUserContext(Contador, "Contador"))
            .Handle(new ModificarCancelacionCommand(fv.Id, "03", null), CancellationToken.None);
        await otro.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_MODIFICAR_SOLO_SOLICITANTE");

        var sinSustituto = () => ModificarAsync(db, fv.Id, "01");
        await sinSustituto.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_SUSTITUTO_REQUERIDO");
    }

    [Fact]
    public async Task Reglas_se_reemplazan_y_no_admiten_duplicados()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        await GuardarReglasAsync(db, empresa, new ReglaFirmaCancelacionDto(null, 1m, 2));
        await GuardarReglasAsync(db, empresa, new ReglaFirmaCancelacionDto("NotaCredito", 5m, 2));

        var reglas = await new ReglasFirmaCancelacionHandler(db, new FakeEmpresaContext(empresa))
            .Handle(new ListarReglasFirmaCancelacionQuery(), CancellationToken.None);
        reglas.Should().ContainSingle().Which.TipoComprobante.Should().Be("NotaCredito");

        var dup = () => GuardarReglasAsync(db, empresa,
            new ReglaFirmaCancelacionDto(null, 1m, 2), new ReglaFirmaCancelacionDto(null, 1m, 1));
        await dup.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_REGLA_DUPLICADA");
    }
}
