using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Application.Timbrado.Contingencias;
using Millet.Facturacion.Application.Timbrado.DescartarComprobante;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>
/// F6.8 · Bitácora de contingencias de timbrado: registro automático por causa
/// externa (CA6.5), regularización con el CFDI final (F6.8-b), descarte con folio
/// quemado y aviso a Fiscal cuando el mes cerró (F6.8-a).
/// </summary>
public sealed class ContingenciasTimbradoTests
{
    // 28-nov-2026 10:00 en Mérida (UTC-6).
    private static readonly DateTimeOffset Nov28 = new(2026, 11, 28, 16, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId, string? nombre = null) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(nombre ?? Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static FacturaVenta Borrador(Guid empresaId, Guid? sucursalId = null)
    {
        var fv = FacturaVenta.CrearBorrador(
            empresaId, $"FV-{Guid.NewGuid():N}"[..12], 1, sucursalId ?? Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente prueba", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor prueba", "601", "76120"),
            "PUE", "01", "MXN", null, 2026, 11,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Vidrio claro 6 mm (prueba)", "H87", 2m, 500m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        return fv;
    }

    private sealed class PacGuionado(params TimbradoResultado[] respuestas) : ICfdiTimbradoPort
    {
        private int _i;

        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken) =>
            Task.FromResult(respuestas[Math.Min(_i++, respuestas.Length - 1)]);

        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static TimbradoResultado Fallo(string codigo) =>
        new(TimbradoEstado.Fallido, null, null, null, null, null, null, null, codigo, $"mensaje {codigo}");

    private static TimbradoResultado Ok(string uuid) =>
        new(TimbradoEstado.Timbrado, uuid, "SELLO", "SELLO_SAT", "00001000000500000000", Nov28.AddDays(1),
            "SAT970701NN3", $"<cfdi uuid=\"{uuid}\" fake=\"true\" />", null, null);

    private static Task<TimbradoResultado> Timbrar(FacturacionDbContext db, Comprobante c, ICfdiTimbradoPort pac, DateTimeOffset ahora) =>
        TimbradoEjecutor.TimbrarYAplicarAsync(db, c, null!, pac, new FakeCfdiRepositorioPort(), ahora, CancellationToken.None);

    [Theory]
    [InlineData("CSD_VENCIDO", TipoContingenciaTimbrado.Certificado)]
    [InlineData("CSD_NO_CONFIGURADO", TipoContingenciaTimbrado.Certificado)]
    [InlineData("PAC_SIN_RESPUESTA", TipoContingenciaTimbrado.Pac)]
    [InlineData("PAC_TIMEOUT", TipoContingenciaTimbrado.Pac)]
    [InlineData("ServiceUnavailable", TipoContingenciaTimbrado.Pac)]
    [InlineData("503", TipoContingenciaTimbrado.Pac)]
    public void Clasifica_las_causas_externas(string codigo, TipoContingenciaTimbrado esperado) =>
        ContingenciasTimbrado.Clasificar(codigo).Should().Be(esperado);

    [Theory]
    [InlineData("CFDI40139")]
    [InlineData("BadRequest")]
    [InlineData(null)]
    public void Un_rechazo_del_documento_no_es_contingencia(string? codigo) =>
        ContingenciasTimbrado.Clasificar(codigo).Should().BeNull();

    [Fact]
    public async Task CA6_5_pac_caido_registra_contingencia_y_el_reintento_la_regulariza_con_el_cfdi()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = Borrador(empresaId);
        db.FacturasVenta.Add(fv);
        var pac = new PacGuionado(Fallo("ServiceUnavailable"), Ok("AAAAAAAA-0000-4000-8000-000000000001"));

        await Timbrar(db, fv, pac, Nov28);
        await db.SaveChangesAsync();

        fv.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
        var c = await db.ContingenciasTimbrado.SingleAsync();
        c.Estado.Should().Be(EstadoContingenciaTimbrado.Abierta);
        c.Tipo.Should().Be(TipoContingenciaTimbrado.Pac);
        c.Origen.Should().Be(OrigenContingenciaTimbrado.Automatica);
        c.ComprobanteId.Should().Be(fv.Id);
        c.ComprobanteFolio.Should().Be(fv.Folio);
        c.ErrorCodigo.Should().Be("ServiceUnavailable");
        c.FechaLimite.Should().Be(new DateOnly(2026, 12, 3));

        fv.ReabrirParaReintentoTimbrado();
        var despues = Nov28.AddDays(1);
        await Timbrar(db, fv, pac, despues);
        await db.SaveChangesAsync();

        fv.Estado.Should().Be(EstadoTimbrado.Timbrado);
        c.Estado.Should().Be(EstadoContingenciaTimbrado.Regularizada);
        c.UuidFinal.Should().Be("AAAAAAAA-0000-4000-8000-000000000001");
        c.CerradaAt.Should().Be(despues);
    }

    [Fact]
    public async Task Un_rechazo_por_datos_no_abre_contingencia_y_otro_fallo_externo_no_la_duplica()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = Borrador(empresaId);
        db.FacturasVenta.Add(fv);
        var pac = new PacGuionado(Fallo("CFDI40139"), Fallo("PAC_SIN_RESPUESTA"), Fallo("CSD_VENCIDO"));

        await Timbrar(db, fv, pac, Nov28);
        await db.SaveChangesAsync();
        (await db.ContingenciasTimbrado.CountAsync()).Should().Be(0);

        fv.ReabrirParaReintentoTimbrado();
        await Timbrar(db, fv, pac, Nov28.AddMinutes(5));
        await db.SaveChangesAsync();
        fv.ReabrirParaReintentoTimbrado();
        await Timbrar(db, fv, pac, Nov28.AddMinutes(10));
        await db.SaveChangesAsync();

        var c = await db.ContingenciasTimbrado.SingleAsync();
        c.Tipo.Should().Be(TipoContingenciaTimbrado.Pac); // el tipo es el del primer evento
        c.ErrorCodigo.Should().Be("CSD_VENCIDO");         // conserva el último error
        c.OcurridaAt.Should().Be(Nov28.AddMinutes(5));
    }

    [Fact]
    public async Task Descartar_el_documento_deja_la_contingencia_descartada_con_folio_quemado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = Borrador(empresaId);
        db.FacturasVenta.Add(fv);
        await Timbrar(db, fv, new PacGuionado(Fallo("PAC_SIN_RESPUESTA")), Nov28);
        // PAC_SIN_RESPUESTA llega como fallido en esta prueba; el worker lo hace con PAC_TIMEOUT.
        await db.SaveChangesAsync();

        await new DescartarComprobanteHandler(db, new FakeClock(Nov28.AddDays(2)))
            .Handle(new DescartarComprobanteCommand(fv.Id), CancellationToken.None);

        var c = await db.ContingenciasTimbrado.SingleAsync();
        c.Estado.Should().Be(EstadoContingenciaTimbrado.Descartada);
        c.CerradaAt.Should().Be(Nov28.AddDays(2));
        c.NotaCierre.Should().Contain("folio");
        c.UuidFinal.Should().BeNull();
    }

    [Fact]
    public void Fecha_limite_es_el_dia_3_del_mes_siguiente_en_hora_de_merida()
    {
        // 30-nov 23:30 en Mérida ya es 1-dic en UTC: sigue contando como noviembre.
        var noche = new DateTimeOffset(2026, 12, 1, 5, 30, 0, TimeSpan.Zero);
        ContingenciaTimbrado.CalcularFechaLimite(noche).Should().Be(new DateOnly(2026, 12, 3));
        ContingenciaTimbrado.CalcularFechaLimite(new DateTimeOffset(2026, 12, 15, 18, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2027, 1, 3));

        var c = ContingenciaTimbrado.Registrar(Guid.NewGuid(), Guid.NewGuid(), TipoContingenciaTimbrado.Sat,
            OrigenContingenciaTimbrado.Manual, "SAT sin servicio", null, Nov28, null, null, null, "Ingresos");
        c.RequiereAlerta(new DateOnly(2026, 11, 30)).Should().BeFalse();
        c.RequiereAlerta(new DateOnly(2026, 12, 2)).Should().BeTrue();
        c.EstaVencida(new DateOnly(2026, 12, 3)).Should().BeFalse();
        c.EstaVencida(new DateOnly(2026, 12, 4)).Should().BeTrue();
    }

    [Fact]
    public void La_captura_manual_con_hora_local_se_guarda_en_UTC_sin_mover_el_mes()
    {
        // La pantalla manda la hora de la sucursal (-06:00); Postgres solo acepta UTC.
        var local = new DateTimeOffset(2026, 11, 30, 22, 0, 0, TimeSpan.FromHours(-6));

        var c = ContingenciaTimbrado.Registrar(Guid.NewGuid(), Guid.NewGuid(), TipoContingenciaTimbrado.Sat,
            OrigenContingenciaTimbrado.Manual, "SAT sin servicio", null, local, null, null, null, "Ingresos");

        c.OcurridaAt.Offset.Should().Be(TimeSpan.Zero);
        c.OcurridaAt.Should().Be(local);
        c.FechaLimite.Should().Be(new DateOnly(2026, 12, 3));
    }

    private sealed class CorreoFalso : ICorreoSaliente
    {
        public List<MensajeCorreo> Enviados { get; } = [];
        public bool EsSimulado => false;

        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
        {
            Enviados.Add(mensaje);
            return Task.FromResult(ResultadoCorreoSaliente.Aceptado);
        }
    }

    private static (ContingenciasAlertaWorker Worker, CorreoFalso Correo) ArmarWorker(
        string dbName, Guid empresaId, DateTimeOffset ahora, params string[] fiscal)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IClock>(new FakeClock(ahora));
        var correo = new CorreoFalso();
        services.AddSingleton<ICorreoSaliente>(correo);
        services.AddOptions<ContingenciasAlertaOptions>().Configure(o => o.Fiscal = [.. fiscal]);
        var sp = services.BuildServiceProvider();
        return (new ContingenciasAlertaWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<ContingenciasAlertaOptions>>(),
            NullLogger<ContingenciasAlertaWorker>.Instance), correo);
    }

    [Fact]
    public async Task F6_8_a_contingencia_abierta_del_28_nov_avisa_a_fiscal_el_2_dic_una_sola_vez()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        using (var seed = NewDb(empresaId, dbName))
        {
            seed.ContingenciasTimbrado.Add(ContingenciaTimbrado.Registrar(empresaId, Guid.NewGuid(),
                TipoContingenciaTimbrado.Internet, OrigenContingenciaTimbrado.Manual, "Sin internet en sucursal",
                null, Nov28, null, null, null, "Ingresos"));
            await seed.SaveChangesAsync();
        }

        // 30-nov: el mes no ha cerrado → sin aviso.
        var (antes, correoAntes) = ArmarWorker(dbName, empresaId, new DateTimeOffset(2026, 11, 30, 18, 0, 0, TimeSpan.Zero), "fiscal@ejemplo.test");
        (await antes.TickAsync(CancellationToken.None)).Should().Be(0);
        correoAntes.Enviados.Should().BeEmpty();

        var dic2 = new DateTimeOffset(2026, 12, 2, 15, 0, 0, TimeSpan.Zero);
        var (worker, correo) = ArmarWorker(dbName, empresaId, dic2, "fiscal@ejemplo.test");
        (await worker.TickAsync(CancellationToken.None)).Should().Be(1);
        correo.Enviados.Should().ContainSingle();
        correo.Enviados[0].Para.Should().Equal("fiscal@ejemplo.test");
        correo.Enviados[0].Html.Should().Contain("Sin internet en sucursal").And.Contain("03/12/2026");

        (await worker.TickAsync(CancellationToken.None)).Should().Be(0);
        correo.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task Sin_correos_de_fiscal_no_marca_el_aviso()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        using (var seed = NewDb(empresaId, dbName))
        {
            seed.ContingenciasTimbrado.Add(ContingenciaTimbrado.Registrar(empresaId, Guid.NewGuid(),
                TipoContingenciaTimbrado.Sat, OrigenContingenciaTimbrado.Manual, "SAT", null, Nov28, null, null, null, null));
            await seed.SaveChangesAsync();
        }

        var (worker, correo) = ArmarWorker(dbName, empresaId, new DateTimeOffset(2026, 12, 2, 15, 0, 0, TimeSpan.Zero));
        (await worker.TickAsync(CancellationToken.None)).Should().Be(0);
        correo.Enviados.Should().BeEmpty();
        using var db = NewDb(empresaId, dbName);
        (await db.ContingenciasTimbrado.SingleAsync()).AlertaEnviadaAt.Should().BeNull();
    }

    [Fact]
    public async Task Registro_manual_y_regularizacion_manual_sin_documento()
    {
        var empresaId = Guid.NewGuid();
        var sucursalId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var clock = new FakeClock(Nov28.AddHours(2));
        var user = new FakeUserContext(Guid.NewGuid(), "Ingresos prueba");

        var creada = await new RegistrarContingenciaHandler(db, clock, user, new FakeEmpresaContext(empresaId))
            .Handle(new RegistrarContingenciaCommand(sucursalId, "Sat", "SAT sin servicio de 9 a 11", Nov28), CancellationToken.None);

        creada.Estado.Should().Be("Abierta");
        creada.Origen.Should().Be("Manual");
        creada.RegistradaPor.Should().Be("Ingresos prueba");
        creada.FechaLimite.Should().Be(new DateOnly(2026, 12, 3));

        var cerrada = await new RegularizarContingenciaHandler(db, clock, user)
            .Handle(new RegularizarContingenciaCommand(creada.Id, "Se timbraron al regresar el servicio."), CancellationToken.None);

        cerrada.Estado.Should().Be("Regularizada");
        cerrada.NotaCierre.Should().Be("Se timbraron al regresar el servicio.");
        cerrada.CerradaAt.Should().Be(Nov28.AddHours(2));
        cerrada.CerradaPor.Should().Be("Ingresos prueba");
    }

    [Fact]
    public async Task Una_contingencia_con_documento_no_se_regulariza_a_mano()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = Borrador(empresaId);
        db.FacturasVenta.Add(fv);
        await Timbrar(db, fv, new PacGuionado(Fallo("PAC_SIN_RESPUESTA")), Nov28);
        await db.SaveChangesAsync();
        var c = await db.ContingenciasTimbrado.SingleAsync();

        var act = () => new RegularizarContingenciaHandler(db, new FakeClock(Nov28), new FakeUserContext(null))
            .Handle(new RegularizarContingenciaCommand(c.Id, "nota"), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CONTINGENCIA_CON_DOCUMENTO");
    }

    [Fact]
    public async Task F6_8_b_la_consulta_de_una_regularizada_muestra_el_cfdi_final_y_la_fecha()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = Borrador(empresaId);
        db.FacturasVenta.Add(fv);
        var pac = new PacGuionado(Fallo("PAC_SIN_RESPUESTA"), Ok("BBBBBBBB-0000-4000-8000-000000000002"));
        await Timbrar(db, fv, pac, Nov28);
        await db.SaveChangesAsync();
        fv.ReabrirParaReintentoTimbrado();
        await Timbrar(db, fv, pac, Nov28.AddHours(3));
        await db.SaveChangesAsync();

        var r = await new ContingenciasQueryHandler(db, new FakeClock(Nov28.AddDays(1)))
            .Handle(new ContingenciasQuery(2026, 11, "Regularizada"), CancellationToken.None);

        var item = r.Items.Should().ContainSingle().Subject;
        item.UuidFinal.Should().Be("BBBBBBBB-0000-4000-8000-000000000002");
        item.CerradaAt.Should().Be(Nov28.AddHours(3));
        item.ComprobanteFolio.Should().Be(fv.Folio);
        item.ComprobanteEstado.Should().Be("Timbrado");
        r.Abiertas.Should().Be(0);
    }
}
