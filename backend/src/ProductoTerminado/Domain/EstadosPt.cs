namespace Millet.ProductoTerminado.Domain;

/// <summary>Cargada (en el camión, PT5.4) y Embarcada (salió con un albarán emitido, PT5.5) son los pasos de la salida; Entregada (PT5.6) la recibió el cliente y EnRevision (PT5.6, de ahí sigue PT5.8) la rechazó en sitio.</summary>
public enum EstadoPieza { EnPT, EnTransito, Cargada, Embarcada, Entregada, EnRevision }

public enum TipoMovimientoPieza { Alta, Reposicion, TrasladoSalida, TrasladoRecepcion, TrasladoRegreso, TrasladoPerdida, Carga, CargaRevertida, Salida, RotaEnCarga, Entrega, RechazoEnSitio, SalidaCancelada }

/// <summary>
/// Preparado (documentado, las piezas aún no se mueven) -> EnTransito (autorizado por otra persona) -> ConDiferencia (recepción parcial, faltan piezas)
/// -> Recibido (llegaron todas) o Cerrado (hubo diferencia y cada pieza quedó aclarada: llegó tarde, regresó al origen o se dio por perdida).
/// </summary>
public enum EstadoTraspaso { Preparado, EnTransito, ConDiferencia, Recibido, Cerrado }

/// <summary>Qué le pasó a una pieza que no llegó en la recepción (M-350).</summary>
public enum ClasificacionDiferencia { Faltante, DanadaEnTransito, EnCuarentena }

/// <summary>Cómo se aclaró la diferencia: la pieza llegó tarde a destino, regresó al PT de origen o se dio por perdida (queda en revisión para Calidad).</summary>
public enum ResolucionDiferencia { RecibidaTarde, RegresadaAlOrigen, DadaPorPerdida }

public enum ResultadoEscaneo { Alta, Pospuesto, Excepcion, Reposicion }

/// <summary>Estado interno del ERP del pedido en PT: Retenido (69) o Liberado (70). No es el 69 de A+W.</summary>
public enum EstadoLiberacion { Retenido, Liberado }

/// <summary>Hoja de salida (PT5.5): Borrador (se carga) -> Emitida (descuenta una sola vez) o Cancelada (solo desde Borrador).</summary>
public enum EstadoSalida { Borrador, Emitida, Cancelada }

/// <summary>Resultado de la entrega de un albarán (PT5.6): todo recibido, recibido con piezas rechazadas en sitio, o todo rechazado.</summary>
public enum ResultadoEntrega { Entregada, Parcial, Rechazada }
