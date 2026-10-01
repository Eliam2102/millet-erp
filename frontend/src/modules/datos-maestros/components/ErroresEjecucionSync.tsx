import { AlertTriangle, RefreshCw, XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import type { EjecucionSyncErrorItem } from '@/modules/datos-maestros/api/types';

/**
 * Catálogo de los códigos por fila que emite el sincronizador de clientes A+W
 * (doc integration/05 §10.3). Un código desconocido se muestra tal cual.
 */
export type CatalogoErroresSync = Record<string, { titulo: string; accion: string; conflicto?: boolean }>;

const CODIGOS: CatalogoErroresSync = {
  moneda_sin_equivalencia: {
    titulo: 'Moneda sin equivalencia',
    accion: 'El cliente no se creó. Configura el mapeo de moneda y reintenta.',
  },
  conflicto_correlacion: {
    titulo: 'Conflicto con un cliente manual',
    accion: 'Ya existe un cliente dado de alta a mano con esta referencia. Revísalo y reintenta; no se fusiona solo.',
    conflicto: true,
  },
  fila_invalida: {
    titulo: 'Fila inválida en A+W',
    accion: 'Falta un dato obligatorio en el origen (p. ej. el nombre). Corrígelo en A+W y reintenta.',
  },
  no_encontrada_en_origen: {
    titulo: 'No existe en A+W',
    accion: 'La referencia ya no está en el origen. Verifica que sea correcta; no se da de baja al cliente.',
  },
  aplicacion_fallida: {
    titulo: 'No se pudo aplicar',
    accion: 'Falló al guardar este cliente; los demás siguieron. Reintenta y, si persiste, avisa a soporte.',
  },
};

/** Lista de errores y conflictos por referencia de una ejecución, con causa y acción sugerida. */
export function ErroresEjecucionSync({
  errores,
  truncados,
  reintentando,
  onReintentar,
  catalogo = CODIGOS,
}: {
  errores: EjecucionSyncErrorItem[];
  truncados?: boolean;
  /** Referencia en reproceso (solo esa fila se deshabilita). */
  reintentando?: string | null;
  onReintentar: (referencia: string) => void;
  /** Catálogo de causas del recurso; por defecto el de clientes. */
  catalogo?: CatalogoErroresSync;
}) {
  return (
    <section aria-label="Errores y conflictos por referencia" className="space-y-2">
      <h4 className="text-xs text-muted-foreground">
        Errores y conflictos por referencia ({errores.length}
        {truncados ? '+' : ''})
      </h4>
      <ul className="divide-y rounded-md border">
        {errores.map((er, i) => {
          const c = catalogo[er.codigo];
          const Icono = c?.conflicto ? AlertTriangle : XCircle;
          return (
            <li key={`${er.referencia}-${i}`} className="flex gap-3 p-3 text-sm">
              <Icono
                aria-hidden
                className={
                  c?.conflicto
                    ? 'mt-0.5 h-4 w-4 shrink-0 text-amber-600'
                    : 'mt-0.5 h-4 w-4 shrink-0 text-destructive'
                }
              />
              <div className="min-w-0 flex-1 space-y-1">
                <p className="font-medium">
                  {c?.titulo ?? 'Error'}{' '}
                  <span className="font-mono text-xs font-normal text-muted-foreground">
                    · ref. {er.referencia}
                  </span>
                </p>
                <p className="break-words text-muted-foreground">{er.mensaje}</p>
                {c != null && <p className="text-xs">{c.accion}</p>}
                <p className="font-mono text-[11px] text-muted-foreground">{er.codigo}</p>
              </div>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="shrink-0 self-start"
                disabled={reintentando != null}
                onClick={() => onReintentar(er.referencia)}
                aria-label={`Reintentar referencia ${er.referencia}`}
              >
                <RefreshCw
                  className={`mr-1.5 h-3.5 w-3.5 ${reintentando === er.referencia ? 'animate-spin' : ''}`}
                />
                {reintentando === er.referencia ? 'Leyendo…' : 'Reintentar'}
              </Button>
            </li>
          );
        })}
      </ul>
      {truncados && (
        <p className="text-xs text-muted-foreground">
          Se muestran solo los primeros errores de la ejecución.
        </p>
      )}
    </section>
  );
}
