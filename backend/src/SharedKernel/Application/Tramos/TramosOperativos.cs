namespace Millet.SharedKernel.Application.Tramos;

/// <summary>Tramo conocido: clave estable, nombre y módulo.</summary>
public sealed record TramoOperativoInfo(string Clave, string Nombre, string Modulo);

/// <summary>M-303: catálogo de tramos que se pueden detener sin apagar la aplicación.</summary>
public static class TramosOperativos
{
    public const string AwIngestaPedidos = "aw.ingesta-pedidos";
    public const string AwRetorno = "aw.retorno";
    public const string AwPinturas = "aw.pinturas";
    public const string PtEscaneos = "pt.escaneos";
    public const string PtAlbaranesAw = "pt.albaranes-aw";
    public const string PtCaptura = "pt.captura";

    public static IReadOnlyList<TramoOperativoInfo> Todos { get; } =
    [
        new(AwIngestaPedidos, "Lectura de pedidos de A+W para facturar", "facturacion"),
        new(AwRetorno, "Retorno de estatus a A+W (70, 72, 115)", "integraciones_aw"),
        new(AwPinturas, "Importación de pedidos de Planta Pintura", "facturacion"),
        new(PtEscaneos, "Entrada de piezas escaneadas en A+W a producto terminado", "producto_terminado"),
        new(PtAlbaranesAw, "Descuento de producto terminado por albaranes de A+W", "producto_terminado"),
        new(PtCaptura, "Captura de producto terminado (salidas, entregas, casos)", "producto_terminado"),
    ];

    public static TramoOperativoInfo? Buscar(string clave) => Todos.FirstOrDefault(t => t.Clave == clave);

    /// <summary>Espejo de <c>PermisosCanonicos.CompartidoTramosGestionar</c> (Compartido no referencia Identidad).</summary>
    public const string PermisoGestionar = "compartido.tramos.gestionar";

    /// <summary>Paso de incidencia que deja el tramo detenido.</summary>
    public const string PasoIncidencia = "plt.tramo.detenido";
}

/// <summary>
/// M-303: lo que consultan los workers y la captura. <see cref="DetenidoAsync"/> se llama al inicio de cada ciclo; detenido, el ciclo no lee.
/// <see cref="RegistrarEventoCorrectoAsync"/> guarda el último ciclo que terminó bien (lo que queda en la incidencia al detener).
/// </summary>
public interface ITramosOperativosPort
{
    Task<bool> DetenidoAsync(string clave, CancellationToken ct);

    Task RegistrarEventoCorrectoAsync(string clave, string detalle, CancellationToken ct);
}

/// <summary>Uso desde un worker: sin el puerto registrado (pruebas, host mínimo) el tramo se considera en marcha.</summary>
public static class TramosGuard
{
    public static async Task<bool> DetenidoAsync(IServiceProvider sp, string clave, CancellationToken ct) =>
        sp.GetService(typeof(ITramosOperativosPort)) is ITramosOperativosPort p && await p.DetenidoAsync(clave, ct);

    public static Task EventoCorrectoAsync(IServiceProvider sp, string clave, string detalle, CancellationToken ct) =>
        sp.GetService(typeof(ITramosOperativosPort)) is ITramosOperativosPort p ? p.RegistrarEventoCorrectoAsync(clave, detalle, ct) : Task.CompletedTask;
}

/// <summary>El tramo está detenido: quien escribe hacia fuera lo trata como «sin conexión» y deja el aviso pendiente.</summary>
public sealed class TramoDetenidoException(string clave) : Exception($"El tramo {clave} está detenido.");
