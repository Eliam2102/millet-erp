using Serilog.Core;
using Serilog.Events;

namespace Millet.SharedKernel.Infrastructure.Logging;

/// <summary>
/// Enriquecedor de Serilog que enmascara propiedades sensibles en log events.
/// Política balanceada para contexto mexicano (ver ADR-0006):
///
/// - <b>Enmascarar siempre:</b> password, token, bearer, authorization, apiKey,
///   clientSecret, curp, clabe, numeroCuenta, numeroTarjeta, cvv,
///   nombrePersonaFisica, apellidoPaterno, apellidoMaterno, emailPersonal,
///   telefonoPersonal.
/// - <b>NO enmascarar (decisión explícita):</b> rfc, domicilioFiscal,
///   razonSocial, nombreComercial — son cuasi-públicos (aparecen en CFDIs y
///   reportes oficiales); enmascararlos haría inservibles los logs para
///   debugging.
///
/// El formato deja los primeros 2 y últimos 2 caracteres visibles
/// (ej. "AB**********CD"). Valores de 4 chars o menos se enmascaran completos.
/// </summary>
public sealed class MaskSensitivePropertiesEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "token",
        "bearer",
        "authorization",
        "apiKey",
        "clientSecret",
        "curp",
        "clabe",
        "numeroCuenta",
        "numeroTarjeta",
        "cvv",
        "nombrePersonaFisica",
        "apellidoPaterno",
        "apellidoMaterno",
        "emailPersonal",
        "telefonoPersonal",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        // Snapshot porque vamos a modificar el log event durante el iter.
        var properties = logEvent.Properties.ToList();

        foreach (var (key, value) in properties)
        {
            if (!SensitiveKeys.Contains(key)) continue;

            var maskedValue = MaskValue(value);
            if (maskedValue is not null)
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(key, maskedValue));
            }
        }
    }

    private static string? MaskValue(LogEventPropertyValue value)
    {
        if (value is not ScalarValue scalar) return null;
        if (scalar.Value is not string str) return null;
        return Mask(str);
    }

    /// <summary>Enmascara una cadena dejando los primeros 2 y últimos 2 chars visibles.</summary>
    public static string Mask(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length <= 4) return new string('*', value.Length);
        return string.Concat(value.AsSpan(0, 2), new string('*', value.Length - 4), value.AsSpan(value.Length - 2, 2));
    }
}
