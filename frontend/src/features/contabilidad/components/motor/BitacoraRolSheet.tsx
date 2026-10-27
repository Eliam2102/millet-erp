import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Skeleton } from '@/components/ui/skeleton';
import { formatDateTime } from '@/lib/datetime';
import { useBitacoraRol } from '../../api/motor';
import type { RolCuenta } from '../../api/motor-types';
import { mensajeMotor } from '../../lib/motor';

interface Props {
  rol: RolCuenta | null;
  onClose: () => void;
}

/** Bitácora de cambios de cuenta de un rol (C1.4-c): quién, cuándo, cuenta anterior y nueva, motivo. */
export function BitacoraRolSheet({ rol, onClose }: Props) {
  const bitacora = useBitacoraRol(rol?.id ?? null);
  const cambios = [...(bitacora.data ?? [])].sort((a, b) => b.cambiadoEn.localeCompare(a.cambiadoEn));

  return (
    <Sheet open={!!rol} onOpenChange={(o) => !o && onClose()}>
      <SheetContent size="form" side="right" data-print="hidden">
        <SheetHeader>
          <SheetTitle>Bitácora del rol</SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            <span className="font-mono">{rol?.codigo}</span> · {rol?.descripcion}
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          {bitacora.isLoading ? (
            <div className="flex flex-col gap-2" aria-label="Cargando bitácora">
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
            </div>
          ) : bitacora.isError ? (
            <p role="alert" className="text-sm text-danger-fg">
              No se pudo cargar la bitácora. {mensajeMotor(bitacora.error)}
            </p>
          ) : cambios.length === 0 ? (
            <p className="text-sm text-ink-muted">El rol no ha cambiado de cuenta.</p>
          ) : (
            <ul aria-label="Cambios de cuenta" className="flex flex-col gap-3">
              {cambios.map((c) => (
                <li key={c.id} className="rounded-md border border-line-divider px-3 py-2.5 text-sm">
                  <div className="flex flex-wrap items-baseline gap-x-2">
                    <span className="font-medium text-ink-strong">{c.usuario}</span>
                    <span className="ml-auto text-xs text-ink-muted">{formatDateTime(c.cambiadoEn)}</span>
                  </div>
                  <p className="mt-1 text-ink-secondary">
                    {c.cuentaAnterior ?? 'Sin cuenta'} → <span className="text-ink">{c.cuentaNueva}</span>
                  </p>
                  {c.motivo && <p className="mt-1 whitespace-pre-line text-xs text-ink-muted">Motivo: {c.motivo}</p>}
                </li>
              ))}
            </ul>
          )}
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
