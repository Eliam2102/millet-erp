using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Application.Cobranza;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Cobranza;

/// <summary>M-241 (CXC-GESTION): ficha de cobranza por factura vencida. Datos genéricos.</summary>
public sealed class FichasCobranzaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Cobrador = Guid.NewGuid();
    private static readonly Guid Supervisor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 13);

    [Fact]
    public async Task Cada_factura_vencida_aparece_con_su_responsable_o_sin_responsable()
    {
        await using var db = CrearDb();
        var vencida = AgregarFactura(db, vencidaHaceDias: 10);
        var sinFicha = AgregarFactura(db, vencidaHaceDias: 20);
        AgregarFactura(db, vencidaHaceDias: -5); // aún no vence
        await db.SaveChangesAsync();
        await Asignar(db, vencida.Id, Cobrador);

        var filas = await new FichasCobranzaHandler(db, new Reloj()).Handle(new FichasCobranzaQuery(), default);

        Assert.Equal(2, filas.Count);
        Assert.Equal(Cobrador, filas.Single(f => f.FacturaCarteraId == vencida.Id).ResponsableId);
        Assert.Null(filas.Single(f => f.FacturaCarteraId == sinFicha.Id).ResponsableId);
        var soloSin = await new FichasCobranzaHandler(db, new Reloj()).Handle(new FichasCobranzaQuery(SoloSinResponsable: true), default);
        Assert.Equal(sinFicha.Id, Assert.Single(soloSin).FacturaCarteraId);
    }

    [Fact]
    public async Task Una_promesa_fija_la_siguiente_revision_en_la_fecha_comprometida()
    {
        await using var db = CrearDb();
        var factura = AgregarFactura(db, 10); await db.SaveChangesAsync();
        await Asignar(db, factura.Id, Cobrador);

        var r = await new RegistrarGestionFichaHandler(db, new Usuario(Cobrador), new Reloj()).Handle(
            new RegistrarGestionFichaCommand(factura.Id, CanalCobranza.Llamada, ResultadoCobranza.PromesaPago, "Promete pagar",
                5_000m, Hoy.AddDays(7), null), default);

        Assert.Equal(Hoy.AddDays(7), r.SiguienteRevision);
        Assert.Equal((short)TipoEventoFicha.Promesa, r.UltimoEvento!.Tipo);
        Assert.Equal(Hoy.AddDays(7), r.UltimoEvento.RevisionDespues);
    }

    [Theory]
    [InlineData(null, 1)]      // sin monto
    [InlineData(100, null)]    // sin fecha
    public async Task Promesa_incompleta_se_rechaza(int? monto, int? dias)
    {
        await using var db = CrearDb();
        var factura = AgregarFactura(db, 10); await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new RegistrarGestionFichaHandler(db, new Usuario(Cobrador), new Reloj()).Handle(
            new RegistrarGestionFichaCommand(factura.Id, CanalCobranza.Correo, ResultadoCobranza.PromesaPago, "x",
                monto, dias is null ? null : Hoy.AddDays(dias.Value), null), default));
        Assert.StartsWith("FCO_PROMESA", ex.Code);
    }

    [Fact]
    public async Task Gestionar_sin_ficha_la_abre_con_quien_gestiona_como_responsable()
    {
        await using var db = CrearDb();
        var factura = AgregarFactura(db, 10); await db.SaveChangesAsync();
        var r = await new RegistrarGestionFichaHandler(db, new Usuario(Cobrador), new Reloj()).Handle(
            new RegistrarGestionFichaCommand(factura.Id, CanalCobranza.Whatsapp, ResultadoCobranza.SinRespuesta, "Sin respuesta", null, null, Hoy.AddDays(3)), default);
        Assert.Equal(Cobrador, r.ResponsableId);
        Assert.Equal(Hoy.AddDays(3), r.SiguienteRevision);
        Assert.Equal(1, await db.FichasCobranza.CountAsync());
    }

    [Fact]
    public async Task Reprogramar_exige_motivo_y_deja_antes_y_despues()
    {
        await using var db = CrearDb();
        var factura = AgregarFactura(db, 10); await db.SaveChangesAsync();
        await Asignar(db, factura.Id, Cobrador, revision: Hoy.AddDays(2));
        var h = new ReprogramarFichaHandler(db, new Usuario(Cobrador), new Reloj());

        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new ReprogramarFichaCommand(factura.Id, Hoy.AddDays(9), " "), default));
        var r = await h.Handle(new ReprogramarFichaCommand(factura.Id, Hoy.AddDays(9), "Cliente pidió más tiempo"), default);

        Assert.Equal(Hoy.AddDays(9), r.SiguienteRevision);
        Assert.Equal(Hoy.AddDays(2), r.UltimoEvento!.RevisionAntes);
        Assert.Equal(Hoy.AddDays(9), r.UltimoEvento.RevisionDespues);
    }

    [Fact]
    public async Task Al_escalar_cambia_el_responsable_y_queda_el_rastro()
    {
        await using var db = CrearDb();
        var factura = AgregarFactura(db, 30); await db.SaveChangesAsync();
        await Asignar(db, factura.Id, Cobrador, "Cobrador DEMO");

        var r = await new EscalarFichaHandler(db, new Usuario(Cobrador), new Reloj()).Handle(
            new EscalarFichaCommand(factura.Id, Supervisor, "Supervisor DEMO", "Sin respuesta tras 3 intentos"), default);

        Assert.Equal(Supervisor, r.ResponsableId);
        Assert.True(r.Escalada);
        Assert.Equal("Cobrador DEMO", r.UltimoEvento!.ResponsableAntes);
        Assert.Equal("Supervisor DEMO", r.UltimoEvento.ResponsableDespues);
        Assert.Equal((short)TipoEventoFicha.Escalamiento, r.UltimoEvento.Tipo);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new EscalarFichaHandler(db, new Usuario(Cobrador), new Reloj())
            .Handle(new EscalarFichaCommand(factura.Id, Supervisor, null, "otra vez"), default));
        Assert.Equal("FCO_SUPERVISOR_ES_RESPONSABLE", ex.Code);
    }

    [Fact]
    public async Task Corregir_deja_rastro_y_no_edita_el_original()
    {
        await using var db = CrearDb();
        var factura = AgregarFactura(db, 10); await db.SaveChangesAsync();
        var gestion = await new RegistrarGestionFichaHandler(db, new Usuario(Cobrador), new Reloj()).Handle(
            new RegistrarGestionFichaCommand(factura.Id, CanalCobranza.Llamada, ResultadoCobranza.Excusa, "Dijo que ya pagó", null, null, null), default);

        var r = await new CorregirEventoFichaHandler(db, new Usuario(Supervisor), new Reloj()).Handle(
            new CorregirEventoFichaCommand(gestion.UltimoEvento!.Id, "Dijo que pagaría el viernes", "Nota mal capturada"), default);

        var original = await db.FichaCobranzaEventos.SingleAsync(e => e.Id == gestion.UltimoEvento.Id);
        Assert.Equal("Dijo que ya pagó", original.Nota);
        Assert.Equal((short)TipoEventoFicha.Correccion, r.UltimoEvento!.Tipo);
        Assert.Equal(gestion.UltimoEvento.Id, r.UltimoEvento.CorrigeEventoId);
        Assert.Equal("Dijo que ya pagó", r.UltimoEvento.NotaAnterior);
        Assert.Equal("Nota mal capturada", r.UltimoEvento.MotivoCorreccion);
        Assert.Equal("Supervisor DEMO", r.UltimoEvento.UsuarioNombre);
        var historial = await new FichaCobranzaDetalleHandler(db, new Reloj()).Handle(new FichaCobranzaDetalleQuery(factura.Id), default);
        Assert.Contains(historial, e => e.Id == r.UltimoEvento.Id);
    }

    [Fact]
    public async Task Alcance_por_sucursal_solo_ve_las_facturas_permitidas()
    {
        await using var db = CrearDb();
        var propia = AgregarFactura(db, 10); var ajena = AgregarFactura(db, 10); await db.SaveChangesAsync();
        var q = new FichasCobranzaQuery { DocumentosPermitidos = [propia.Id] };
        var filas = await new FichasCobranzaHandler(db, new Reloj()).Handle(q, default);
        Assert.Equal(propia.Id, Assert.Single(filas).FacturaCarteraId);
        Assert.Empty(await new FichaCobranzaDetalleHandler(db, new Reloj()).Handle(new FichaCobranzaDetalleQuery(ajena.Id) { DocumentosPermitidos = [propia.Id] }, default));
    }


    [Fact]
    public async Task La_evaluacion_abre_la_ficha_de_toda_vencida_una_sola_vez_y_sin_responsable_si_no_hay_gestor()
    {
        await using var db = CrearDb();
        var vencida = AgregarFactura(db, 10);
        var vigente = AgregarFactura(db, -5);
        await db.SaveChangesAsync();

        await Evaluar(db);
        await Evaluar(db); // idempotente

        var ficha = await db.FichasCobranza.SingleAsync();
        Assert.Equal(vencida.Id, ficha.FacturaCarteraId);
        Assert.Null(ficha.ResponsableId);
        Assert.DoesNotContain(await db.FichasCobranza.ToListAsync(), f => f.FacturaCarteraId == vigente.Id);
        var evento = await db.FichaCobranzaEventos.SingleAsync();
        Assert.Equal("Sistema", evento.UsuarioNombre);
        var sin = await new FichasCobranzaHandler(db, new Reloj()).Handle(new FichasCobranzaQuery(SoloSinResponsable: true), default);
        Assert.Equal(vencida.Id, Assert.Single(sin).FacturaCarteraId);
    }

    [Fact]
    public async Task La_evaluacion_asigna_por_omision_al_ultimo_gestor_del_cliente()
    {
        await using var db = CrearDb();
        AgregarFactura(db, 10);
        db.SeguimientosCobranza.Add(SeguimientoCobranza.Registrar(EmpresaId, ClienteId, Supervisor, CanalCobranza.Llamada,
            ResultadoCobranza.Otro, null, null, "antiguo", Ahora.AddDays(-9)));
        db.SeguimientosCobranza.Add(SeguimientoCobranza.Registrar(EmpresaId, ClienteId, Cobrador, CanalCobranza.Correo,
            ResultadoCobranza.Otro, null, null, "reciente", Ahora.AddDays(-2)));
        await db.SaveChangesAsync();

        await Evaluar(db);

        Assert.Equal(Cobrador, (await db.FichasCobranza.SingleAsync()).ResponsableId);
    }

    [Fact]
    public async Task Asignar_a_una_ficha_sin_asignar_no_pide_motivo_y_reasignar_si()
    {
        await using var db = CrearDb();
        var factura = AgregarFactura(db, 10); await db.SaveChangesAsync();
        await Evaluar(db);

        var r = await Asignar(db, factura.Id, Cobrador, "Cobrador DEMO");
        Assert.Equal(Cobrador, r.ResponsableId);
        Assert.Null(r.UltimoEvento!.ResponsableAntes);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Asignar(db, factura.Id, Supervisor));
        Assert.Equal("FCO_MOTIVO_VACIO", ex.Code);
    }

    private static Task<int> Evaluar(CuentasPorCobrarDbContext db) =>
        new EvaluarAlertasCarteraHandler(db, new Publicador(), Options.Create(new AlertasCarteraOptions()), new Reloj(),
            NullLogger<EvaluarAlertasCarteraHandler>.Instance).Handle(new EvaluarAlertasCarteraCommand(), default);

    private sealed class Publicador : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private static Task<FichaCobranzaResponse> Asignar(CuentasPorCobrarDbContext db, Guid facturaId, Guid responsable, string? nombre = null, DateOnly? revision = null) =>
        new AsignarResponsableFichaHandler(db, new Usuario(Supervisor), new Reloj())
            .Handle(new AsignarResponsableFichaCommand(facturaId, responsable, nombre, revision, null), default);

    private static FacturaCartera AgregarFactura(CuentasPorCobrarDbContext db, int vencidaHaceDias)
    {
        var vence = Ahora.AddDays(-vencidaHaceDias);
        var factura = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "XAXX010101000", "Cliente DEMO",
            Guid.NewGuid().ToString(), "FV-DEMO", 10_000m, "MXN", "PPD", vence.AddDays(-30), vence);
        db.FacturasCartera.Add(factura);
        return factura;
    }

    private static CuentasPorCobrarDbContext CrearDb() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_m241_{Guid.NewGuid()}").Options, new EmpresaCtx());

    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Usuario(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => id == Supervisor ? "Supervisor DEMO" : "Cobrador DEMO";
    }
    private sealed class EmpresaCtx : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
