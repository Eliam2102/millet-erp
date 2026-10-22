import { useState } from 'react';
import { AlertTriangle, ChevronDown, ChevronRight, RefreshCw, XCircle } from 'lucide-react';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { EjecucionSyncErrorItem } from '@/modules/datos-maestros/api/types';
import { DetalleOrigenAw } from '@/modules/datos-maestros/components/DetalleOrigenAw';
import { usePaginacionLocal } from '@/modules/datos-maestros/lib/use-paginacion-local';

/**
 * Catálogo de los códigos por fila que emite el sincronizador de clientes A+W
 * (doc integration/05 §10.3). Un código desconocido se muestra tal cual.
 */
export type CatalogoErroresSync = Record<
  string,
  { titulo: string; accion: string; conflicto?: boolean; area?: string; corrigeEn?: string }
>;

const CODIGOS: CatalogoErroresSync = {
  moneda_sin_equivalencia: {
    titulo: 'Moneda sin equivalencia',
    area: 'TI',
    corrigeEn: 'A+W',
    accion: 'El cliente no se creó. Configura el mapeo de moneda y reintenta.',
  },
  conflicto_correlacion: {
    titulo: 'Coincidencia de RFC con otro cliente',
    accion:
      'Ya existe en el ERP un cliente con el mismo RFC o la misma referencia. Revisa el ligado en Datos maestros → Clientes (Ligados pendientes de revisión); no se crea otro cliente.',
    conflicto: true,
  },
  fila_invalida: {
    titulo: 'Fila inválida en A+W',
    accion:
      'Falta un dato obligatorio. Fiscal valida el dato correcto contra la constancia; después se corrige en A+W y se reintenta.',
    area: 'Fiscal',
    corrigeEn: 'A+W',
  },
  no_encontrada_en_origen: {
    titulo: 'No existe en A+W',
    area: 'TI',
    corrigeEn: 'A+W',
    accion:
      'La referencia ya no está en el origen. Verifica que sea correcta; no se da de baja al cliente.',
  },
  aplicacion_fallida: {
    titulo: 'No se pudo aplicar',
    area: 'Soporte VILO',
    corrigeEn: 'ERP',
    accion:
      'Falló al guardar este cliente; los demás siguieron. Reintenta y, si persiste, avisa a soporte.',
  },
};

/** Lista de errores y conflictos por referencia de una ejecución, con causa y acción sugerida. */
export function ErroresEjecucionSync({
  errores,
  truncados,
  reintentando,
  onReintentar,
  catalogo = CODIGOS,
  origen,
  titulo = 'Errores y conflictos por referencia',
}: {
  errores: EjecucionSyncErrorItem[];
  truncados?: boolean;
  /** Referencia en reproceso (solo esa fila se deshabilita). */
  reintentando?: string | null;
  onReintentar: (referencia: string) => void;
  /** Catálogo de causas del recurso; por defecto el de clientes. */
  catalogo?: CatalogoErroresSync;
  /** Con permiso de ver origen: cada error (no conflicto) muestra cómo llegó de A+W. */
  origen?: { fecha: string };
  titulo?: string;
}) {
  const [abierto, setAbierto] = useState<string | null>(null);
  const { offset, limit, visibles, onChange } = usePaginacionLocal(errores);
  return (
    <section aria-label={titulo} className="space-y-2">
      <h4 className="text-xs text-ink-muted">
        {titulo} ({errores.length}
        {truncados ? '+' : ''})
      </h4>
      <ul className="divide-y rounded-md border">
        {visibles.map((er, i) => {
          const c = catalogo[er.codigo];
          const clave = `${er.referencia}-${er.codigo}`;
          const conDetalle = origen != null && !c?.conflicto && er.codigo !== 'no_encontrada_en_origen';
          const expandido = abierto === clave;
          const Icono = c?.conflicto ? AlertTriangle : XCircle;
          return (
            <li
              key={`${er.referencia}-${offset + i}`}
              className="grid gap-x-4 gap-y-2 p-3 text-sm sm:grid-cols-[minmax(0,1fr)_auto]"
            >
              <div className="min-w-0 space-y-1">
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <Icono
                    aria-hidden
                    className={
                      c?.conflicto
                        ? 'h-4 w-4 shrink-0 text-warning-fg'
                        : 'h-4 w-4 shrink-0 text-danger-fg'
                    }
                  />
                  <span className="font-medium">{c?.titulo ?? 'Error'}</span>
                  <span className="font-mono text-xs text-ink-muted">· ref. {er.referencia}</span>
                  {c?.area != null && (
                    <>
                      <Badge variant="neutral">Valida: {c.area}</Badge>
                      <Badge variant="neutral">Se corrige en {c.corrigeEn}</Badge>
                    </>
                  )}
                  <span className="font-mono text-2xs text-ink-muted">{er.codigo}</span>
                </div>
                <p className="break-words text-ink-muted">{er.mensaje}</p>
                {c != null && <p className="text-xs">{c.accion}</p>}
              </div>
              <div className="flex flex-wrap items-start gap-2 sm:justify-end">
                {conDetalle && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-expanded={expandido}
                    aria-controls={`origen-${clave}`}
                    onClick={() => setAbierto(expandido ? null : clave)}
                  >
                    {expandido ? (
                      <ChevronDown aria-hidden className="mr-1 h-3.5 w-3.5" />
                    ) : (
                      <ChevronRight aria-hidden className="mr-1 h-3.5 w-3.5" />
                    )}
                    {expandido ? 'Ocultar cómo llegó de A+W' : 'Ver cómo llegó de A+W y qué falta'}
                  </Button>
                )}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={reintentando != null}
                  onClick={() => onReintentar(er.referencia)}
                  aria-label={`Reintentar referencia ${er.referencia}`}
                >
                  <RefreshCw
                    className={`mr-1.5 h-3.5 w-3.5 ${reintentando === er.referencia ? 'animate-spin motion-reduce:animate-none' : ''}`}
                  />
                  {reintentando === er.referencia ? 'Leyendo…' : 'Reintentar'}
                </Button>
              </div>
              {conDetalle && expandido && (
                <div id={`origen-${clave}`} className="min-w-0 sm:col-span-2">
                  <DetalleOrigenAw referencia={er.referencia} fecha={origen.fecha} />
                </div>
              )}
            </li>
          );
        })}
      </ul>
      {errores.length > 10 && (
        <PaginacionTabla
          total={errores.length}
          offset={offset}
          limit={limit}
          extremos
          onChange={onChange}
        />
      )}
      {truncados && (
        <p className="text-xs text-ink-muted">
          Se muestran solo los primeros errores de la ejecución.
        </p>
      )}
    </section>
  );
}
