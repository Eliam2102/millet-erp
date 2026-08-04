using Millet.Administracion.Domain;

namespace Millet.Administracion.Application.Parametros;

/// <summary>
/// Respuesta del endpoint <c>GET /api/v1/admin/parametros</c> y
/// <c>PATCH /api/v1/admin/parametros/{clave}</c>.
/// </summary>
public sealed record ParametroResponse(
    Guid Id,
    string Clave,
    string Valor,
    TipoParametro Tipo,
    string? Modulo,
    string Descripcion,
    int Version);

public sealed record ListarParametrosResponse(IReadOnlyList<ParametroResponse> Items);
