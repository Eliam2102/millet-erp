import type {
  OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

/** Pestaña del detalle a la que lleva el botón «Ir a…» de un requisito. */
export type DestinoRevisionOc = 'informacion' | 'lineas' | 'adjuntos';

export interface RequisitoRevisionOc {
  id:
    | 'cabecera'
    | 'precios'
    | 'cotizacion'
    | 'motivoSinRq'
    | 'correoAutorizacion'
    | 'fichaTecnica'
    | 'importacion';
  etiqueta: string;
  ok: boolean;
  /** Qué falta (solo si <c>ok=false</c>). */
  detalle?: string;
  /** Adónde ir a corregirlo (solo si <c>ok=false</c>). */
  destino?: { tab: DestinoRevisionOc; etiqueta: string };
}

export interface RevisionPreviaOc {
  requisitos: RequisitoRevisionOc[];
  pendientes: number;
  total: number;
}

type TipoDocumento = { id: string; clave: string };

type OcRevision = Pick<
  OrdenCompraDetalleResponse,
  | 'camposPorCapturar'
  | 'lineas'
  | 'adjuntos'
  | 'sinRequisicionPrevia'
  | 'motivoSinRequisicion'
  | 'esImportacion'
  | 'infoImportIncotermId'
  | 'infoImportPaisOrigen'
>;

const NOMBRE_CAMPO_CABECERA = {
  proveedor: 'proveedor',
  condicionesPago: 'condiciones de pago',
  usoPrincipal: 'uso principal',
} as const;

/**
 * Requisitos que el servidor exige en «Transmitir a autorización»
 * (<c>EnviarAAutorizacionOcHandler</c> + <c>OrdenCompra.EnviarAAutorizacion</c>),
 * calculados con los datos del detalle. El backend sigue siendo la fuente de
 * verdad: si esto difiere, el servidor rechaza con su propio mensaje.
 *
 * <para>Si el catálogo de tipos de documento aún no cargó (<c>tipos</c>
 * indefinido) o no trae la clave, los requisitos de adjuntos se omiten en vez
 * de bloquear con datos incompletos.</para>
 */
export function calcularRevisionPrevia(
  oc: OcRevision,
  tipos: readonly TipoDocumento[] | undefined,
): RevisionPreviaOc {
  const requisitos: RequisitoRevisionOc[] = [];

  const campos = oc.camposPorCapturar ?? [];
  requisitos.push({
    id: 'cabecera',
    etiqueta: 'Proveedor, condiciones y uso principal',
    ok: campos.length === 0,
    ...(campos.length > 0 && {
      detalle: `Falta: ${campos.map((c) => NOMBRE_CAMPO_CABECERA[c]).join(', ')}.`,
      destino: { tab: 'informacion' as const, etiqueta: 'Ir a Información' },
    }),
  });

  const sinPrecio = oc.lineas
    .filter((l) => l.precioUnitario <= 0)
    .map((l) => l.posicion)
    .sort((a, b) => a - b);
  const sinLineas = oc.lineas.length === 0;
  const preciosOk = !sinLineas && sinPrecio.length === 0;
  requisitos.push({
    id: 'precios',
    etiqueta: 'Todas las líneas con precio',
    ok: preciosOk,
    ...(!preciosOk && {
      detalle: sinLineas
        ? 'Agrega al menos una línea.'
        : sinPrecio.length === 1
          ? `Falta el precio de la línea ${sinPrecio[0]}.`
          : `Faltan los precios de las líneas ${sinPrecio.join(', ')}.`,
      destino: {
        tab: 'lineas' as const,
        etiqueta: sinLineas || sinPrecio.length === 0 ? 'Ir a Líneas' : `Ir a línea ${sinPrecio[0]}`,
      },
    }),
  });

  const tieneAdjunto = (clave: string): boolean | undefined => {
    const tipo = tipos?.find((t) => t.clave === clave);
    if (!tipo) return undefined;
    return oc.adjuntos.some((a) => a.tipoDocumentoId === tipo.id);
  };
  const requisitoAdjunto = (
    id: RequisitoRevisionOc['id'],
    clave: string,
    etiqueta: string,
  ) => {
    const tiene = tieneAdjunto(clave);
    if (tiene === undefined) return;
    requisitos.push({
      id,
      etiqueta,
      ok: tiene,
      ...(!tiene && {
        detalle: 'Aún no hay un archivo de este tipo.',
        destino: { tab: 'adjuntos' as const, etiqueta: 'Ir a Adjuntos' },
      }),
    });
  };

  requisitoAdjunto('cotizacion', 'cotizacion', 'Cotización adjunta');

  if (oc.sinRequisicionPrevia) {
    const motivoOk = (oc.motivoSinRequisicion ?? '').trim().length > 0;
    requisitos.push({
      id: 'motivoSinRq',
      etiqueta: 'Motivo de la orden sin requisición',
      ok: motivoOk,
      ...(!motivoOk && {
        detalle: 'Explica por qué la orden no tiene requisición previa.',
        destino: { tab: 'informacion' as const, etiqueta: 'Ir a Información' },
      }),
    });
    requisitoAdjunto(
      'correoAutorizacion',
      'correo_autorizacion',
      'Correo de autorización adjunto',
    );
  }

  if (oc.esImportacion) {
    requisitoAdjunto('fichaTecnica', 'ficha_tecnica', 'Ficha técnica adjunta');
    const faltan = [
      oc.infoImportIncotermId ? null : 'incoterm',
      oc.infoImportPaisOrigen ? null : 'país de origen',
    ].filter((x): x is string => x !== null);
    requisitos.push({
      id: 'importacion',
      etiqueta: 'Incoterm y país de origen',
      ok: faltan.length === 0,
      ...(faltan.length > 0 && {
        detalle: `Falta: ${faltan.join(' y ')}.`,
        destino: { tab: 'informacion' as const, etiqueta: 'Ir a Información' },
      }),
    });
  }

  const pendientes = requisitos.filter((r) => !r.ok).length;
  return { requisitos, pendientes, total: requisitos.length };
}

/** Motivo visible del botón «Transmitir» deshabilitado; <c>undefined</c> si no falta nada. */
export function motivoTransmisionBloqueada(
  revision: Pick<RevisionPreviaOc, 'pendientes' | 'total'>,
): string | undefined {
  if (revision.pendientes === 0) return undefined;
  return `Faltan ${revision.pendientes} de ${revision.total} requisitos · ver revisión previa`;
}
