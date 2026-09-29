using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Clientes;

/// <summary>
/// Wiring de la sincronización de clientes A+W (ADM-06). Independiente de pedidos: no toca
/// <c>AwIntegracionDb</c>. Con la config por defecto solo se enlazan opciones y se registra
/// (perezosamente) la fuente simulada VACÍA: no se registra el adaptador SQL ni se lee nada.
/// </summary>
public static class ClientesDependencyInjection
{
    public static IServiceCollection AddIntegracionesAwClientes(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AwClientesOptions.SectionName);
        services.AddOptions<AwClientesOptions>().Bind(section);
        var opciones = section.Get<AwClientesOptions>() ?? new AwClientesOptions();

        if (opciones.Origen == AwClientesOrigenTipo.Sql)
        {
            // Solo con connection string real (una ref KV sin resolver se trata como ausente).
            var cs = configuration.GetConnectionString(AwClientesOptions.ConnectionStringName);
            if (!string.IsNullOrWhiteSpace(cs)
                && !cs.StartsWith("@Microsoft.KeyVault", StringComparison.OrdinalIgnoreCase))
            {
                services.AddSingleton<IAwClientesOrigen>(sp => new AwClientesSqlOrigen(
                    new ClientesSqlConnectionFactory(cs),
                    sp.GetRequiredService<IOptions<AwClientesOptions>>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AwClientesSqlOrigen>>()));
            }
        }
        else
        {
            services.AddSingleton<IAwClientesOrigen>(sp =>
            {
                var archivo = sp.GetRequiredService<IOptions<AwClientesOptions>>().Value.ArchivoSimulado;
                return string.IsNullOrWhiteSpace(archivo)
                    ? new AwClientesOrigenSimulado()
                    : AwClientesOrigenSimulado.DesdeArchivo(archivo);
            });
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AwClientesSincronizador>();

        // El worker solo existe con ProgramacionHabilitada=true (apagado por defecto).
        if (opciones.ProgramacionHabilitada)
        {
            services.AddSingleton<AwClientesSyncWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<AwClientesSyncWorker>());
            services.AddHealthChecks().AddCheck<AwClientesSyncWorkerHealthCheck>(
                "aw-clientes-sync-worker", tags: ["liveness"]);
        }

        return services;
    }

    /// <summary>Conexión propia de clientes (no es el factory de pedidos); TLS verificado obligatorio.</summary>
    private sealed class ClientesSqlConnectionFactory : IIntegracionSqlConnectionFactory
    {
        private readonly string _connectionString;

        public ClientesSqlConnectionFactory(string connectionString)
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            if (b.Encrypt == SqlConnectionEncryptOption.Optional || b.TrustServerCertificate)
                throw new InvalidOperationException(
                    "ConnectionStrings:AwClientesDb exige TLS verificado (Encrypt=True, sin TrustServerCertificate).");
            _connectionString = b.ConnectionString;
        }

        public DbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}
