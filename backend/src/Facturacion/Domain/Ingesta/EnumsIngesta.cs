namespace Millet.Facturacion.Domain.Ingesta;

/// <summary>Operación que A+W escribe en la cola de solicitudes (D18, §12.1).</summary>
public enum OperacionAw : short
{
    Alta = 1,
    Modificacion = 2,
    Cancelacion = 3,
}

/// <summary>
/// Resultado que el ERP escribe de vuelta tras procesar una solicitud (§12.1).
/// </summary>
public enum ResultadoSolicitudAw : short
{
    Aplicada = 1,
    Rechazada = 2,
    Pospuesta = 3,
    Error = 4,
}

/// <summary>
/// Estado en <c>ingesta_control</c> — la fuente de verdad de la idempotencia
/// (sobrevive aunque A+W reescriba algo). §5 diseño.
/// </summary>
public enum EstadoIngesta : short
{
    Importado = 1,
    Excepcion = 2,
    Facturado = 3,
    Cancelado = 4,
    Ignorado = 5,
}

/// <summary>Motivo por el que un registro cae a la bandeja de excepciones (§12.1).</summary>
public enum MotivoExcepcion : short
{
    ClienteNoExiste = 1,
    ArticuloNoExiste = 2,
    ProductoSinClaveSat = 3,
    AlmacenNoAsignado = 4,
    DivisaInvalida = 5,
    TotalesNoCuadran = 6,
    CancelacionSobreFacturado = 7,
    ModificacionSobreFacturado = 8,
    /// <summary>El GRUPPE del pedido no tiene canal activo con esa clave_aw (corregible en catálogo).</summary>
    CanalVentaSinClaveAw = 9,
    /// <summary>numero_sucursal sin sucursal activa con esa clave_aw (corregible en catálogo).</summary>
    SucursalSinClaveAw = 10,
    /// <summary>La clase no mapea a ComportamientoFiscal (gap G14; corregible vía vista u override).</summary>
    ComportamientoSinRegla = 11,
    /// <summary>Planta Pintura: el producto existe pero no está marcado como servicio de recubrimiento.</summary>
    ServicioNoMarcado = 12,
    /// <summary>Planta Pintura: el IVA de la orden no coincide con la tasa del producto.</summary>
    IvaNoCuadra = 13,
    /// <summary>Nota de crédito de A+W: la factura de origen no existe (o no está timbrada vigente, o es ambigua).</summary>
    NcFacturaNoExiste = 14,
    /// <summary>Nota de crédito de A+W: el cliente de la nota no es el de la factura de origen.</summary>
    NcClienteDistinto = 15,
    /// <summary>Nota de crédito de A+W: el importe excede el saldo de la factura de origen.</summary>
    NcImporteExcedeSaldo = 16,
    Otro = 99,
}
