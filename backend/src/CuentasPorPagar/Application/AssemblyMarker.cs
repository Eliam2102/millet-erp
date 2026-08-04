namespace Millet.CuentasPorPagar.Application;

/// <summary>
/// Marker para que <c>AddMilletApplication</c> en <c>Program.cs</c> pueda
/// resolver el assembly de Application del módulo CxP cuando registre
/// MediatR + FluentValidation + Mapster (F0-PR1). En F1+ ya habrá tipos
/// concretos (commands/queries/handlers) y este marker se vuelve
/// redundante — se conserva como ancla del assembly hasta que esos
/// tipos existan.
/// </summary>
public static class CuentasPorPagarAssemblyMarker
{
    public static readonly System.Reflection.Assembly Assembly = typeof(CuentasPorPagarAssemblyMarker).Assembly;
}
