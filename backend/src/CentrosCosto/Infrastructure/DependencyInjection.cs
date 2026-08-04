using Microsoft.Extensions.DependencyInjection;

namespace Millet.CentrosCosto.Infrastructure;

/// <summary>
/// Wiring de DI del módulo Centros de Costo.
///
/// <para>
/// Con la separación total (CECO-PR4, levantamiento §7.4) el módulo no
/// tiene adapters de puertos externos: el <c>ISucursalReadPort</c> del
/// modelo anterior se eliminó. El DbContext se registra en
/// <c>Program.cs</c> de Api (mismo patrón que los demás módulos). Este
/// shell queda para los registros de CECO-PR6+ (alcance) y el read-port
/// PÚBLICO de la Fase E (donde CeCo es el servidor, no el cliente).
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCentrosCostoModule(this IServiceCollection services)
    {
        // Evaluador del alcance congelado (CECO-PR6): scoped como el
        // DbContext y el contexto de usuario que consume.
        services.AddScoped<
            Application.Asignaciones.Alcance.IAlcanceDim3Evaluator,
            Application.Asignaciones.Alcance.AlcanceDim3Evaluator>();

        // Read-port PÚBLICO de la Fase E (ADR-0050): CeCo es el SERVIDOR.
        // Resuelve dim3Id → clave/nombre/activa para el display heredado en
        // documentos (Compras/Almacén), sin filtro de alcance e incluyendo
        // inactivas. Consumidores se cablean en PR3/PR4/PR5.
        services.AddScoped<
            Application.PublicPorts.IDim3ReadPort,
            Infrastructure.PublicAdapters.Dim3ReadAdapter>();

        return services;
    }
}
