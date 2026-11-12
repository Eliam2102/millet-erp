using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

namespace Millet.Integraciones.Aw.UnitTests.AlmMensaje;

/// <summary>M-283, criterio 4: un mensaje sin acuse con más del plazo avisa al jefe de Almacén, sin repetir cada ciclo.</summary>
public sealed class AlmMensajeAlarmaTests
{
    private sealed class Aviso : IAvisoMensajesSinAcuse
    {
        public List<(int Cantidad, TimeSpan MasAntiguo)> Enviados { get; } = [];
        public bool Falla { get; set; }
        public Task AvisarAsync(int cantidad, TimeSpan masAntiguo, CancellationToken ct)
        {
            if (Falla) throw new InvalidOperationException("correo caído");
            Enviados.Add((cantidad, masAntiguo));
            return Task.CompletedTask;
        }
    }

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private static (SimuladoAlmMensajeOrigen Origen, Aviso Aviso, Reloj Reloj, AlmMensajeAlarma Alarma) Crear(int minutos = 5, int reaviso = 60)
    {
        var origen = new SimuladoAlmMensajeOrigen();
        var aviso = new Aviso();
        var reloj = new Reloj(DateTimeOffset.UtcNow);
        var alarma = new AlmMensajeAlarma(origen, aviso,
            Options.Create(new AlmMensajeOptions { AlarmaSinAcuseMinutos = minutos, AlarmaReavisoMinutos = reaviso }),
            NullLogger<AlmMensajeAlarma>.Instance, reloj);
        return (origen, aviso, reloj, alarma);
    }

    private static void Poner(SimuladoAlmMensajeOrigen o, string id, int hace) =>
        o.Encolar(id, "SALIDA_MANUAL", null, "{}", DateTimeOffset.UtcNow.AddMinutes(-hace));

    [Fact]
    public async Task Mensaje_sin_acuse_hace_6_minutos_avisa_una_vez_y_cuenta_uno()
    {
        var (origen, aviso, _, alarma) = Crear();
        Poner(origen, "M1", 6);

        (await alarma.EvaluarAsync(default)).Should().Be(1);

        aviso.Enviados.Should().ContainSingle();
        aviso.Enviados[0].Cantidad.Should().Be(1);
        aviso.Enviados[0].MasAntiguo.TotalMinutes.Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task Mensaje_reciente_no_avisa()
    {
        var (origen, aviso, _, alarma) = Crear();
        Poner(origen, "M1", 2);

        (await alarma.EvaluarAsync(default)).Should().Be(0);

        aviso.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task No_repite_el_aviso_cada_ciclo_y_vuelve_a_avisar_pasado_el_reaviso()
    {
        var (origen, aviso, reloj, alarma) = Crear(reaviso: 60);
        Poner(origen, "M1", 10);

        await alarma.EvaluarAsync(default);
        reloj.Ahora = reloj.Ahora.AddMinutes(30);
        (await alarma.EvaluarAsync(default)).Should().Be(1); // sigue contando para el tablero
        aviso.Enviados.Should().ContainSingle();              // pero no repite el correo

        reloj.Ahora = reloj.Ahora.AddMinutes(31);
        await alarma.EvaluarAsync(default);
        aviso.Enviados.Should().HaveCount(2);
    }

    [Fact]
    public async Task Con_acuse_ya_no_cuenta_y_se_rearma()
    {
        var (origen, aviso, _, alarma) = Crear();
        var id = origen.Encolar("M1", "SALIDA_MANUAL", null, "{}", DateTimeOffset.UtcNow.AddMinutes(-10));
        await alarma.EvaluarAsync(default);

        await origen.EscribirAcuseAsync(id, new AcuseApp("M1", "RECIBIDO", "F-1", null), default);

        (await alarma.EvaluarAsync(default)).Should().Be(0);
        Poner(origen, "M2", 7); // un mensaje nuevo vencido avisa otra vez sin esperar el reaviso
        await alarma.EvaluarAsync(default);
        aviso.Enviados.Should().HaveCount(2);
    }

    [Fact]
    public async Task Alarma_apagada_con_cero_minutos_no_lee_ni_avisa()
    {
        var (origen, aviso, _, alarma) = Crear(minutos: 0);
        Poner(origen, "M1", 30);

        (await alarma.EvaluarAsync(default)).Should().Be(0);

        aviso.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Si_el_correo_falla_no_lanza_y_reintenta_en_el_siguiente_ciclo()
    {
        var (origen, aviso, _, alarma) = Crear();
        Poner(origen, "M1", 10);
        aviso.Falla = true;

        (await alarma.EvaluarAsync(default)).Should().Be(1);

        aviso.Falla = false;
        await alarma.EvaluarAsync(default);
        aviso.Enviados.Should().ContainSingle();
    }
}
