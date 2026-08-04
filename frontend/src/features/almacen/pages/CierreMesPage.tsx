import { useState } from 'react';
import { AlertTriangle, CheckCircle2, Lock } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useEjecutarCierreMes } from '@/features/almacen/api/useSaldosCierreReportes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { Field } from '@/features/almacen/components/internal/Field';

/**
 * <c>P12 — Cierre de mes</c> (doc 07 §FE-F6-PR1). Pantalla del Jefe
 * Almacén para cerrar el periodo mensual. El backend valida que no
 * existan conteos pendientes ni movimientos en estado Borrador/Validado
 * del mes que se quiere cerrar — si falla, devuelve
 * <c>422 CIERRE_PERIODO_NO_LIMPIO</c> con detalle.
 *
 * <para>Permiso: <c>almacen.cierre-mes.ejecutar</c>. Idempotency-Key
 * obligatorio en el endpoint backend.</para>
 */
export function CierreMesPage() {
  const idempotencyKey = useFormIdempotencyKey();
  const ejecutar = useEjecutarCierreMes();

  const hoy = new Date();
  const [anio, setAnio] = useState<number>(hoy.getFullYear());
  const [mes, setMes] = useState<number>(hoy.getMonth() + 1); // 1-12
  const [confirmOpen, setConfirmOpen] = useState(false);

  function ejecutarCierre() {
    ejecutar.mutate(
      { command: { anio, mes }, idempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success('Cierre ejecutado', {
            description: `Periodo ${resp.anio}-${String(resp.mes).padStart(2, '0')} cerrado el ${new Date(resp.cerradoAt).toLocaleString('es-MX')}.`,
          });
          setConfirmOpen(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'CIERRE_PERIODO_NO_LIMPIO') {
              toast.error('Periodo con pendientes', {
                description:
                  error.problem.detail ??
                  'Hay conteos o movimientos pendientes en el periodo. Resuélvelos antes de cerrar.',
              });
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error inesperado al ejecutar el cierre.');
        },
      },
    );
  }

  const MES_NOMBRES = [
    '',
    'Enero',
    'Febrero',
    'Marzo',
    'Abril',
    'Mayo',
    'Junio',
    'Julio',
    'Agosto',
    'Septiembre',
    'Octubre',
    'Noviembre',
    'Diciembre',
  ];

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <Lock className="h-6 w-6 text-primary" />
          Cierre de mes
        </h1>
        <p className="text-sm text-muted-foreground">
          Cierra el periodo mensual del módulo Almacén. Una vez cerrado, no
          podrán registrarse ni editarse movimientos con fecha dentro del
          periodo.
        </p>
      </header>

      <section className="rounded-md border bg-amber-50 px-4 py-3 text-sm">
        <h2 className="flex items-center gap-2 font-medium text-amber-900">
          <AlertTriangle className="h-4 w-4" />
          Checklist antes de cerrar
        </h2>
        <ul className="mt-2 list-inside list-disc space-y-1 text-amber-800">
          <li>Conteos del periodo aplicados (sin pendientes en conciliación).</li>
          <li>
            Recepciones en estado Borrador resueltas (registradas o canceladas).
          </li>
          <li>Salidas y devoluciones registradas en firme.</li>
          <li>
            Conciliación cross-módulo con CxP completa (NC fiscales aplicadas).
          </li>
        </ul>
        <p className="mt-2 text-amber-800">
          El backend valida estas condiciones y rechaza el cierre si no se
          cumplen (código <code>CIERRE_PERIODO_NO_LIMPIO</code>).
        </p>
      </section>

      <section className="rounded-md border p-4">
        <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
          Periodo a cerrar
        </h2>
        <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field label="Año" required>
            <Select
              value={String(anio)}
              onValueChange={(v) => setAnio(Number(v))}
            >
              <SelectTrigger aria-label="Año a cerrar">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {[anio - 2, anio - 1, anio, anio + 1].map((y) => (
                  <SelectItem key={y} value={String(y)}>
                    {y}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Field label="Mes" required>
            <Select
              value={String(mes)}
              onValueChange={(v) => setMes(Number(v))}
            >
              <SelectTrigger aria-label="Mes a cerrar">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                  <SelectItem key={m} value={String(m)}>
                    {String(m).padStart(2, '0')} — {MES_NOMBRES[m]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        </div>

        <div className="mt-4">
          <Button onClick={() => setConfirmOpen(true)} disabled={ejecutar.isPending}>
            <CheckCircle2 className="mr-2 h-4 w-4" />
            Ejecutar cierre {anio}-{String(mes).padStart(2, '0')}
          </Button>
        </div>
      </section>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              ¿Cerrar periodo {anio}-{String(mes).padStart(2, '0')}?
            </AlertDialogTitle>
            <AlertDialogDescription>
              Esta acción es irreversible. Después del cierre, no podrán
              registrarse ni editarse movimientos con fecha dentro del
              periodo. Asegúrate de haber revisado el checklist.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={ejecutar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={ejecutar.isPending}
              onClick={ejecutarCierre}
            >
              {ejecutar.isPending ? 'Cerrando…' : 'Cerrar periodo'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
