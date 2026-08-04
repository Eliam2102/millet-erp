using MediatR;

namespace Millet.Compras.Application.Settings;

/// <summary>
/// Command para actualizar (upsert) los settings del módulo Compras
/// para la empresa actual. Permiso requerido en el endpoint:
/// <c>compras.configuracion.editar</c>.
///
/// <para>
/// PATCH parcial: solo los campos no-null se aplican. Usar <c>null</c>
/// significa "no tocar este campo".
/// </para>
/// </summary>
/// <param name="AutoGenerarOcAlAutorizar">
/// Si se provee, sobrescribe el flag. <c>null</c> = no tocar.
/// </param>
public sealed record ActualizarComprasSettingsCommand(
    bool? AutoGenerarOcAlAutorizar) : IRequest<ComprasSettingsResponse>;
