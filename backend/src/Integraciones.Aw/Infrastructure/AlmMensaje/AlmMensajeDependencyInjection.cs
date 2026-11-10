using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.AppAlmacenes;

namespace Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

public static class AlmMensajeDependencyInjection
{
    /// <summary>
    /// Origen de <c>alm_mensaje</c> (Almacenes → ERP, acuerdo 5-nov): <c>Simulado</c> (por omisión) o <c>Sql</c>. El simulado es
    /// singleton y también se registra por su tipo para que las pruebas lo manejen. El worker sigue apagado salvo
    /// <c>Integraciones:AlmMensaje:Habilitado=true</c>.
    /// </summary>
    public static IServiceCollection AddAlmMensajeOrigen(this IServiceCollection services, IConfiguration configuration)
    {
        var origen = configuration[$"{AlmMensajeOptions.SectionName}:{nameof(AlmMensajeOptions.Origen)}"];
        if (string.Equals(origen, "Sql", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAlmMensajeOrigen, SqlAlmMensajeOrigen>();
            services.AddSingleton<IAlmCargaInicialOrigen, SqlAlmCargaInicialOrigen>(); // M-283, mismo origen y cadena
            return services;
        }
        if (!string.IsNullOrWhiteSpace(origen) && !string.Equals(origen, "Simulado", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Integraciones:AlmMensaje:Origen '{origen}' no válido (Simulado | Sql).");
        services.AddSingleton<SimuladoAlmMensajeOrigen>();
        services.AddSingleton<IAlmMensajeOrigen>(sp => sp.GetRequiredService<SimuladoAlmMensajeOrigen>());
        services.AddSingleton<SimuladoAlmCargaInicialOrigen>();
        services.AddSingleton<IAlmCargaInicialOrigen>(sp => sp.GetRequiredService<SimuladoAlmCargaInicialOrigen>());
        return services;
    }
}
