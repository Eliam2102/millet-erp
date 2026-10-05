namespace Millet.Integraciones.Aw.Application.Cambios;

/// <summary>
/// Sincronización por CDC (sección <c>IntegracionesAw:Cambios</c>; conexión <c>ConnectionStrings:AwCambiosDb</c>).
/// Apagada por defecto. Complementa (no reemplaza) el barrido completo de clientes/productos, que sigue siendo
/// la conciliación y la recuperación cuando el LSN expira.
/// </summary>
public sealed class AwCambiosOptions
{
    public const string SectionName = "IntegracionesAw:Cambios";
    public const string ConnectionStringName = "AwCambiosDb";

    public bool Habilitado { get; set; }
    public bool Clientes { get; set; } = true;
    public bool Productos { get; set; } = true;
    public int IntervaloSegundos { get; set; } = 30;
    public int TamanoLote { get; set; } = 500;
    public int SqlQueryTimeoutSeconds { get; set; } = 15;
    public int SqlConnectTimeoutSeconds { get; set; } = 15;
}
