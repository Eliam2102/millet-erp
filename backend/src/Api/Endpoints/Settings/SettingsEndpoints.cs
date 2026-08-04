using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Settings;

namespace Millet.Api.Endpoints.Settings;

/// <summary>
/// Endpoints HTTP genéricos que el área de Administración consume para
/// listar y editar los settings de cualquier módulo de negocio.
/// El módulo dueño implementa <see cref="ISettingsSchemaProvider"/> y
/// se registra en DI; este endpoint resuelve la instancia correcta
/// por <c>{modulo}</c> en el path.
///
/// <list type="bullet">
///   <item><c>GET /api/v1/{modulo}/settings/schema</c> — lista de
///         <see cref="SettingItem"/> filtrada por permisos del usuario.</item>
///   <item><c>PATCH /api/v1/{modulo}/settings/{clave}</c> — aplica un
///         valor al setting; valida tipo + <see cref="ValidacionSetting"/>;
///         requiere <c>Idempotency-Key</c> (ADR-0020).</item>
/// </list>
/// Ver ADR-0034 §SettingsSchema. F-Admin-PR1.1.
/// </summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/{modulo}/settings")
            .WithTags("Settings");

        group.MapGet("/schema", GetSchemaAsync)
            .RequireAuthorization()
            .WithName("GetSettingsSchema")
            .WithSummary("Schema declarativo de settings de un módulo")
            .Produces<SettingsSchemaResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{clave}", PatchSettingAsync)
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization()
            .WithName("PatchSetting")
            .WithSummary("Aplicar un valor a un setting individual de un módulo")
            .Produces<SettingItem>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetSchemaAsync(
        string modulo,
        IEnumerable<ISettingsSchemaProvider> providers,
        IAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providers, modulo);
        if (provider is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Módulo no encontrado",
                Detail = $"No hay un ISettingsSchemaProvider registrado para módulo '{modulo}'.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var items = await provider.ObtenerSchemaAsync(cancellationToken);
        var filtered = new List<SettingItem>(items.Count);

        foreach (var item in items)
        {
            var auth = await authorization.AuthorizeAsync(
                httpContext.User,
                resource: null,
                new PermissionRequirement(item.PermisoLeer));
            if (auth.Succeeded)
            {
                filtered.Add(item);
            }
        }

        return Results.Ok(new SettingsSchemaResponse(filtered));
    }

    private static async Task<IResult> PatchSettingAsync(
        string modulo,
        string clave,
        [FromBody] AplicarSettingPatchRequest request,
        IEnumerable<ISettingsSchemaProvider> providers,
        IAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providers, modulo);
        if (provider is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Módulo no encontrado",
                Detail = $"No hay un ISettingsSchemaProvider registrado para módulo '{modulo}'.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var items = await provider.ObtenerSchemaAsync(cancellationToken);
        var item = items.FirstOrDefault(i => i.Clave == clave);
        if (item is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Setting no encontrado",
                Detail = $"El módulo '{modulo}' no expone un setting con clave '{clave}'.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var permitido = await authorization.AuthorizeAsync(
            httpContext.User,
            resource: null,
            new PermissionRequirement(item.PermisoEditar));
        if (!permitido.Succeeded)
        {
            return Results.Forbid();
        }

        var validationErrors = ValidarValor(item, request.Valor);
        if (validationErrors.Count > 0)
        {
            // 422 (UnprocessableEntity) — el body es sintácticamente válido
            // pero el valor no cumple el contrato del schema. 400 lo reserva
            // ASP.NET para parseo fallido.
            return Results.ValidationProblem(validationErrors, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var updated = await provider.AplicarPatchAsync(clave, request.Valor, cancellationToken);
        return Results.Ok(updated);
    }

    private static ISettingsSchemaProvider? ResolveProvider(
        IEnumerable<ISettingsSchemaProvider> providers,
        string modulo) =>
        providers.FirstOrDefault(p =>
            string.Equals(p.Modulo, modulo, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string[]> ValidarValor(
        SettingItem item,
        JsonElement valor)
    {
        var errors = new Dictionary<string, string[]>();
        switch (item.Tipo)
        {
            case TipoSetting.Booleano:
                if (valor.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors["valor"] = ["El valor debe ser un booleano (true/false)."];
                }
                break;

            case TipoSetting.Entero:
                if (valor.ValueKind is not JsonValueKind.Number || !valor.TryGetInt64(out var intValue))
                {
                    errors["valor"] = ["El valor debe ser un entero."];
                    break;
                }
                if (item.Validacion?.Min is decimal min && intValue < min)
                {
                    errors["valor"] = [$"El valor debe ser >= {min}."];
                }
                if (item.Validacion?.Max is decimal max && intValue > max)
                {
                    errors["valor"] = [$"El valor debe ser <= {max}."];
                }
                break;

            case TipoSetting.Numerico:
                if (valor.ValueKind is not JsonValueKind.Number || !valor.TryGetDecimal(out var decValue))
                {
                    errors["valor"] = ["El valor debe ser un número decimal."];
                    break;
                }
                if (item.Validacion?.Min is decimal dmin && decValue < dmin)
                {
                    errors["valor"] = [$"El valor debe ser >= {dmin}."];
                }
                if (item.Validacion?.Max is decimal dmax && decValue > dmax)
                {
                    errors["valor"] = [$"El valor debe ser <= {dmax}."];
                }
                break;

            case TipoSetting.Texto:
                if (valor.ValueKind is not JsonValueKind.String)
                {
                    errors["valor"] = ["El valor debe ser una cadena."];
                    break;
                }
                if (item.Validacion?.Pattern is { Length: > 0 } pattern)
                {
                    var input = valor.GetString() ?? string.Empty;
                    if (!Regex.IsMatch(input, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
                    {
                        errors["valor"] = [$"El valor no cumple el patrón '{pattern}'."];
                    }
                }
                break;

            case TipoSetting.Lista:
                if (valor.ValueKind is not JsonValueKind.String)
                {
                    errors["valor"] = ["El valor debe ser una cadena (enum)."];
                    break;
                }
                var enumValue = valor.GetString() ?? string.Empty;
                if (item.Validacion?.Opciones is { Count: > 0 } opciones && !opciones.Contains(enumValue))
                {
                    errors["valor"] = [$"El valor debe ser una de: {string.Join(", ", opciones)}."];
                }
                break;

            case TipoSetting.Fecha:
                if (valor.ValueKind is not JsonValueKind.String ||
                    !DateTime.TryParse(valor.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out _))
                {
                    errors["valor"] = ["El valor debe ser una fecha en formato ISO 8601."];
                }
                break;
        }
        return errors;
    }
}
