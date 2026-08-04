namespace Millet.Compras.Application.Settings;

/// <summary>
/// DTO de respuesta para los settings del módulo Compras de una empresa.
/// Mirror del agregado <see cref="Domain.ComprasSettings"/>; campos
/// estables al wire (no se expone <c>Id</c> ni timestamps internos).
/// </summary>
/// <param name="EmpresaId">Empresa propietaria de la configuración.</param>
/// <param name="AutoGenerarOcAlAutorizar">
/// Si <c>true</c>, el handler de Autorizar RQ genera una OC borrador
/// síncrona cuando hay saldo de compra (Narrativa A del diseño original
/// RQ §A3, hoy stubbed por <c>InMemoryGenerarSolicitudCompraPort</c>).
/// Si <c>false</c> (default), el comprador convierte manualmente
/// (Narrativa B del diseño OC §3.bis.1).
/// </param>
public sealed record ComprasSettingsResponse(
    Guid EmpresaId,
    bool AutoGenerarOcAlAutorizar);
