namespace Millet.Contabilidad.Application;

/// <summary>Marker para registrar MediatR/FluentValidation del módulo desde <c>Program.cs</c>.</summary>
public static class ContabilidadAssemblyMarker
{
    public static readonly System.Reflection.Assembly Assembly = typeof(ContabilidadAssemblyMarker).Assembly;
}
