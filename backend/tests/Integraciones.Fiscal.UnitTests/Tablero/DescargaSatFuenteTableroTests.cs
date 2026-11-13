using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Infrastructure.Tablero;
using Millet.Integraciones.Fiscal.UnitTests.Application;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Integraciones.Fiscal.UnitTests.Tablero;

/// <summary>I13.6 · la descarga masiva del SAT en el tablero de integraciones. Datos ficticios.</summary>
public sealed class DescargaSatFuenteTableroTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Clasifica_las_solicitudes_y_solo_una_consulta_vencida_marca_detenido()
    {
        using var db = InMemoryFiscalDb.Create();
        var esperando = Solicitud("req-1", siguienteConsulta: Ahora.AddHours(2));
        var vencida = Solicitud("req-2", siguienteConsulta: Ahora.AddMinutes(-40));
        var error = Solicitud("req-3", siguienteConsulta: Ahora);
        error.MarcarError("SAT_RECHAZO", "Solicitud rechazada por el SAT", Ahora.AddHours(-1));
        var cosechada = Solicitud("req-4", siguienteConsulta: Ahora);
        db.SolicitudesDescarga.AddRange(esperando, vencida, error, cosechada);
        Fijar(db, error, nameof(SolicitudDescarga.UpdatedAt), Ahora.AddHours(-1));
        Fijar(db, cosechada, nameof(SolicitudDescarga.Estado), EstadoSolicitudDescarga.Cosechada);
        Fijar(db, cosechada, nameof(SolicitudDescarga.CosechadaAt), (DateTimeOffset?)Ahora.AddHours(-2));
        Fijar(db, cosechada, nameof(SolicitudDescarga.UpdatedAt), Ahora.AddHours(-2));
        await db.SaveChangesAsync();

        var r = await new DescargaSatFuenteTablero(db).LeerAsync(Ahora, 10, default);

        (r.Pendientes, r.Reintentando, r.EnError, r.OkUltimas24h).Should().Be((2, 0, 1, 1));
        r.PendienteMasAntiguo.Should().Be(Ahora.AddMinutes(-40), "esperar al SAT no es falla; una consulta vencida sí");
        r.UltimoExito.Should().Be(Ahora.AddHours(-2));
        r.Mensajes[0].Should().Match<MensajeIntegracion>(m =>
            m.Estado == EstadoMensajeIntegracion.Error && m.UltimoError == "SAT_RECHAZO: Solicitud rechazada por el SAT");
    }

    private static SolicitudDescarga Solicitud(string requestId, DateTimeOffset siguienteConsulta) =>
        new(Guid.NewGuid(), EmpresaId, Guid.NewGuid(), requestId, Ahora.AddDays(-2), Ahora.AddDays(-1), siguienteConsulta);

    private static void Fijar<T>(IntegracionesFiscalDbContext db, SolicitudDescarga s, string propiedad, T valor) =>
        db.Entry(s).Property(propiedad).CurrentValue = valor;
}
