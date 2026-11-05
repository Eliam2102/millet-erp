namespace Millet.ProductoTerminado.Domain;

/// <summary>Cargada (en el camión, PT5.4) y Embarcada (salió con un albarán emitido, PT5.5) son los pasos de la salida.</summary>
public enum EstadoPieza { EnPT, EnTransito, Cargada, Embarcada }

public enum TipoMovimientoPieza { Alta, Reposicion, TrasladoSalida, TrasladoRecepcion, Carga, CargaRevertida, Salida }

/// <summary>EnTransito (sin recibir) -> ConDiferencia (recepción parcial, faltan piezas) -> Recibido (llegaron todas).</summary>
public enum EstadoTraspaso { EnTransito, ConDiferencia, Recibido }

public enum ResultadoEscaneo { Alta, Pospuesto, Excepcion, Reposicion }

/// <summary>Estado interno del ERP del pedido en PT: Retenido (69) o Liberado (70). No es el 69 de A+W.</summary>
public enum EstadoLiberacion { Retenido, Liberado }

/// <summary>Hoja de salida (PT5.5): Borrador (se carga) -> Emitida (descuenta una sola vez) o Cancelada (solo desde Borrador).</summary>
public enum EstadoSalida { Borrador, Emitida, Cancelada }
