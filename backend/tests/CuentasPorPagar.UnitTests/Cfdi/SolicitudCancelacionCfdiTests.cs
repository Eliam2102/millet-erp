using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
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

    private DecidirSolicitudCancelacionHandler Decisor(bool contadorGeneral = false, IReadOnlyList<CfdiRelacionadosParseados>? relacionados = null) =>
        new(_db, new Usuario(_usuario), new Reloj(), new Permisos(contadorGeneral), new BlobXml(), new ParserStub(relacionados));

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
    public async Task Aceptar_guarda_la_decision_pero_no_cancela_el_CFDI_en_el_SAT()
    {
        var (cfdi, sol) = await ConSolicitudAsync();

        var r = await Decisor().Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", "ref/acuse.pdf", "acuse.pdf"), default);

        r.Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
        r.DecididoPor.Should().Be(_usuario);
        r.EvidenciaNombre.Should().Be("acuse.pdf");
        var guardado = await _db.CfdisRecibidos.SingleAsync(c => c.Id == cfdi.Id);
        guardado.EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
        guardado.FechaCancelacionSat.Should().BeNull();
    }

    [Fact]
    public async Task Aceptar_con_factura_capturada_sin_pagos_guarda_la_decision_y_no_toca_la_factura()
    {
        var (cfdi, factura) = FacturaDeCfdi(pagos: 0m);
        var (_, sol) = await ConSolicitudAsync(cfdi);

        var r = await Decisor().Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", null, null), default);

        (await _db.FacturasProveedor.SingleAsync(x => x.Id == factura.Id)).Estado.Should().Be(EstadoPasivo.Capturada);
        (await _db.CfdisRecibidos.SingleAsync(c => c.Id == cfdi.Id)).EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
        (await _db.SolicitudesCancelacionCfdi.SingleAsync()).Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
        (r.SustitutoCfdiId, r.AutorizadoPor).Should().Be(((Guid?)null, (Guid?)null));
    }

    // ─── M-096: cancelación de un CFDI cuya factura ya tiene pagos ───────────────────────────────

    private (CfdiRecibido Cfdi, Factura Factura) FacturaDeCfdi(decimal pagos)
    {
        var cfdi = NuevoCfdi();
        var factura = Factura.CapturarSinOc(_empresa, cfdi.Id, null, Guid.NewGuid(), Guid.NewGuid(), "F1", null, Ahora, Ahora,
            DateOnly.FromDateTime(Ahora.UtcDateTime), "MXN", null, 100, 0, 16, 0, 116, "prueba", Ahora);
        if (pagos > 0m)
        {
            factura.Autorizar(null, Ahora);
            factura.RegistrarPago(pagos, Ahora, "pago prueba");
        }
        cfdi.MarcarConvertidoEnPasivo(factura.Id);
        _db.FacturasProveedor.Add(factura);
        return (cfdi, factura);
    }

    private async Task<CfdiRecibido> SustitutoAsync(CfdiRecibido original, string rfc = "PRO010101AAA")
    {
        var sustituto = CfdiRecibido.Ingresar(_empresa, UuidCfdi.Parse(Guid.NewGuid().ToString()),
            RfcMexicano.Parse(rfc), RfcMexicano.Parse("MIL010101AAA"), TipoCfdi.Ingreso,
            "F2", null, Ahora, 116m, 100m, 16m, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, Ahora,
            "blob-sustituto", null, "hash2", estatusSat: EstatusSatCfdi.Vigente);
        _db.CfdisRecibidos.Add(sustituto);
        await _db.SaveChangesAsync();
        return sustituto;
    }

    private static IReadOnlyList<CfdiRelacionadosParseados> Relaciona(CfdiRecibido original, string tipo = "04") =>
        [new(tipo, [original.UuidCfdi.Valor])];

    [Theory]
    [InlineData(116)]  // pagada del todo
    [InlineData(40)]   // pago parcial también cuenta
    public async Task M096_aceptar_con_pagos_sin_sustituto_se_rechaza(decimal pagos)
    {
        var (cfdi, factura) = FacturaDeCfdi(pagos);
        var (_, sol) = await ConSolicitudAsync(cfdi);

        var r = () => Decisor(contadorGeneral: true).Handle(new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", null, null), default);

        (await r.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CANCELACION_PAGADA_SIN_SUSTITUTO");
        (await _db.SolicitudesCancelacionCfdi.SingleAsync()).Estado.Should().Be(EstadoSolicitudCancelacion.Pendiente);
    }

    [Fact]
    public async Task M096_aceptar_con_pagos_y_sustituto_sin_el_permiso_del_Contador_General_da_403()
    {
        var (cfdi, _) = FacturaDeCfdi(116m);
        var (_, sol) = await ConSolicitudAsync(cfdi);
        var sust = await SustitutoAsync(cfdi);

        var r = () => Decisor(contadorGeneral: false, Relaciona(cfdi)).Handle(
            new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", null, null, sust.Id), default);

        (await r.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("CANCELACION_PAGADA_SIN_PERMISO");
        (await _db.SolicitudesCancelacionCfdi.SingleAsync()).Estado.Should().Be(EstadoSolicitudCancelacion.Pendiente);
    }

    [Fact]
    public async Task M096_aceptar_con_pagos_sustituto_relacionado_y_permiso_guarda_sustituto_y_autorizador_y_no_toca_la_factura()
    {
        var (cfdi, factura) = FacturaDeCfdi(40m);
        var (_, sol) = await ConSolicitudAsync(cfdi);
        var sust = await SustitutoAsync(cfdi);
        var estadoAntes = factura.Estado;

        var r = await Decisor(contadorGeneral: true, Relaciona(cfdi)).Handle(
            new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", null, null, sust.Id), default);

        r.Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
        (r.SustitutoCfdiId, r.AutorizadoPor).Should().Be(((Guid?)sust.Id, (Guid?)_usuario));
        (await _db.FacturasProveedor.SingleAsync(x => x.Id == factura.Id)).Estado.Should().Be(estadoAntes);
    }

    [Theory]
    [InlineData("01")]  // relación distinta de «04 Sustitución»
    [InlineData(null)]  // sin relación
    public async Task M096_sustituto_sin_la_relacion_04_al_UUID_cancelado_se_rechaza(string? tipo)
    {
        var (cfdi, _) = FacturaDeCfdi(116m);
        var (_, sol) = await ConSolicitudAsync(cfdi);
        var sust = await SustitutoAsync(cfdi);

        var r = () => Decisor(contadorGeneral: true, tipo is null ? [] : Relaciona(cfdi, tipo)).Handle(
            new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", null, null, sust.Id), default);

        (await r.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CANCELACION_SUSTITUTO_NO_RELACIONADO");
    }

    [Fact]
    public async Task M096_sustituto_de_otro_proveedor_se_rechaza()
    {
        var (cfdi, _) = FacturaDeCfdi(116m);
        var (_, sol) = await ConSolicitudAsync(cfdi);
        var sust = await SustitutoAsync(cfdi, rfc: "OTR010101AAA");

        var r = () => Decisor(contadorGeneral: true, Relaciona(cfdi)).Handle(
            new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, true, "Acuse SAT", null, null, sust.Id), default);

        (await r.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CANCELACION_SUSTITUTO_OTRO_PROVEEDOR");
    }

    [Fact]
    public async Task M096_rechazar_una_cancelacion_con_pagos_siempre_se_permite_sin_permiso_ni_sustituto()
    {
        var (cfdi, _) = FacturaDeCfdi(116m);
        var (_, sol) = await ConSolicitudAsync(cfdi);

        var r = await Decisor(contadorGeneral: false).Handle(
            new DecidirSolicitudCancelacionCommand(cfdi.Id, sol.Id, false, "Ya está pagada", null, null), default);

        r.Estado.Should().Be(EstadoSolicitudCancelacion.Rechazada);
        r.AutorizadoPor.Should().BeNull();
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

    private sealed class Permisos(bool tiene) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(tiene && permiso == PermisosCanonicos.CuentasPorPagarCfdiAceptarCancelacionPagada);
    }

    private sealed class BlobXml : ICfdiBlobStorage
    {
        public Task<string> GuardarXmlAsync(string uuid, DateTimeOffset fechaCfdi, Stream contenido, CancellationToken ct) => Task.FromResult("blob");
        public Task<string?> GuardarPdfAsync(string uuid, DateTimeOffset fechaCfdi, Stream? contenido, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken ct) => Task.FromResult<Stream?>(new MemoryStream([1]));
        public Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }

    private sealed class ParserStub(IReadOnlyList<CfdiRelacionadosParseados>? relacionados) : IXmlCfdiParser
    {
        public DatosCfdiParseados Parsear(Stream xml) => new("u", "PRO010101AAA", "Proveedor", "MIL010101AAA", "Millet", TipoCfdi.Ingreso, null, null,
            Ahora, 116m, 100m, 16m, 0m, "MXN", null, [], CfdiRelacionados: relacionados);
        public DatosCfdiParseados Parsear(string xml) => Parsear(Stream.Null);
    }
}
