using System.Reflection;

namespace Millet.Almacen.Application;

/// <summary>
/// Marker para escanear el assembly de Application desde
/// <c>AddMilletApplication(...)</c> en <c>Program.cs</c> (registra
/// MediatR handlers + FluentValidation validators + Mapster).
/// </summary>
public static class AssemblyMarker
{
    public static Assembly Assembly => typeof(AssemblyMarker).Assembly;
}
