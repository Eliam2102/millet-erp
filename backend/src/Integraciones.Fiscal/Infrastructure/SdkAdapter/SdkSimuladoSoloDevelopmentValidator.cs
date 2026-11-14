using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// M-293b: <c>IntegracionesFiscal:Sdk:Modo=Simulado</c> solo se acepta en Development. Con otro ambiente la validación de
/// opciones al arrancar falla, así que un SDK simulado nunca puede quedar encendido en QA ni en producción.
/// </summary>
public sealed class SdkSimuladoSoloDevelopmentValidator(IHostEnvironment entorno) : IValidateOptions<FiscalApiSdkAdapterOptions>
{
    public ValidateOptionsResult Validate(string? name, FiscalApiSdkAdapterOptions options)
    {
        if (!string.Equals(options.Modo, FiscalApiSdkAdapterOptions.ModoReal, StringComparison.OrdinalIgnoreCase)
            && !options.EsSimulado)
            return ValidateOptionsResult.Fail(
                $"IntegracionesFiscal:Sdk:Modo '{options.Modo}' no es válido: usa '{FiscalApiSdkAdapterOptions.ModoReal}' o '{FiscalApiSdkAdapterOptions.ModoSimulado}'.");
        if (options.EsSimulado && !entorno.IsDevelopment())
            return ValidateOptionsResult.Fail(
                $"IntegracionesFiscal:Sdk:Modo=Simulado solo se permite en Development (ambiente actual: {entorno.EnvironmentName}).");
        return ValidateOptionsResult.Success;
    }
}
