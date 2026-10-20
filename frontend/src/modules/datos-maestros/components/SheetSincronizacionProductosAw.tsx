import { ControlOrigenAw } from '@/modules/datos-maestros/components/ControlOrigenAw';
import { useState } from 'react';
import { Loader2, PackageSearch, RefreshCw, ShieldCheck } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  SheetBody,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
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
    accion:
      'El producto no se creó ni se actualizó. Da de alta la unidad y su equivalencia SAT, y reintenta.',
  },
  fila_invalida: {
    titulo: 'Fila inválida en A+W',
    accion:
      'Falta un dato obligatorio en el origen (p. ej. la descripción). Corrígelo en A+W y reintenta.',
  },
  no_encontrada_en_origen: {
    titulo: 'No existe en A+W',
    accion:
      'La referencia ya no está en el origen. Verifica que sea correcta; no se da de baja al producto.',
  },
  aplicacion_fallida: {
    titulo: 'No se pudo aplicar',
    accion:
      'Falló al guardar este producto; los demás siguieron. Reintenta y, si persiste, avisa a soporte.',
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
              r.erroresPorReferencia[0]?.mensaje ??
                `La lectura de ${referencia} no se pudo aplicar.`,
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
      <SheetContent size="lg" overlayClassName="bg-[var(--mt-scrim-sheet)]">
        <SheetHeader className="shrink-0 gap-3 border-line-divider bg-surface-card px-6 py-5 pr-12">
          <div className="flex items-center gap-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-info-bg text-info-fg">
              <RefreshCw className="h-5 w-5" strokeWidth={1.6} aria-hidden />
            </span>
            <div className="space-y-1">
              <p className="text-2xs font-medium uppercase tracking-wide text-ink-muted">
                Datos maestros · A+W
              </p>
              <SheetTitle className="text-xl text-ink">Sincronización de productos A+W</SheetTitle>
            </div>
          </div>
          <SheetDescription className="text-xs leading-relaxed text-ink-muted">
            Lee los productos de A+W y actualiza el catálogo del ERP.
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          <div className="space-y-5">
            <section aria-labelledby="origen-productos-aw">
              <h3 id="origen-productos-aw" className="mb-3 text-sm font-semibold text-ink-strong">
                1. Origen de datos
              </h3>
              <ControlOrigenAw
                area="productos"
                enabled={open}
                className="mt-0 rounded-lg bg-surface-card p-4 shadow-card-flat"
              />
            </section>
            <div className="flex gap-3 rounded-lg bg-info-bg p-4 text-info-fg" role="note">
              <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={1.6} aria-hidden />
              <div className="space-y-1">
                <p className="text-sm font-medium">Tus datos fiscales se conservan</p>
                <p className="text-xs leading-relaxed">
                  Los datos fiscales que ya completaste no se sobrescriben al sincronizar.
                </p>
              </div>
            </div>
            <section aria-labelledby="resultado-productos-aw" className="space-y-3">
              <h3 id="resultado-productos-aw" className="text-sm font-semibold text-ink-strong">
                2. Resultado de sincronización
              </h3>
              {sincronizar.isPending && (
                <div
                  role="status"
                  className="flex items-center gap-3 rounded-lg bg-surface-card p-4 text-sm text-ink-secondary shadow-card-flat"
                >
                  <Loader2 className="h-4 w-4 animate-spin text-brand" aria-hidden />
                  Leyendo y aplicando productos. Espera a que termine la sincronización.
                </div>
              )}
              {resumen == null && !sincronizar.isPending && (
                <div className="flex flex-col items-center rounded-lg bg-surface-card px-5 py-8 text-center shadow-card-flat">
                  <span className="mb-3 flex h-10 w-10 items-center justify-center rounded-lg bg-surface-muted text-ink-muted">
                    <PackageSearch className="h-5 w-5" strokeWidth={1.6} aria-hidden />
                  </span>
                  <p className="text-sm font-medium text-ink">Listo para sincronizar</p>
                  <p className="mt-2 max-w-sm text-xs leading-relaxed text-ink-muted">
                    Inicia la sincronización para ver los productos leídos, creados y actualizados,
                    junto con los pendientes que requieran revisión.
                  </p>
                </div>
              )}
              {resumen != null && (
                <>
                  <div className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <p className="text-sm font-medium text-ink">Último resultado</p>
                      <Badge
                        variant={
                          resumen.errores + resumen.conflictos + resumen.pendientes > 0
                            ? 'warning'
                            : 'success'
                        }
                      >
                        {resumen.errores + resumen.conflictos + resumen.pendientes > 0
                          ? 'Requiere revisión'
                          : 'Completa'}
                      </Badge>
                    </div>
                    <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3" aria-label="Resumen">
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
                      ).map(([titulo, valor]) => (
                        <div key={titulo} className="rounded-md bg-surface-subtle px-3 py-2.5">
                          <dt className="text-xs text-ink-muted">{titulo}</dt>
                          <dd className="mt-1 font-mono text-2xl font-semibold tabular-nums text-ink">
                            {valor}
                          </dd>
                        </div>
                      ))}
                    </dl>
                  </div>
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
            </section>
          </div>
        </SheetBody>
        <SheetFooter className="shrink-0 items-center justify-between border-line-divider bg-surface-card px-6 py-4 sm:justify-between">
          <Button size="lg" variant="ghost" onClick={() => onOpenChange(false)}>
            Cerrar
          </Button>
          <Button
            size="lg"
            onClick={handleIniciar}
            disabled={sincronizar.isPending}
            title={sincronizar.isPending ? 'La sincronización está en curso' : undefined}
          >
            <RefreshCw
              className={sincronizar.isPending ? 'h-3.5 w-3.5 animate-spin' : 'h-3.5 w-3.5'}
              aria-hidden
            />
            {sincronizar.isPending ? 'Sincronizando…' : 'Iniciar sincronización'}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
