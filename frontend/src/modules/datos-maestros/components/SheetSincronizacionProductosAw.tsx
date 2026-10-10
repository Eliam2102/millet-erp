import { ControlOrigenAw } from '@/modules/datos-maestros/components/ControlOrigenAw';
import { useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  ErroresEjecucionSync,
  type CatalogoErroresSync,
} from '@/modules/datos-maestros/components/ErroresEjecucionSync';
import {
  mensajeErrorSincronizacionProductos,
  useReintentarProductoAw,
  useSincronizarProductosAw,
} from '@/modules/datos-maestros/api';
import type { AwProductosResumen } from '@/modules/datos-maestros/api/types';

/** Causas por fila de la sincronización de productos A+W (doc integration/06). */
const CODIGOS_PRODUCTOS_AW: CatalogoErroresSync = {
  UNIDAD_SIN_EQUIVALENCIA: {
    titulo: 'Unidad sin equivalencia',
    accion: 'El producto no se creó ni se actualizó. Da de alta la unidad y su equivalencia SAT, y reintenta.',
  },
  fila_invalida: {
    titulo: 'Fila inválida en A+W',
    accion: 'Falta un dato obligatorio en el origen (p. ej. la descripción). Corrígelo en A+W y reintenta.',
  },
  no_encontrada_en_origen: {
    titulo: 'No existe en A+W',
    accion: 'La referencia ya no está en el origen. Verifica que sea correcta; no se da de baja al producto.',
  },
  aplicacion_fallida: {
    titulo: 'No se pudo aplicar',
    accion: 'Falló al guardar este producto; los demás siguieron. Reintenta y, si persiste, avisa a soporte.',
  },
};

/**
 * Sheet de sincronización de productos A+W (F1-ADM-07). El botón que lo
 * abre ya está gateado por `productos-aw.gestionar`. El barrido es
 * síncrono: el resumen mostrado es el real, y conflictos/errores NUNCA
 * se presentan como éxito.
 */
export function SheetSincronizacionProductosAw({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const sincronizar = useSincronizarProductosAw();
  const reintentar = useReintentarProductoAw();
  const [resumen, setResumen] = useState<AwProductosResumen | null>(null);
  const [resueltas, setResueltas] = useState<string[]>([]);

  function handleIniciar() {
    setResueltas([]);
    sincronizar.mutate(undefined, {
      onSuccess: (r) => {
        setResumen(r);
        if (r.errores + r.conflictos > 0)
          toast.warning('Sincronización terminada con errores o conflictos.');
        else toast.success('Sincronización completa.');
      },
      onError: (e) => toast.error(mensajeErrorSincronizacionProductos(e)),
    });
  }

  function handleReintentar(referencia: string) {
    reintentar.mutate(
      { referencia },
      {
        onSuccess: (r) => {
          if (r.errores + r.conflictos + r.pendientes === 0) {
            setResueltas((x) => [...x, referencia]);
            toast.success(`Referencia ${referencia} reprocesada.`);
          } else
            toast.warning(
              r.erroresPorReferencia[0]?.mensaje ?? `La lectura de ${referencia} no se pudo aplicar.`,
            );
        },
        onError: (e) => toast.error(mensajeErrorSincronizacionProductos(e)),
      },
    );
  }

  const errores = (resumen?.erroresPorReferencia ?? []).filter(
    (e) => !resueltas.includes(e.referencia),
  );

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Sincronización de productos A+W</SheetTitle>
          <SheetDescription>
            Lee los productos de A+W y los aplica al ERP. Los datos fiscales
            que ya completaste no se sobrescriben.
          </SheetDescription>
        </SheetHeader>
        <ControlOrigenAw area="productos" enabled={open} />
        <div className="mt-4 space-y-3">
          <Button size="sm" onClick={handleIniciar} disabled={sincronizar.isPending}>
            <RefreshCw className="mr-1.5 h-4 w-4" />
            {sincronizar.isPending ? 'Sincronizando…' : 'Iniciar sincronización'}
          </Button>

          {resumen != null && (
            <>
              <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-4" aria-label="Resumen">
                {(
                  [
                    ['Leídos', resumen.leidos],
                    ['Creados', resumen.creados],
                    ['Actualizados', resumen.actualizados],
                    ['Sin cambios', resumen.sinCambios],
                    ['Pendientes', resumen.pendientes],
                    ['Conflictos', resumen.conflictos],
                    ['Errores', resumen.errores],
                  ] as const
                ).map(([t, v]) => (
                  <div key={t} className="rounded-md border p-2">
                    <dt className="text-xs text-muted-foreground">{t}</dt>
                    <dd className="font-mono text-base">{v}</dd>
                  </div>
                ))}
              </dl>
              {errores.length > 0 && (
                <ErroresEjecucionSync
                  errores={errores}
                  catalogo={CODIGOS_PRODUCTOS_AW}
                  reintentando={reintentar.isPending ? reintentar.variables?.referencia : null}
                  onReintentar={handleReintentar}
                />
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
