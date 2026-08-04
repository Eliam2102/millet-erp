namespace Millet.CuentasPorCobrar.Application;

/// <summary>
/// Marker para que <c>Program.cs</c> de Api pueda resolver el assembly
/// de Application del módulo CxC al registrar MediatR + FluentValidation
/// (CXC-PR1, mismo patrón que CxP).
/// </summary>
public static class CuentasPorCobrarAssemblyMarker
{
    public static readonly System.Reflection.Assembly Assembly = typeof(CuentasPorCobrarAssemblyMarker).Assembly;
}
