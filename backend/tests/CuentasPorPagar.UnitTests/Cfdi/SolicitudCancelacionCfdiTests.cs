using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CancelarFactura;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

/// <summary>P8.7 (CA8.16, P8.7-a): registrar y decidir solicitudes de cancelación de CFDI.</summary>
public sealed class SolicitudCancelacionCfdiTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero); // miércoles
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly CuentasPorPagarDbContext _db;

    public SolicitudCancelacionCfdiTests() =>
        _db = new CuentasPorPagarDbContext(
            new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase($"p87-{Guid.NewGuid():N}").Options,
            new EmpresaBypass(), new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private CfdiRecibido NuevoCfdi() =>
        CfdiRecibido.Ingresar(_empresa, UuidCfdi.Parse(Guid.NewGuid().ToString()),
            RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"), TipoCfdi.Ingreso,
            "F1", null, Ahora, 116m, 100m, 16m, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, Ahora,
            "blob", null, "hash", estatusSat: EstatusSatCfdi.Vigente);

    private RegistrarSolicitudCancelacionHandler Registrador() => new(_db, new Usuario(_usuario), new Reloj());

    private DecidirSolicitudCancelacionHandler Decisor() => new(_db, new Usuario(_usuario),
        new Mediator(new ServiceCollection()
            .AddSingleton<IRequestHandler<CancelarFacturaCommand, CancelarFacturaResponse>>(
                new CancelarFacturaHandler(_db, new Usuario(_usuario), new Mediator(new ServiceCollection().BuildServiceProvider()), new Reloj()))
            .BuildServiceProvider()),
        new Reloj());

    private async Task<(CfdiRecibido Cfdi, SolicitudCancelacionResponse Sol)> ConSolicitudAsync(CfdiRecibido? cfdi = null)
    {
        cfdi ??= NuevoCfdi();
        _db.CfdisRecibidos.Add(cfdi);
        await _db.SaveChangesAsync();
        var sol = await Registrador().Handle(
            new RegistrarSolicitudCancelacionCommand(cfdi.Id, AreaSolicitudCancelacion.Compras, null, "Pidió el proveedor"), default);
        return (cfdi, sol);
    }

    [Fact]
    public async Task Registrar_deja_responsable_area_estado_pendiente_y_plazo_SAT_de_3_dias_habiles()
    {
        var (_, sol) = await ConSolicitudAsync();

        sol.Estado.Should().Be(EstadoSolicitudCancelacion.Pendiente);
        sol.ResponsableId.Should().Be(_usuario);
        sol.Area.Should().Be(AreaSolicitudCancelacion.Compras);
        sol.FechaRecepcion.Should().Be(Ahora);
        sol.PlazoSatVence.Should().Be(Ahora.AddDays(5)); // mié + 3 hábiles = lun
    }

    [Fact]
    public async Task Registrar_otra_con_una_pendiente_rechaza()
    {
        var (cfdi, _) = await ConSolicitudAsync();

        var otra = () => Registrador().Handle(new RegistrarSolicitudCancelacionCommand(cfdi.Id, AreaSolicitudCancelacion.Transportes, null, null), default);

        (await otra.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SOLICITUD_CANCELACION_PENDIENTE");
    }

    [Fact]
    public async Task Aceptar_sin_pasivo_marca_el_CFDI_cancelado_en_el_SAT()
    {
        var (cfdi, sol) = await ConSolicitudAsync();

        var r = await Decisor().Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", "ref/acuse.pdf", "acuse.pdf"), default);

        r.Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
        r.DecididoPor.Should().Be(_usuario);
        r.EvidenciaNombre.Should().Be("acuse.pdf");
        var guardado = await _db.CfdisRecibidos.SingleAsync(c => c.Id == cfdi.Id);
        guardado.EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        guardado.FechaCancelacionSat.Should().Be(Ahora);
    }

    [Fact]
    public async Task Aceptar_con_pasivo_cancela_la_factura_por_CfdiCanceladoEnSat()
    {
        var cfdi = NuevoCfdi();
        var factura = Factura.CapturarSinOc(_empresa, cfdi.Id, null, Guid.NewGuid(), Guid.NewGuid(), "F1", null, Ahora, Ahora,
            DateOnly.FromDateTime(Ahora.UtcDateTime), "MXN", null, 100, 0, 16, 0, 116, "prueba", Ahora);
        cfdi.MarcarConvertidoEnPasivo(factura.Id);
        _db.FacturasProveedor.Add(factura);
        var (_, sol) = await ConSolicitudAsync(cfdi);

        await Decisor().Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", null, null), default);

        var f = await _db.FacturasProveedor.SingleAsync(x => x.Id == factura.Id);
        f.Estado.Should().Be(EstadoPasivo.Cancelada);
        f.MotivoDeCancelacion.Should().Be(MotivoCancelacion.CfdiCanceladoEnSat);
        (await _db.CfdisRecibidos.SingleAsync(c => c.Id == cfdi.Id)).EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        (await _db.SolicitudesCancelacionCfdi.SingleAsync()).Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
    }

    [Fact]
    public async Task Rechazar_deja_el_CFDI_vigente_y_la_evidencia()
    {
        var (cfdi, sol) = await ConSolicitudAsync();

        var r = await Decisor().Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, false, "Ya está pagada", "ref/e.pdf", "e.pdf"), default);

        r.Estado.Should().Be(EstadoSolicitudCancelacion.Rechazada);
        r.ComentarioDecision.Should().Be("Ya está pagada");
        r.EvidenciaNombre.Should().Be("e.pdf");
        r.FechaDecision.Should().Be(Ahora);
        (await _db.CfdisRecibidos.SingleAsync(c => c.Id == cfdi.Id)).EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
    }

    [Fact]
    public async Task Decidir_dos_veces_rechaza()
    {
        var (cfdi, sol) = await ConSolicitudAsync();
        await Decisor().Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, false, "No procede", null, null), default);

        var otra = () => Decisor().Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Cambié de opinión", null, null), default);

        (await otra.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("SOLICITUD_CANCELACION_YA_DECIDIDA");
    }

    [Fact]
    public void El_plazo_SAT_salta_fines_de_semana()
    {
        var viernes = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        SolicitudCancelacionCfdi.SumarDiasHabiles(viernes, 3).Should().Be(viernes.AddDays(5)); // miércoles
    }

    private sealed class EmpresaBypass : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    private sealed class Usuario(Guid id) : ICurrentUserContext
    {
        public Guid? UserId { get; } = id;
        public string? UserName => "test";
    }

    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }
}
