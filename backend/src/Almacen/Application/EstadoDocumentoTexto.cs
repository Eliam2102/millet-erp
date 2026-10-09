namespace Millet.Almacen.Application;

internal static class EstadoDocumentoTexto
{
    public static string Describir(string estado) => estado switch
    {
        "Borrador" => "en borrador",
        "EnAutorizacionJefeCompras" => "en autorización del jefe de compras",
        "EnAutorizacionDireccion" => "en autorización de Dirección",
        "EnAutorizacion" => "en autorización",
        "EnSurtido" => "en surtido",
        "CerradaSinSurtir" => "cerrada sin surtir",
        "CerradaSurtidaParcial" => "cerrada con surtido parcial",
        _ => estado.ToLowerInvariant(),
    };
}
