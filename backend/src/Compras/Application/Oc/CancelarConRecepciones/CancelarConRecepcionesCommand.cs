using MediatR;

namespace Millet.Compras.Application.Oc.CancelarConRecepciones;

/// <summary>
/// Cancela una OC que ya tiene recepciones parciales o completas
/// (F5-PR4). Las cantidades ya recibidas permanecen en las líneas
/// para preservar la trazabilidad contable. Para cada línea con RQ y
/// saldo no recibido, se libera la <c>cantidad_no_recibida</c> a la
/// RQ origen vía <c>LineaRqLiberadaEvent</c>.
///
/// <para>
/// <b>Doble autorización</b>: el endpoint API requiere que el usuario
/// tenga los 3 permisos:
/// <c>compras.ordenes.cancelar-doble</c> +
/// <c>compras.ordenes.autorizar-nivel1</c> +
/// <c>compras.ordenes.autorizar-nivel2</c>.
/// </para>
/// </summary>
public sealed record CancelarConRecepcionesCommand(
    Guid OrdenCompraId,
    Guid MotivoCancelacionId,
    string? MotivoCancelacionTexto) : IRequest;
