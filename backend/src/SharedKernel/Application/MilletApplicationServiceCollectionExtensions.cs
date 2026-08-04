using System.Reflection;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Millet.SharedKernel.Application;

/// <summary>
/// Helper que centraliza el wiring de la infraestructura de aplicación
/// (CQRS via MediatR, validación via FluentValidation, mapping via
/// Mapster) para los assemblies dados.
///
/// Cada módulo que defina handlers, validators y/o mappings de Mapster
/// se incluye pasando su assembly desde <c>Program.cs</c>:
///
/// <code>
/// builder.Services.AddMilletApplication(
///     typeof(Program).Assembly,
///     typeof(Millet.Compras.Application.CrearRequisicion.CrearRequisicionCommand).Assembly,
///     // typeof(Millet.Facturacion.Application.AlgunCommand).Assembly,
///     // ...
/// );
/// </code>
///
/// Sustituye el wiring inline introducido en F0-PR1 cuando solo había
/// un assembly (Api). Crece linealmente con la lista de módulos: cada
/// módulo nuevo agrega un <c>typeof(...)</c>.
/// </summary>
public static class MilletApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddMilletApplication(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
        {
            throw new ArgumentException(
                "Se requiere al menos un assembly. Pasar typeof(Program).Assembly y los assemblies de cada módulo con handlers/validators/mappings.",
                nameof(assemblies));
        }

        // CQRS: registra IRequest/IRequestHandler/INotificationHandler de
        // todos los assemblies indicados.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(assemblies));

        // Validación: descubre AbstractValidator<T> en los assemblies.
        // El pipeline behavior los invoca antes de cada handler y traduce
        // failures a ValidationException (HTTP 400, ADR-0010).
        services.AddValidatorsFromAssemblies(assemblies);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));

        // Mapping: TypeAdapterConfig.GlobalSettings.Scan inspecciona los
        // assemblies por IRegister para registrar mappings explícitos. Si
        // un módulo no usa IRegister, los mappings por convención (mismo
        // nombre) siguen funcionando sin registro.
        var mapsterConfig = TypeAdapterConfig.GlobalSettings;
        foreach (var assembly in assemblies)
        {
            mapsterConfig.Scan(assembly);
        }
        services.AddSingleton(mapsterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
