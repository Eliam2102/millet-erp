using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Application.TableroIntegraciones;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Integration.Tablero;
using Millet.SharedKernel.Domain.Integraciones;

namespace Millet.Api.IntegrationTests.Integraciones;

/// <summary>
/// I13.6 · QA O1: cada tipo de alerta del tablero de integraciones se abre, se guarda y se cierra
/// contra PostgreSQL real (el CHECK de <c>compartido.alertas_integracion</c> no se ve con EF InMemory;
/// así se escapó «Reintentos altos», QA H1). Fuente y correo falsos; datos ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class AlertasIntegracionPostgresTests(WebApplicationFactory<Program> factory)
{
    private static readonly DateTimeOffset Inicio = new(2020, 3, 2, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cada_tipo_de_alerta_se_abre_se_guarda_una_vez_y_se_cierra_con_su_aviso()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var clave = $"prueba.alertas-{Guid.NewGuid():N}"[..40];
        var fuente = new FuenteFalsa(clave);
        var correo = new CorreoFalso();
        var reloj = new Reloj { UtcNow = Inicio };
        var tablero = new TableroIntegracionesServicio([fuente], db, correo, reloj,
            new Monitor(new TableroIntegracionesOptions { Destinatarios = ["operacion@ejemplo.test"], UmbralDetenidoMinutos = 15 }),
            NullLogger<TableroIntegracionesServicio>.Instance);
        try
        {
            // Las tres condiciones a la vez: un mensaje en error, uno reintentando con 5 fallas y uno detenido.
            fuente.Foto = new ResumenFuenteIntegracion(1, 1, 1, 0, null, Inicio.AddHours(-1),
            [
                new("m-err", "evento.a", EstadoMensajeIntegracion.Error, 11, "Timeout", Inicio.AddHours(-2), null),
                new("m-rei", "evento.b", EstadoMensajeIntegracion.Reintentando, 5, "Timeout", Inicio.AddHours(-1), null),
            ]);

            var abiertas = await tablero.EvaluarAlertasAsync(default);
            reloj.UtcNow = Inicio.AddMinutes(1);
            var otraVuelta = await tablero.EvaluarAlertasAsync(default);

            var tipos = Enum.GetValues<TipoAlertaIntegracion>();
            Assert.Equal(tipos.Length, abiertas.Abiertas);
            Assert.Equal(0, otraVuelta.Abiertas);
            Assert.Equal(tipos.Length, correo.Enviados);
            var guardadas = await db.AlertasIntegracion.AsNoTracking().Where(a => a.FuenteClave == clave).ToListAsync();
            Assert.Equal(tipos.OrderBy(t => t), guardadas.Select(a => a.Tipo).OrderBy(t => t));
            Assert.All(guardadas, a => Assert.NotNull(a.NotificadaEn));

            // Se recupera: las tres se cierran y avisan su recuperación.
            fuente.Foto = ResumenFuenteIntegracion.Vacio;
            reloj.UtcNow = Inicio.AddMinutes(2);
            var cierre = await tablero.EvaluarAlertasAsync(default);

            Assert.Equal(tipos.Length, cierre.Cerradas);
            Assert.Equal(tipos.Length * 2, correo.Enviados);
            var cerradas = await db.AlertasIntegracion.AsNoTracking().Where(a => a.FuenteClave == clave).ToListAsync();
            Assert.All(cerradas, a => Assert.True(a.CerradaEn is not null && a.RecuperacionNotificadaEn is not null));
        }
        finally
        {
            await db.AlertasIntegracion.Where(a => a.FuenteClave == clave).ExecuteDeleteAsync();
        }
    }

    private sealed class FuenteFalsa(string clave) : IFuenteTableroIntegracion
    {
        public ResumenFuenteIntegracion Foto { get; set; } = ResumenFuenteIntegracion.Vacio;
        public string Clave => clave;
        public string Nombre => "Integración de prueba";
        public string Grupo => "Pruebas";
        public bool PermiteReintento => false;
        public Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken) =>
            Task.FromResult(Foto);
        public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class CorreoFalso : ICorreoSaliente
    {
        public int Enviados { get; private set; }
        public bool EsSimulado => false;
        public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
        {
            Enviados++;
            return Task.FromResult(ResultadoCorreoSaliente.Aceptado);
        }
    }

    private sealed class Reloj : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class Monitor(TableroIntegracionesOptions valor) : IOptionsMonitor<TableroIntegracionesOptions>
    {
        public TableroIntegracionesOptions CurrentValue => valor;
        public TableroIntegracionesOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<TableroIntegracionesOptions, string?> listener) => null;
    }
}
