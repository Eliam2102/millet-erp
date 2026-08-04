/**
 * Glosario de términos del dominio Compras (doc 05 §13.7 de RQ + §11.2
 * de OC). Centraliza las definiciones que <c>&lt;EstadoBadge/&gt;</c>,
 * <c>&lt;NaturalezaBadge/&gt;</c>, <c>&lt;DomainTermTooltip/&gt;</c> y la
 * página de ayuda muestran al usuario.
 *
 * <para>Mantener alineado con los diseños del módulo:
 * <c>docs/modulos/compras-requisiciones/01-diseno.md</c> §5 (estados RQ)
 * y §3.bis (clasificación + naturaleza), y
 * <c>docs/modulos/compras-ordenes-compra/01-diseno.md</c> §5 (estados OC)
 * + §6 (sub-estados + glosario). Cambios al modelo del dominio se
 * reflejan acá en el mismo PR.</para>
 *
 * <para>Vive bajo <c>features/compras/lib/</c> porque cubre los dos
 * submódulos del módulo Compras (RQ + OC). Cuando otros módulos del
 * back-office (CxC, CxP, ...) lleguen con sus propios términos, esta
 * estructura se generaliza a un sistema de namespaces.</para>
 */

/**
 * 10 estados del agregado <c>Requisicion</c> (doc 01 §5.2): 8 base + 2
 * terminales de cierre manual (ADR-0043).
 */
export type EstadoRequisicion =
  | 'Borrador'
  | 'EnAutorizacion'
  | 'Autorizada'
  | 'EnSurtido'
  | 'Cerrada'
  | 'Cancelada'
  | 'Rechazada'
  | 'Eliminada'
  | 'CerradaSinSurtir'
  | 'CerradaSurtidaParcial';

/**
 * 3 situaciones calculadas dentro de <c>EnSurtido</c> (ADR-0043, el
 * "badge calculado"). No son estados de la máquina: se derivan server-side
 * y solo aplican mientras la RQ está <c>EnSurtido</c>.
 */
export type SituacionSurtido =
  | 'EsperandoCompra'
  | 'ListoParaSurtir'
  | 'SurtidoParcial';

/**
 * 4 naturalezas del artículo (doc 01 §3.bis).
 */
export type Naturaleza = 'Estandar' | 'Servicio' | 'Critico' | 'Riesgo';

/**
 * 7 estados del agregado <c>OrdenCompra</c> (doc OC 01 §5.1).
 */
export type EstadoOrdenCompra =
  | 'Borrador'
  | 'EnAutorizacionJefeCompras'
  | 'EnAutorizacionDireccion'
  | 'Autorizada'
  | 'Cerrada'
  | 'Cancelada'
  | 'Rechazada';

/**
 * Términos transversales del módulo Compras (no-enum, mezcla RQ + OC)
 * que aparecen en la UI y requieren tooltip de glosario.
 */
export type TerminoTransversal =
  // Transversales de RQ (doc RQ 05 §13.7).
  | 'Cubrimiento'
  | 'Matriz'
  | 'Bifurcacion'
  | 'Reserva'
  // Transversales de OC (doc OC 05 §11.2 + 06 Fase 0). Ver detalle
  // por término en el diccionario `TRANSVERSALES` abajo.
  | 'SubEstado'
  | 'PartidaAbierta'
  | 'Consolidacion'
  | 'DuplicarOc'
  | 'CotizacionExcepcionada'
  | 'OcOrigen'
  | 'Contenedor'
  | 'Ruta'
  | 'Semana'
  | 'Pedimento'
  | 'Incoterm';

interface DefinicionTerm {
  /** Texto corto para tooltip (≤ 200 caracteres). */
  resumen: string;
  /** Texto largo para la página de ayuda (UF7-PR3). Opcional. */
  detalle?: string;
}

export const ESTADOS: Record<EstadoRequisicion, DefinicionTerm> = {
  Borrador: {
    resumen:
      'La requisición se está editando y aún no se ha enviado a autorización. Puedes agregar/quitar líneas y cambiar la cabecera.',
  },
  EnAutorizacion: {
    resumen:
      'Esperando firma de uno o más autorizadores. Las líneas y la cabecera ya no se editan; el siguiente paso es Aprobar o Rechazar.',
  },
  Autorizada: {
    resumen:
      'Las firmas requeridas están completas. La RQ está lista para que almacén o compras la materialicen, pero todavía no se ha tocado stock ni se ha generado OC.',
  },
  EnSurtido: {
    resumen:
      'La RQ está autorizada y el almacén o el proveedor están entregando el material. El cubrimiento por línea muestra el avance.',
  },
  Cerrada: {
    resumen:
      'Todas las líneas se cubrieron al 100% (almacén o recepciones de OC). La RQ es histórica y ya no admite cambios.',
  },
  Cancelada: {
    resumen:
      'La RQ se canceló post-autorización. Las reservas se liberaron y las OC en borrador asociadas se cancelaron también.',
  },
  Rechazada: {
    resumen:
      'Algún autorizador rechazó la RQ con un motivo. No puede volver a Borrador ni re-enviarse; el solicitante debe crear una nueva si quiere reintentar.',
  },
  Eliminada: {
    resumen:
      'La RQ se eliminó pre-autorización (estado Borrador o EnAutorizacion). Es un soft delete: queda en BD para auditoría pero ya no aparece en bandejas.',
  },
  CerradaSinSurtir: {
    resumen:
      'Cierre administrativo (jefe de almacén): la RQ se cerró sin entregar nada al solicitante porque ya no se necesita el material. Lo no entregado queda como stock libre. ADR-0043.',
  },
  CerradaSurtidaParcial: {
    resumen:
      'Cierre administrativo (jefe de almacén): se entregó solo una parte y el resto ya no se entregará; lo no entregado queda como stock libre. ADR-0043.',
  },
};

export const ESTADOS_OC: Record<EstadoOrdenCompra, DefinicionTerm> = {
  Borrador: {
    resumen:
      'La OC se está editando y aún no se ha enviado a autorización. El comprador puede agregar/quitar líneas, cambiar la cabecera y adjuntar documentos.',
  },
  EnAutorizacionJefeCompras: {
    resumen:
      'Esperando la firma del Jefe de Compras (Nivel 1). Las líneas y la cabecera están bloqueadas; el siguiente paso es Aprobar o Rechazar.',
  },
  EnAutorizacionDireccion: {
    resumen:
      'Aprobada por Jefe de Compras y esperando la firma de Dirección (Nivel 2). Sigue bloqueada para edición estructural.',
  },
  Autorizada: {
    resumen:
      'Las dos firmas están completas y la OC se transmitió al proveedor. Habilita recepciones, facturación y pagos. La logística se sigue editando con permiso `logistica`.',
  },
  Cerrada: {
    resumen:
      'Todas las partidas se recibieron, facturaron y pagaron. La OC es histórica y ya no admite cambios — solo lectura y descarga del PDF.',
  },
  Cancelada: {
    resumen:
      'La OC se canceló (con 1 firma si no había recepciones, o con doble firma si había recepciones parciales). Para corregir, usa el flujo "Duplicar OC".',
  },
  Rechazada: {
    resumen:
      'Algún autorizador (N1 o N2) rechazó la OC con un motivo. El comprador puede editar y re-transmitir, o duplicarla si prefiere arrancar limpia.',
  },
};

/**
 * Situaciones calculadas dentro de <c>EnSurtido</c> (ADR-0043). El badge
 * de la RQ reemplaza el label genérico "En surtido" por estas tres fases
 * cuando el backend las pobla, para que el solicitante distinga "todo por
 * comprar" de "ya hay material listo" de un vistazo. Ninguna es terminal:
 * la RQ avanza sola por el canal de surtido y se cierra al entregar todo.
 */
export const SITUACIONES: Record<SituacionSurtido, DefinicionTerm> = {
  EsperandoCompra: {
    resumen:
      'Fase del surtido: la RQ está autorizada pero todavía no hay material disponible para entregar — todo está por comprar (esperando OC y recepción). No es terminal; avanza sola cuando llega el material.',
  },
  ListoParaSurtir: {
    resumen:
      'Fase del surtido: ya hay material disponible para entregar al solicitante (reservado de stock y/o recibido de compra) y aún no se ha entregado nada. No es terminal; el siguiente paso es la salida de almacén.',
  },
  SurtidoParcial: {
    resumen:
      'Fase del surtido: ya se entregó una parte al solicitante y falta el resto; se cierra al entregar todo. No es terminal — distinto de "Cerrada surtida parcialmente", que es el cierre administrativo definitivo.',
  },
};

export const NATURALEZAS: Record<Naturaleza, DefinicionTerm> = {
  Estandar: {
    resumen:
      'Material o servicio común sin requisitos especiales. Sigue la matriz de aprobación por monto y clasificación.',
  },
  Servicio: {
    resumen:
      'Trabajo contratado a terceros (mantenimientos, asesorías, fletes). No genera entrada a almacén; se cubre 100% por OC al recibirlo.',
  },
  Critico: {
    resumen:
      'Material que afecta operación o calidad si falta. Requiere autorización de Nivel 2 sin importar el monto.',
  },
  Riesgo: {
    resumen:
      'Compra que toca control interno o cumplimiento (regalos, donativos, ciertos químicos). Requiere autorización N2 + revisión adicional según política.',
  },
};

export const TRANSVERSALES: Record<TerminoTransversal, DefinicionTerm> = {
  // -- RQ --
  Cubrimiento: {
    resumen:
      'Cómo se va satisfaciendo la cantidad pedida de cada línea: parte de almacén (existente), parte de OC nueva (en compra) y/o parte ya recibida.',
  },
  Matriz: {
    resumen:
      'Tabla que decide cuántas firmas y de qué nivel necesita la RQ según naturaleza, clasificación y monto. Ver §3.bis del diseño.',
  },
  Bifurcacion: {
    resumen:
      'Una vez completada la matriz, el motor decide automáticamente si cada línea va a Almacén (hay stock) o a OC (no hay). El usuario no elige.',
  },
  Reserva: {
    resumen:
      'Apartado de stock que el almacén hace cuando la matriz se cumple, para que otra RQ no consuma ese material antes de que se entregue. Se libera al cancelar.',
  },
  // -- OC --
  SubEstado: {
    resumen:
      'Tres dimensiones independientes que avanzan post-autorización: Recepción, Facturación y Pago. Cada una tiene su propio progreso por línea (se muestra como 3 barras segmentadas).',
  },
  PartidaAbierta: {
    resumen:
      'Línea de OC autorizada que aún no completó alguno de los 3 sub-estados (recepción, factura o pago). Es la unidad de seguimiento del reporte operativo del comprador.',
  },
  Consolidacion: {
    resumen:
      'Crear una OC al mismo proveedor a partir de varias requisiciones (RQ → N:1). Las RQs deben ser de la misma sucursal para evitar destinos cruzados.',
  },
  DuplicarOc: {
    resumen:
      'Acción de cancelar una OC y crear una nueva clonando cabecera y líneas (sin adjuntos ni autorizaciones). Es el flujo de modificación post-autorización (decisión C4).',
  },
  CotizacionExcepcionada: {
    resumen:
      'OC creada sin RQ previa (caso "Sin RQ previa"). Requiere permiso especial `crear-sin-rq`, motivo obligatorio y correo de autorización adjunto.',
  },
  OcOrigen: {
    resumen:
      'OC desde la que se duplicó esta OC. Se enlaza por `oc_origen_id` y aparece en el aside list del detalle como "Origen: OC-...".',
  },
  Contenedor: {
    resumen:
      'Identificador del contenedor de embarque para una OC de importación. Se usa para agrupar OCs en partidas abiertas y filtrar bandejas operativas.',
  },
  Ruta: {
    resumen:
      'Código de la ruta logística del embarque (importación). Sirve para agrupar OCs que viajan juntas y planificar recepciones.',
  },
  Semana: {
    resumen:
      'Semana de embarque programada (importación). Permite filtrar bandejas y partidas abiertas por ventana logística.',
  },
  Pedimento: {
    resumen:
      'Número del pedimento aduanal (importación). Único campo de información de importación que sigue siendo editable después de la autorización N2.',
  },
  Incoterm: {
    resumen:
      'Término comercial internacional (FOB, CIF, EXW, etc.) que define qué partes corren con flete, seguro y trámites aduanales. Aplica solo a OC de importación.',
  },
};

/**
 * Lookup centralizado: busca el término en los cinco diccionarios
 * (estados RQ / estados OC / situaciones de surtido / naturalezas /
 * transversales). Devuelve
 * <c>undefined</c> si el término no existe — el caller decide si
 * renderiza el tooltip vacío o cae a un fallback.
 *
 * <para>Nota: las claves <c>Borrador</c>, <c>Autorizada</c>,
 * <c>Cancelada</c> y <c>Rechazada</c> existen en RQ y OC con definiciones
 * distintas. La RQ tiene precedencia (orden de evaluación) — los callers
 * de OC deben pasar las claves OC-específicas
 * (<c>EnAutorizacionJefeCompras</c>, <c>EnAutorizacionDireccion</c>,
 * <c>Cerrada</c>) que no colisionan, y aceptar el resumen de RQ para las
 * compartidas (<c>Borrador</c>, <c>Autorizada</c>, etc.) cuando es
 * suficiente. Si una pantalla OC necesita el wording exacto de OC para
 * una clave compartida, leer directamente <c>ESTADOS_OC[clave]</c>.</para>
 */
export function obtenerDefinicion(
  term:
    | EstadoRequisicion
    | EstadoOrdenCompra
    | SituacionSurtido
    | Naturaleza
    | TerminoTransversal
    | string,
): DefinicionTerm | undefined {
  if (term in ESTADOS) return ESTADOS[term as EstadoRequisicion];
  if (term in ESTADOS_OC) return ESTADOS_OC[term as EstadoOrdenCompra];
  if (term in SITUACIONES) return SITUACIONES[term as SituacionSurtido];
  if (term in NATURALEZAS) return NATURALEZAS[term as Naturaleza];
  if (term in TRANSVERSALES) return TRANSVERSALES[term as TerminoTransversal];
  return undefined;
}

/**
 * Variante de <c>obtenerDefinicion</c> que prefiere el diccionario de
 * estados OC sobre el de estados RQ cuando el término existe en ambos
 * (claves compartidas: <c>Borrador</c>, <c>Autorizada</c>,
 * <c>Cancelada</c>, <c>Rechazada</c>). Úsalo desde componentes que
 * renderizan estado de OC (e.g., <c>&lt;EstadoBadge tipo="orden-compra"/&gt;</c>)
 * para que el tooltip muestre el wording de OC, no el de RQ.
 */
export function obtenerDefinicionOc(
  term:
    | EstadoOrdenCompra
    | TerminoTransversal
    | string,
): DefinicionTerm | undefined {
  if (term in ESTADOS_OC) return ESTADOS_OC[term as EstadoOrdenCompra];
  if (term in TRANSVERSALES) return TRANSVERSALES[term as TerminoTransversal];
  return undefined;
}
