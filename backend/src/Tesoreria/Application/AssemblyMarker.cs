namespace Millet.Tesoreria.Application;

/// <summary>
/// Marker para que <c>Program.cs</c> de Api pueda resolver el assembly
/// de Application del módulo Tesorería al registrar MediatR +
/// FluentValidation (TES-PR1, mismo patrón que CxP/CxC).
/// </summary>
public static class TesoreriaAssemblyMarker
{
    public static readonly System.Reflection.Assembly Assembly = typeof(TesoreriaAssemblyMarker).Assembly;
}
