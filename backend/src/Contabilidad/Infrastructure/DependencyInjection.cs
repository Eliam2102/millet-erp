using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Infrastructure.PublicAdapters;

namespace Millet.Contabilidad.Infrastructure;

/// <summary>
/// Wiring de DI del módulo Contabilidad. El DbContext se registra en <c>Program.cs</c> de Api
/// (mismo patrón que los demás módulos). La configuración de formato (<c>Contabilidad:Catalogo</c>)
/// se valida al arrancar: una configuración inconsistente tumba el host con mensaje claro (§20.1).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddContabilidadModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogoOpciones>()
            .Bind(configuration.GetSection(CatalogoOpciones.Seccion))
            .PostConfigure(o => o.AplicarDefaults())
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CatalogoOpciones>, CatalogoOpcionesValidator>();
        services.AddSingleton(sp => new FormatoCatalogo(sp.GetRequiredService<IOptions<CatalogoOpciones>>().Value));
        services.AddScoped<ICuentaContableReadPort, CuentaContableReadAdapter>();

        // F1-CON-02: reglas de dimensión. Los puertos ICentroCostoContabilidadPort e ISucursalContabilidadPort los
        // implementan sus dueños (CentrosCosto, Compartido) y se cablean en Program.cs.
        services.AddOptions<DimensionesOpciones>()
            .Bind(configuration.GetSection(DimensionesOpciones.Seccion))
            .PostConfigure(o => o.AplicarDefaults())
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DimensionesOpciones>, DimensionesOpcionesValidator>();
        services.AddScoped<ValidadorDimensiones>();
        services.AddScoped<IDimensionContableValidacionPort>(sp => sp.GetRequiredService<ValidadorDimensiones>());
        services.AddScoped<AlcanceSucursalContable>();
        services.AddScoped<LecturaMovimientos>();

        // F1-CON-03: contrato público de consulta de periodo y candado de movimientos (falla cerrada, D9).
        services.AddScoped<IPeriodoContableConsultaPort, PeriodoContableConsultaAdapter>();
        services.AddScoped<VerificadorPeriodoContable>();
        return services;
    }

    private sealed class DimensionesOpcionesValidator : IValidateOptions<DimensionesOpciones>
    {
        public ValidateOptionsResult Validate(string? name, DimensionesOpciones options)
        {
            var errores = options.Validar().ToList();
            return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores.Select(e => $"{DimensionesOpciones.Seccion}: {e}"));
        }
    }

    /// <summary>Convierte cada inconsistencia en un mensaje de arranque legible.</summary>
    private sealed class CatalogoOpcionesValidator : IValidateOptions<CatalogoOpciones>
    {
        public ValidateOptionsResult Validate(string? name, CatalogoOpciones options)
        {
            var errores = options.Validar().ToList();
            return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores.Select(e => $"Contabilidad:Catalogo: {e}"));
        }
    }
}
