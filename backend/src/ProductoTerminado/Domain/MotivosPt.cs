namespace Millet.ProductoTerminado.Domain;

/// <summary>Códigos de motivo de los escaneos pospuestos, en excepción o en reposición.</summary>
public static class MotivosPt
{
    public const string PedidoInexistente = "PT_PEDIDO_INEXISTENTE";
    public const string PedidoCancelado = "PT_PEDIDO_CANCELADO";
    public const string PosicionInexistente = "PT_POSICION_INEXISTENTE";
    public const string PiezaExcedePosicion = "PT_PIEZA_EXCEDE_POSICION";
    public const string CantidadNoEntera = "PT_CANTIDAD_NO_ENTERA";
    public const string SucursalSinClaveAw = "PT_SUCURSAL_SIN_CLAVE_AW";
    public const string IdentidadIncompleta = "PT_IDENTIDAD_INCOMPLETA";
    public const string PiezaDuplicada = "PT_PIEZA_DUPLICADA";
    public const string Reposicion = "PT_REPOSICION";

    // Motivos de la retención/liberación del pedido (PT5.2).
    public const string SaldoPendiente = "SaldoPendiente";
    public const string SaldoCubierto = "SaldoCubierto";
    public const string Reparto = "Reparto";
    public const string DecisionCxc = "DecisionCxc";
}
