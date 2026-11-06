using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Aviso a Fiscal de contingencias de timbrado abiertas cuando su mes ya cerró
/// (F6.8-a: una del 28-nov sigue abierta el 2-dic → Fiscal recibe la alerta). Un
/// correo por empresa con todas las pendientes; cada contingencia se avisa una vez
/// por correo y la pantalla de la bitácora las sigue mostrando como por vencer o vencidas.
/// </summary>
public sealed class ContingenciasAlertaWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ContingenciasAlertaOptions> options,
    ILogger<ContingenciasAlertaWorker> logger) : BackgroundService
{
    public const string Modulo = "facturacion";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.Disabled)
        {
            logger.LogInformation("[ContingenciasAlertaWorker] Disabled — loop no inicia.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.IntervalMinutes)), stoppingToken);
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ContingenciasAlertaWorker] Error inesperado.");
            }
        }
    }

    /// <summary>Internal para tests. Devuelve cuántas contingencias quedaron avisadas.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var clock = sp.GetRequiredService<IClock>();
        var correo = sp.GetRequiredService<ICorreoSaliente>();
        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(ContingenciasAlertaWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();

        var ahora = clock.UtcNow;
        var hoy = ZonaMillet.Hoy(ahora);
        var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
        // Abiertas cuya fecha límite cae en este mes o antes (su mes ya cerró).
        var pendientes = await db.ContingenciasTimbrado
            .Where(c => c.Estado == EstadoContingenciaTimbrado.Abierta && c.AlertaEnviadaAt == null
                && c.FechaLimite < inicioMes.AddMonths(1))
            .OrderBy(c => c.FechaLimite).ThenBy(c => c.OcurridaAt)
            .ToListAsync(cancellationToken);
        pendientes = [.. pendientes.Where(c => c.RequiereAlerta(hoy))];
        if (pendientes.Count == 0) return 0;

        var para = ContingenciasAlertaOptions.Validos(options.CurrentValue.Fiscal);
        if (para.Count == 0)
        {
            logger.LogWarning(
                "[ContingenciasAlertaWorker] Sin correos en {Seccion}:Fiscal; {Total} contingencia(s) por regularizar sin aviso.",
                ContingenciasAlertaOptions.SectionName, pendientes.Count);
            return 0;
        }

        var avisadas = 0;
        foreach (var grupo in pendientes.GroupBy(c => c.EmpresaId))
        {
            var lista = grupo.ToList();
            var resultado = await correo.EnviarAsync(new MensajeCorreo(
                grupo.Key, Modulo, "contingencias-timbrado", lista[0].Id, para,
                $"Contingencias de timbrado por regularizar ({lista.Count})", Html(lista, hoy)), cancellationToken);
            if (resultado.Estado == EstadoCorreoSaliente.Error)
            {
                logger.LogWarning("[ContingenciasAlertaWorker] El aviso no salió: {Codigo} {Motivo}.", resultado.Codigo, resultado.Motivo);
                continue;
            }
            foreach (var c in lista) c.MarcarAlertaEnviada(ahora);
            avisadas += lista.Count;
        }

        if (avisadas > 0) await db.SaveChangesAsync(cancellationToken);
        return avisadas;
    }

    private static string Html(IReadOnlyList<ContingenciaTimbrado> lista, DateOnly hoy)
    {
        var filas = string.Concat(lista.Select(c =>
            "<tr>" +
            $"<td>{ZonaMillet.ALocal(c.OcurridaAt):dd/MM/yyyy HH:mm}</td>" +
            $"<td>{WebUtility.HtmlEncode(c.Tipo.ToString())}</td>" +
            $"<td>{WebUtility.HtmlEncode(c.ComprobanteFolio ?? "Sin documento")}</td>" +
            $"<td>{WebUtility.HtmlEncode(c.Motivo)}</td>" +
            $"<td>{c.FechaLimite:dd/MM/yyyy}{(hoy > c.FechaLimite ? " (vencida)" : string.Empty)}</td>" +
            "</tr>"));
        return "<div style='font-family:Arial,sans-serif;max-width:720px;margin:auto'>" +
               "<h2>Contingencias de timbrado por regularizar</h2>" +
               "<p>El mes ya cerró y estas contingencias siguen abiertas. Deben regularizarse a más tardar el día 3 del mes siguiente.</p>" +
               "<table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse'>" +
               "<tr><th>Ocurrió</th><th>Tipo</th><th>Documento</th><th>Motivo</th><th>Regularizar a más tardar</th></tr>" +
               filas + "</table>" +
               "<p>Revísalas en el ERP, Facturación › Contingencias de timbrado.</p></div>";
    }
}

public sealed class ContingenciasAlertaOptions
{
    public const string SectionName = "Facturacion:Avisos:Contingencias";

    public bool Disabled { get; init; }

    public int IntervalMinutes { get; init; } = 60;

    /// <summary>Correos de Fiscal que reciben el aviso. Sin correos no sale y queda en el log.</summary>
    public List<string> Fiscal { get; set; } = [];

    internal static IReadOnlyList<string> Validos(IEnumerable<string> correos) =>
        [.. correos.Select(c => c.Trim()).Where(c => c.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase)];
}
