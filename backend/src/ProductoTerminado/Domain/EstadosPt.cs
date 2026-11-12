namespace Millet.ProductoTerminado.Domain;

/// <summary>Cargada (en el camión, PT5.4) y Embarcada (salió con un albarán emitido, PT5.5) son los pasos de la salida; Entregada (PT5.6) la recibió el cliente y EnRevision (PT5.6, de ahí sigue PT5.8) la rechazó en sitio.</summary>
public enum EstadoPieza { EnPT, EnTransito, Cargada, Embarcada, Entregada, EnRevision,
    /// <summary>M-351: Calidad la mandó a reproceso en planta; vuelve a PT cuando A+W la escanea de nuevo.</summary>
    EnReproceso,
    /// <summary>M-351: dada de baja por desecho o refabricación (la refabricación es un pedido nuevo en A+W).</summary>
    Baja }

public enum TipoMovimientoPieza { Alta, Reposicion, TrasladoSalida, TrasladoRecepcion, Carga, CargaRevertida, Salida, RotaEnCarga, Entrega, RechazoEnSitio, SalidaCancelada,
    Ubicacion, DanoRegistrado, DictamenDisponible, DictamenReproceso, Baja, ReingresoReposicion, EntregaCorregida }

/// <summary>EnTransito (sin recibir) -> ConDiferencia (recepción parcial, faltan piezas) -> Recibido (llegaron todas).</summary>
public enum EstadoTraspaso { EnTransito, ConDiferencia, Recibido }

public enum ResultadoEscaneo { Alta, Pospuesto, Excepcion, Reposicion }

/// <summary>Estado interno del ERP del pedido en PT: Retenido (69) o Liberado (70). No es el 69 de A+W.</summary>
public enum EstadoLiberacion { Retenido, Liberado }

/// <summary>Hoja de salida (PT5.5): Borrador (se carga) -> Emitida (descuenta una sola vez) o Cancelada (solo desde Borrador).</summary>
public enum EstadoSalida { Borrador, Emitida, Cancelada }

/// <summary>Resultado de la entrega de un albarán (PT5.6): todo recibido, recibido con piezas rechazadas en sitio, o todo rechazado.</summary>
public enum ResultadoEntrega { Entregada, Parcial, Rechazada }

/// <summary>M-251: almacén de producto terminado donde se ubica la pieza (PT no tiene racks: Nacional y Exportación, Millet 10-nov).</summary>
public enum AlmacenPt { Nacional, Exportacion }

/// <summary>Solicitud con autorización de otra persona (salida parcial M-245, cancelación de albarán M-247).</summary>
public enum EstadoSolicitudPt { Pendiente, Autorizada, Rechazada }

/// <summary>M-351: dónde se detectó el daño. AntesDeCarga (en PT o rota al cargar), RechazoEnSitio (el cliente no la aceptó), DevolucionCliente (regresó después de entregada).</summary>
public enum EtapaDanoPt { AntesDeCarga, RechazoEnSitio, DevolucionCliente }

public enum EstadoCasoDanoPt { Abierto, Dictaminado, Cerrado }

/// <summary>M-351 (Fernando 11-nov): lo decide Calidad. La refabricación se pide como pedido nuevo en A+W; el ERP no la crea.</summary>
public enum DictamenCalidadPt { RegresarDisponible, Reproceso, Refabricacion, Desecho }

/// <summary>M-352: correctivo fiscal ligado al caso.</summary>
public enum CorrectivoFiscalPt { NotaCredito, CancelacionFactura }
