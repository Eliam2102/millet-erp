namespace Millet.ProductoTerminado.Domain;

/// <summary>Cargada (en el camión, PT5.4) y Embarcada (salió con un albarán emitido, PT5.5) son los pasos de la salida; Entregada (PT5.6) la recibió el cliente y EnRevision (PT5.6, de ahí sigue PT5.8) la rechazó en sitio.</summary>
public enum EstadoPieza { EnPT, EnTransito, Cargada, Embarcada, Entregada, EnRevision }

public enum TipoMovimientoPieza { Alta, Reposicion, TrasladoSalida, TrasladoRecepcion, Carga, CargaRevertida, Salida, RotaEnCarga, Entrega, RechazoEnSitio, SalidaCancelada }

/// <summary>EnTransito (sin recibir) -> ConDiferencia (recepción parcial, faltan piezas) -> Recibido (llegaron todas).</summary>
public enum EstadoTraspaso { EnTransito, ConDiferencia, Recibido }

public enum ResultadoEscaneo { Alta, Pospuesto, Excepcion, Reposicion }

/// <summary>Estado interno del ERP del pedido en PT: Retenido (69) o Liberado (70). No es el 69 de A+W.</summary>
public enum EstadoLiberacion { Retenido, Liberado }

/// <summary>Hoja de salida (PT5.5): Borrador (se carga) -> Emitida (descuenta una sola vez) o Cancelada (solo desde Borrador).</summary>
public enum EstadoSalida { Borrador, Emitida, Cancelada }

/// <summary>Resultado de la entrega de un albarán (PT5.6): todo recibido, recibido con piezas rechazadas en sitio, o todo rechazado.</summary>
public enum ResultadoEntrega { Entregada, Parcial, Rechazada }
