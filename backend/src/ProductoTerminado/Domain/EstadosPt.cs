namespace Millet.ProductoTerminado.Domain;

public enum EstadoPieza { EnPT, EnTransito }

public enum TipoMovimientoPieza { Alta, Reposicion, TrasladoSalida, TrasladoRecepcion }

/// <summary>EnTransito (sin recibir) -> ConDiferencia (recepción parcial, faltan piezas) -> Recibido (llegaron todas).</summary>
public enum EstadoTraspaso { EnTransito, ConDiferencia, Recibido }

public enum ResultadoEscaneo { Alta, Pospuesto, Excepcion, Reposicion }

/// <summary>Estado interno del ERP del pedido en PT: Retenido (69) o Liberado (70). No es el 69 de A+W.</summary>
public enum EstadoLiberacion { Retenido, Liberado }
