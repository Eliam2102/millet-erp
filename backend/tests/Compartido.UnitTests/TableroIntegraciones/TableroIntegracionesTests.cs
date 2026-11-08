using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Application.TableroIntegraciones;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration.Tablero;
using Millet.SharedKernel.Domain.Integraciones;

namespace Millet.Compartido.UnitTests.TableroIntegraciones;

/// <summary>
/// I13.6 · tablero de integraciones: junta las fuentes, alerta por correo una vez
/// al abrir y otra al recuperarse, no repite avisos y reintenta a mano. Datos ficticios.
/// </summary>
public sealed class TableroIntegracionesTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CA13_6_flujo_detenido_alerta_una_vez_y_al_recuperarse_cierra_y_avisa_la_recuperacion()
    {
        using var db = NewDb();
        var aw = new FuenteFalsa("aw.avisos", "Avisos de pedidos de A+W");
        var reloj = new Reloj(Inicio);
        var correo = new CorreoFalso();
        var tablero = Tablero(db, reloj, correo, aw);

        // A+W caída: hay avisos pendientes desde hace una hora.
        aw.Foto = new(Pendientes: 12, 0, 0, 0, null, Inicio.AddHours(-1), []);
        var primera = await tablero.EvaluarAlertasAsync(default);
        reloj.Ahora = Inicio.AddMinutes(1);
        aw.Foto = aw.Foto with { Pendientes = 30 };
        var segunda = await tablero.EvaluarAlertasAsync(default);

        (primera.Abiertas, primera.CorreosEnviados, segunda.Abiertas, segunda.CorreosEnviados).Should().Be((1, 1, 0, 0));
        var alerta = await db.AlertasIntegracion.SingleAsync();
        (alerta.Tipo, alerta.FuenteClave, alerta.NotificadaEn).Should().Be((TipoAlertaIntegracion.FlujoDetenido, "aw.avisos", Inicio));
        alerta.Detalle.Should().Contain("30 mensaje(s) sin procesar");
        correo.Enviados.Single().Asunto.Should().Be("Alerta de integración: Avisos de pedidos de A+W · Flujo detenido");

        // Se restablece: los avisos se procesaron solos.
        reloj.Ahora = Inicio.AddMinutes(70);
        aw.Foto = new(0, 0, 0, 42, Inicio.AddMinutes(69), null, []);
        var recuperada = await tablero.EvaluarAlertasAsync(default);

        (recuperada.Cerradas, recuperada.CorreosEnviados).Should().Be((1, 1));
        alerta = await db.AlertasIntegracion.SingleAsync();
        (alerta.CerradaEn, alerta.RecuperacionNotificadaEn).Should().Be((Inicio.AddMinutes(70), Inicio.AddMinutes(70)));
        correo.Enviados.Last().Asunto.Should().Be("Integración recuperada: Avisos de pedidos de A+W");
        (await tablero.LeerAsync(default)).AlertasAbiertas.Should().BeEmpty();
        (await tablero.AlertasAsync(10, default)).Should().ContainSingle(a => a.CerradaEn != null);
    }

    [Fact]
    public async Task I13_6_a_mensajes_que_agotaron_reintentos_abren_alerta_de_error_y_se_reintentan_a_mano()
    {
        using var db = NewDb();
        var mensaje = new MensajeIntegracion("m-1", "compras.orden-compra.autorizada.v1", EstadoMensajeIntegracion.Error, 11,
            "Timeout", Inicio.AddMinutes(-5), null);
        var outbox = new FuenteFalsa("outbox.compras", "Eventos de Compras")
        {
            Foto = new(0, 0, 1, 10, Inicio.AddMinutes(-1), null, [mensaje]),
        };
        var correo = new CorreoFalso();
        var tablero = Tablero(db, new Reloj(Inicio), correo, outbox);

        await tablero.EvaluarAlertasAsync(default);
        await tablero.ReintentarAsync("outbox.compras", "m-1", default);

        (await db.AlertasIntegracion.SingleAsync()).Tipo.Should().Be(TipoAlertaIntegracion.MensajesEnError);
        correo.Enviados.Single().Html.Should().Contain("1 mensaje(s) agotaron sus reintentos");
        outbox.Reintentados.Should().Equal("m-1");
        var foto = (await tablero.LeerAsync(default)).Fuentes.Single();
        (foto.EnError, foto.Detenido, foto.Mensajes.Single().Intentos).Should().Be((1, false, 11));
    }

    [Fact]
    public async Task Sin_destinatarios_la_alerta_queda_en_el_tablero_y_un_correo_caido_se_reintenta_en_la_siguiente_vuelta()
    {
        using var db = NewDb();
        var fuente = new FuenteFalsa("x", "Correo de CFDI") { Foto = new(0, 0, 2, 0, null, null, []) };
        var correo = new CorreoFalso { Falla = true };
        var reloj = new Reloj(Inicio);
        var tablero = Tablero(db, reloj, correo, fuente);

        (await tablero.EvaluarAlertasAsync(default)).CorreosEnviados.Should().Be(0);
        (await db.AlertasIntegracion.SingleAsync()).NotificadaEn.Should().BeNull();

        correo.Falla = false;
        reloj.Ahora = Inicio.AddMinutes(1);
        (await tablero.EvaluarAlertasAsync(default)).CorreosEnviados.Should().Be(1);
        (await db.AlertasIntegracion.SingleAsync()).NotificadaEn.Should().Be(Inicio.AddMinutes(1));

        using var db2 = NewDb();
        var sinDestinatarios = Tablero(db2, reloj, new CorreoFalso(), fuente, destinatarios: []);
        (await sinDestinatarios.EvaluarAlertasAsync(default)).Abiertas.Should().Be(1);
        (await db2.AlertasIntegracion.SingleAsync()).NotificadaEn.Should().NotBeNull("sin destinatarios no hay correo que reintentar");
    }

    [Fact]
    public async Task Una_fuente_que_no_se_puede_leer_se_muestra_no_disponible_y_no_cierra_su_alerta()
    {
        using var db = NewDb();
        var fuente = new FuenteFalsa("aw.retorno", "Retorno a A+W") { Foto = new(0, 0, 3, 0, null, null, []) };
        var otra = new FuenteFalsa("facturacion.correo-cfdi", "Correo de CFDI") { Foto = ResumenFuenteIntegracion.Vacio };
        var tablero = Tablero(db, new Reloj(Inicio), new CorreoFalso(), fuente, otra);
        await tablero.EvaluarAlertasAsync(default);

        fuente.Lanza = true;
        var r = await tablero.EvaluarAlertasAsync(default);
        var foto = await tablero.LeerAsync(default);

        r.Cerradas.Should().Be(0);
        (await db.AlertasIntegracion.SingleAsync()).CerradaEn.Should().BeNull();
        foto.Fuentes.Single(f => f.Clave == "aw.retorno").Should().Match<FuenteTableroResponse>(f =>
            !f.Disponible && f.ErrorLectura != null && !f.PermiteReintento);
        foto.Fuentes.Single(f => f.Clave == "facturacion.correo-cfdi").Disponible.Should().BeTrue();
    }

    [Fact]
    public async Task Reintento_manual_valida_la_integracion_y_el_mensaje()
    {
        using var db = NewDb();
        var soloLectura = new FuenteFalsa("service-bus.dead-letter", "Cola de errores", permiteReintento: false);
        var fuente = new FuenteFalsa("outbox.compras", "Eventos de Compras") { Reintentable = false };
        var tablero = Tablero(db, new Reloj(Inicio), new CorreoFalso(), soloLectura, fuente);

        var noExiste = () => tablero.ReintentarAsync("no.existe", "m", default);
        var sinReintento = () => tablero.ReintentarAsync("service-bus.dead-letter", "m", default);
        var yaProcesado = () => tablero.ReintentarAsync("outbox.compras", "m", default);

        (await noExiste.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("INTEGRACION_NO_ENCONTRADA");
        (await sinReintento.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("INTEGRACION_SIN_REINTENTO");
        (await yaProcesado.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("INTEGRACION_MENSAJE_NO_REINTENTABLE");
    }

    // ---------- Apoyo ----------

    private static TableroIntegracionesServicio Tablero(CompartidoDbContext db, IClock reloj, ICorreoSaliente correo,
        params IFuenteTableroIntegracion[] fuentes) => Tablero(db, reloj, correo, fuentes[0], ["operacion@ejemplo.test"], fuentes);

    private static TableroIntegracionesServicio Tablero(CompartidoDbContext db, IClock reloj, ICorreoSaliente correo,
        IFuenteTableroIntegracion primera, string[] destinatarios, params IFuenteTableroIntegracion[] resto)
    {
        var fuentes = resto.Length > 0 ? resto : [primera];
        var opciones = new TableroIntegracionesOptions { Destinatarios = destinatarios, UmbralDetenidoMinutos = 15 };
        return new TableroIntegracionesServicio(fuentes, db, correo, reloj, new Monitor(opciones),
            NullLogger<TableroIntegracionesServicio>.Instance);
    }

    private static CompartidoDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());

    private sealed class FuenteFalsa(string clave, string nombre, bool permiteReintento = true) : IFuenteTableroIntegracion
    {
        public ResumenFuenteIntegracion Foto { get; set; } = ResumenFuenteIntegracion.Vacio;
        public bool Lanza { get; set; }
        public bool Reintentable { get; set; } = true;
        public List<string> Reintentados { get; } = [];
        public string Clave => clave;
        public string Nombre => nombre;
        public string Grupo => "Pruebas";
        public bool PermiteReintento => permiteReintento;

        public Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken) =>
            Lanza ? throw new InvalidOperationException("conexión rechazada") : Task.FromResult(Foto);

        public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken)
        {
            if (!Reintentable) return Task.FromResult(false);
            Reintentados.Add(mensajeId);
            return Task.FromResult(true);
        }
    }

    private sealed class CorreoFalso : ICorreoSaliente
    {
        public bool Falla { get; set; }
        public List<MensajeCorreo> Enviados { get; } = [];
        public bool EsSimulado => false;

        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
        {
            if (Falla) return Task.FromResult(ResultadoCorreoSaliente.Error("SMTP", "sin conexión"));
            Enviados.Add(mensaje);
            return Task.FromResult(ResultadoCorreoSaliente.Aceptado);
        }
    }

    private sealed class Reloj(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class Monitor(TableroIntegracionesOptions valor) : IOptionsMonitor<TableroIntegracionesOptions>
    {
        public TableroIntegracionesOptions CurrentValue => valor;
        public TableroIntegracionesOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<TableroIntegracionesOptions, string?> listener) => null;
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
