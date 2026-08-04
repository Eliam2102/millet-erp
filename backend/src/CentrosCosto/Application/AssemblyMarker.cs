namespace Millet.CentrosCosto.Application;

/// <summary>
/// Marker para que <c>Program.cs</c> de Api pueda resolver el assembly del
/// módulo Centros de Costo al registrar MediatR + FluentValidation
/// (CECO-PR2, mismo patrón que CxC/Tesorería).
/// </summary>
public static class CentrosCostoAssemblyMarker
{
    public static readonly System.Reflection.Assembly Assembly = typeof(CentrosCostoAssemblyMarker).Assembly;
}
