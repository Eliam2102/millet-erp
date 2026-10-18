using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Integraciones.Aw.Application.DiferenciasFiscales;
using Millet.Integraciones.Aw.Application.Workers;

namespace Millet.Integraciones.Aw.Infrastructure.DiferenciasFiscales;

/// <summary>
/// Wiring del reporte de diferencias fiscales A+W vs ERP (U1.8b). El servicio usa los puertos de lectura ya
/// registrados (clientes A+W vía selector Real/Demo); no registra adaptadores propios ni otra sincronización.
/// El worker semanal solo existe con <c>ProgramacionHabilitada=true</c> (apagado por defecto).
/// </summary>
public static class DiferenciasFiscalesDependencyInjection
{
    public static IServiceCollection AddIntegracionesAwDiferenciasFiscales(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DiferenciasFiscalesAwOptions.SectionName);
        services.AddOptions<DiferenciasFiscalesAwOptions>().Bind(section)
            .Validate(o => TimeOnly.TryParseExact(o.HoraLocal, "HH:mm", out _), "HoraLocal debe tener formato HH:mm.")
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.ZonaHoraria, out _), "ZonaHoraria no existe.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<DiferenciasFiscalesAwService>();

        if ((section.Get<DiferenciasFiscalesAwOptions>() ?? new()).ProgramacionHabilitada)
            services.AddHostedService<DiferenciasFiscalesAwWorker>();
        return services;
    }
}
