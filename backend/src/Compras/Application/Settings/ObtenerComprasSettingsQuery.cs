using MediatR;

namespace Millet.Compras.Application.Settings;

/// <summary>
/// Query que devuelve los settings del módulo Compras para la empresa
/// actual (del JWT). Si la fila no existe en BD, retorna la versión
/// default (<c>AutoGenerarOcAlAutorizar=false</c>) sin persistirla — el
/// upsert solo ocurre al cambiar el valor explícitamente.
/// </summary>
public sealed record ObtenerComprasSettingsQuery : IRequest<ComprasSettingsResponse>;
