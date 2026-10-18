using System.Globalization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.DiferenciasFiscales;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Corrida semanal del reporte de diferencias fiscales A+W vs ERP (U1.8b). Solo se registra con
/// <c>IntegracionesAw:DiferenciasFiscales:ProgramacionHabilitada=true</c>. Espera al siguiente
/// <c>DiaSemana</c>/<c>HoraLocal</c> en <c>ZonaHoraria</c>, calcula y guarda la corrida. Solo lee A+W.
/// Una corrida perdida (API apagada a esa hora) no se recupera: queda para la semana siguiente.
/// </summary>
public sealed class DiferenciasFiscalesAwWorker(
    IServiceScopeFactory scopes,
    IOptions<DiferenciasFiscalesAwOptions> options,
    TimeProvider time,
    ILogger<DiferenciasFiscalesAwWorker> logger) : BackgroundService
{
    public const string Actor = "system:aw-diferencias-fiscales";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var hora = TimeOnly.ParseExact(o.HoraLocal, "HH:mm", CultureInfo.InvariantCulture);
        var zona = TimeZoneInfo.FindSystemTimeZoneById(o.ZonaHoraria);
        logger.LogInformation("DiferenciasFiscalesAwWorker iniciado: {Dia} {Hora} ({Zona}).", o.DiaSemana, o.HoraLocal, o.ZonaHoraria);
        while (!stoppingToken.IsCancellationRequested)
        {
            var espera = ProximaEjecucion(time.GetUtcNow(), o.DiaSemana, hora, zona) - time.GetUtcNow();
            try
            {
                await Task.Delay(espera < TimeSpan.Zero ? TimeSpan.Zero : espera, time, stoppingToken);
                await EjecutarUnaVezAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // El handler ya guardó la corrida como fallida; aquí solo se registra.
                logger.LogError(ex, "DiferenciasFiscalesAwWorker: la corrida falló; se reintenta la próxima semana.");
            }
        }
    }

    internal async Task EjecutarUnaVezAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(DiferenciasFiscalesAwWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var r = await sp.GetRequiredService<ISender>().Send(
            new EjecutarCorridaDiferenciasFiscalesAwCommand(OrigenCorridaDiferenciaFiscal.Programada, Actor), ct);
        logger.LogInformation("DiferenciasFiscalesAwWorker: corrida {Id} con {Diferencias} diferencias pendientes.", r.Id, r.Diferencias);
    }

    /// <summary>Siguiente instante UTC estrictamente posterior a <paramref name="ahoraUtc"/> que cae en el día y hora locales.</summary>
    public static DateTimeOffset ProximaEjecucion(DateTimeOffset ahoraUtc, DayOfWeek dia, TimeOnly hora, TimeZoneInfo zona)
    {
        var local = TimeZoneInfo.ConvertTime(ahoraUtc, zona);
        var dias = ((int)dia - (int)local.DayOfWeek + 7) % 7;
        for (var i = 0; i < 2; i++, dias += 7)
        {
            var fecha = DateOnly.FromDateTime(local.Date).AddDays(dias).ToDateTime(hora);
            // Hora inexistente por cambio de horario: se toma la hora siguiente.
            if (zona.IsInvalidTime(fecha)) fecha = fecha.AddHours(1);
            var candidato = new DateTimeOffset(fecha, zona.GetUtcOffset(fecha));
            if (candidato > ahoraUtc) return candidato.ToUniversalTime();
        }
        throw new InvalidOperationException("No se pudo calcular la próxima corrida.");
    }
}
