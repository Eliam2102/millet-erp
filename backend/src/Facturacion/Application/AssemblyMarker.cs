using System.Reflection;

namespace Millet.Facturacion.Application;

/// <summary>
/// Marker para escanear el assembly de Application desde
/// <c>AddMilletApplication(...)</c> en <c>Program.cs</c> (registra
/// MediatR handlers + FluentValidation validators + Mapster).
///
/// <para>
/// F0-PR1 no trae handlers todavía — el módulo arranca con la fundación
/// (DbContext + puertos + smoke). El primer comando (<c>EmitirFacturaVentaCommand</c>)
/// entra en F1-PR1; registrar el assembly desde ahora deja el wiring listo
/// y no duplica ediciones de <c>Program.cs</c>.
/// </para>
/// </summary>
public static class AssemblyMarker
{
    public static Assembly Assembly => typeof(AssemblyMarker).Assembly;
}
