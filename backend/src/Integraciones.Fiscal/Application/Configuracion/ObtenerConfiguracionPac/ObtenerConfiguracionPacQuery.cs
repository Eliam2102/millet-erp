using MediatR;
using Millet.Integraciones.Fiscal.Application.Configuracion;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.ObtenerConfiguracionPac;

/// <summary>
/// Obtiene la configuración del PAC de una empresa para un proveedor.
/// Retorna <c>null</c> si no existe (no es error — el frontend renderiza
/// el formulario en modo "crear").
///
/// <para>El <c>ApiKey</c> regresa siempre como <c>"••••"</c>; el
/// plaintext NUNCA sale del backend.</para>
/// </summary>
public sealed record ObtenerConfiguracionPacQuery(
    Guid EmpresaId,
    ProveedorPac Proveedor) : IRequest<ConfiguracionPacResponse?>;
