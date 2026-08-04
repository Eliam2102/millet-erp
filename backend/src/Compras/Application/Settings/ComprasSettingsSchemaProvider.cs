using System.Text.Json;
using MediatR;
using Millet.SharedKernel.Application.Settings;

namespace Millet.Compras.Application.Settings;

/// <summary>
/// Provider exemplar del contrato <see cref="ISettingsSchemaProvider"/>
/// (ADR-0034, A7=b). Expone los settings del módulo Compras al área
/// transversal <c>/admin</c>.
///
/// <para>
/// El único setting actual es <c>AutoGenerarOcAlAutorizar</c> (ADR-0033),
/// que se marca <see cref="DisplayMode.Custom"/> con
/// <c>RutaCustom = "/compras/configuracion"</c> porque tiene efectos
/// cross-cutting en el flujo RQ→OC (la UI custom muestra advertencias
/// contextuales). El endpoint genérico de PATCH sigue funcionando para
/// automation/scripts; la UI auto-rendered solo lo lista como link.
/// </para>
/// </summary>
public sealed class ComprasSettingsSchemaProvider : ISettingsSchemaProvider
{
    private const string ClaveAutoGenerarOc = "AutoGenerarOcAlAutorizar";

    private readonly IMediator _mediator;

    public ComprasSettingsSchemaProvider(IMediator mediator)
    {
        _mediator = mediator;
    }

    public string Modulo => "compras";

    public async Task<IReadOnlyList<SettingItem>> ObtenerSchemaAsync(
        CancellationToken cancellationToken)
    {
        var current = await _mediator.Send(new ObtenerComprasSettingsQuery(), cancellationToken);

        return new[]
        {
            new SettingItem(
                Clave: ClaveAutoGenerarOc,
                Etiqueta: "Auto-generar OC al autorizar requisición",
                Descripcion:
                    "Si está activado, al autorizar una requisición con saldo de " +
                    "compra el handler genera una OC borrador automáticamente. " +
                    "Si está desactivado (default), el comprador convierte la " +
                    "requisición a OC manualmente (1:1) o consolida varias en una " +
                    "OC nueva. Ver ADR-0033.",
                Tipo: TipoSetting.Booleano,
                Default: false,
                Valor: current.AutoGenerarOcAlAutorizar,
                Validacion: null,
                // Strings canónicos (Identidad.Domain.PermisosCanonicos no está
                // accesible aquí — Compras no referencia Identidad). Si cambia
                // el canónico, los tests del seed de permisos lo detectan.
                PermisoLeer: "compras.configuracion.leer",
                PermisoEditar: "compras.configuracion.editar",
                Mostrar: DisplayMode.Custom,
                RutaCustom: "/compras/configuracion",
                AlertaCambio:
                    "Este cambio afecta el flujo de autorización de requisiciones. " +
                    "Las requisiciones autorizadas antes del cambio mantienen su " +
                    "comportamiento original."),
        };
    }

    public async Task<SettingItem> AplicarPatchAsync(
        string clave,
        JsonElement valor,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(clave, ClaveAutoGenerarOc, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Clave '{clave}' no existe en el schema de Compras. El endpoint " +
                "genérico debió haber retornado 404 antes de invocar este método.");
        }

        // El endpoint genérico ya validó tipo Bool antes de invocar.
        var flag = valor.GetBoolean();

        await _mediator.Send(
            new ActualizarComprasSettingsCommand(AutoGenerarOcAlAutorizar: flag),
            cancellationToken);

        // Re-leer el schema actualizado para retornar el SettingItem con
        // el Valor reciente. Costo aceptable (1 query) por simplicidad
        // — el caller espera ver el estado post-PATCH.
        var items = await ObtenerSchemaAsync(cancellationToken);
        return items.First(i => i.Clave == ClaveAutoGenerarOc);
    }
}
