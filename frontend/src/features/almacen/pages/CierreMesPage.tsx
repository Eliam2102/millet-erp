import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Check, CheckCircle2, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  useEjecutarCierreMes,
  usePeriodosCierre,
  useRevisionPreviaCierre,
} from '@/features/almacen/api/useSaldosCierreReportes';
import { esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import { Field } from '@/features/almacen/components/internal/Field';

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

const claveDe = (anio: number, mes: number) => `${anio}-${String(mes).padStart(2, '0')}`;

/**
 * <c>P12 — Cierre de mes</c> (doc 07 §FE-F6-PR1). Pantalla del Jefe
 * Almacén para cerrar el periodo mensual. El backend valida que no
 * existan conteos pendientes ni movimientos en estado Borrador/Validado
 * del mes que se quiere cerrar — si falla, devuelve un 422 con
 * <c>CIERRE_CONTEOS_PENDIENTES</c>, <c>CIERRE_MOVIMIENTOS_PENDIENTES</c>
 * o <c>CIERRE_VALES_PENDIENTES</c> con detalle.
 *
 * <para>La revisión previa lee del servidor las mismas validaciones que el cierre
 * (<c>GET cierre-mes/revision-previa</c>); el primario queda deshabilitado con motivo
 * mientras haya bloqueos.</para>
 *
 * <para>Permiso: <c>almacen.cierre-mes.ejecutar</c>. Idempotency-Key
 * obligatorio en el endpoint backend. El cierre aplica a todas las
 * sucursales (ADR-0051, bypass <c>almacen.cierre-mes.gestionar-todas-sucursales</c>).</para>
 */
export function CierreMesPage() {
  const idempotencyKey = useFormIdempotencyKey();
  const ejecutar = useEjecutarCierreMes();
  const periodosQuery = usePeriodosCierre();

  const hoy = new Date();
  // Por defecto, el mes anterior (el mes en curso y los futuros no se pueden cerrar).
  const anterior = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1);
  const [anio, setAnio] = useState<number>(anterior.getFullYear());
  const [mes, setMes] = useState<number>(anterior.getMonth() + 1); // 1-12
  const [confirmOpen, setConfirmOpen] = useState(false);
  const revisionQuery = useRevisionPreviaCierre(anio, mes);
  const revision = revisionQuery.data;
  const periodo = claveDe(anio, mes);

  const bloqueos = revision?.checks.filter((c) => !c.ok).length ?? 0;
  let motivoBloqueo: string | null = null;
  if (revisionQuery.isError) motivoBloqueo = 'No se pudo revisar el periodo. Recarga la página.';
  else if (!revision) motivoBloqueo = 'Revisando el periodo…';
  else if (revision.yaCerrado) motivoBloqueo = `El periodo ${periodo} ya está cerrado.`;
  else if (revision.periodoNoConcluido) motivoBloqueo = `El periodo ${periodo} aún no termina.`;
  else if (bloqueos > 0)
    motivoBloqueo = `Resuelve ${bloqueos} ${bloqueos === 1 ? 'pendiente' : 'pendientes'} de la revisión previa para cerrar ${periodo}.`;

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
          setConfirmOpen(false);
          if (esApiError(error)) {
            if (
              error.code === 'CIERRE_CONTEOS_PENDIENTES' ||
              error.code === 'CIERRE_MOVIMIENTOS_PENDIENTES' ||
              error.code === 'CIERRE_VALES_PENDIENTES'
            ) {
              toast.error('Periodo con pendientes', {
                description: error.problem.detail ?? error.problem.title,
              });
              return;
            }
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
            return;
          }
          toast.error('Error inesperado al ejecutar el cierre.');
        },
      },
    );
  }

  const periodos = periodosQuery.data ?? [];
  // El periodo elegido siempre debe existir como opción (aunque la lista aún no cargue).
  const opciones = periodos.some((p) => p.anio === anio && p.mes === mes)
    ? periodos
    : [{ anio, mes, cerrado: false, cerradoAt: null }, ...periodos];

  return (
    <div className="space-y-6">
      <PageHeader
        titulo="Cierre de mes"
        descripcion="Cierra el periodo mensual del módulo Almacén. Una vez cerrado, no podrán registrarse ni editarse movimientos con fecha dentro del periodo."
      />

      <section className="rounded-lg border border-line-row bg-surface-card p-4">
        <h2 className="text-sm font-semibold text-ink-strong">Periodo a cerrar</h2>
        <div className="mt-3 max-w-sm">
          <Field label="Periodo" required>
            <Select
              value={periodo}
              onValueChange={(v) => {
                const [a, m] = v.split('-').map(Number);
                setAnio(a);
                setMes(m);
              }}
            >
              <SelectTrigger aria-label="Periodo a cerrar">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {opciones.map((p) => (
                  <SelectItem key={claveDe(p.anio, p.mes)} value={claveDe(p.anio, p.mes)}>
                    {claveDe(p.anio, p.mes)} — {MES_NOMBRES[p.mes]} ({p.cerrado ? 'Cerrado' : 'Abierto'})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        </div>
      </section>

      <section
        aria-labelledby="revision-previa-cierre-titulo"
        className="overflow-hidden rounded-lg border border-line-row bg-surface-card"
        data-component="revision-previa-cierre"
      >
        <div className="border-b border-line-row px-4 py-3">
          <h2 id="revision-previa-cierre-titulo" className="text-sm font-semibold text-ink-strong">
            Revisión previa
          </h2>
          <p className="text-xs text-ink-muted">
            {revision
              ? bloqueos === 0
                ? `El periodo ${periodo} cumple las condiciones para cerrarse.`
                : `Faltan ${bloqueos} de ${revision.checks.length} condiciones para cerrar ${periodo}.`
              : 'Revisando el periodo…'}
          </p>
        </div>
        <ul>
          {revision?.checks.map((c) => (
            <li
              key={c.codigo}
              data-check={c.codigo}
              data-ok={c.ok}
              className="flex min-h-11 items-center gap-3 border-b border-line-row px-4 py-2 text-sm last:border-b-0"
            >
              {c.ok ? (
                <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-success-bg text-success-fg">
                  <Check aria-hidden="true" className="size-3" />
                  <span className="sr-only">Listo:</span>
                </span>
              ) : (
                <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-warning-bg text-warning-fg">
                  <TriangleAlert aria-hidden="true" className="size-3" />
                  <span className="sr-only">Pendiente:</span>
                </span>
              )}
              <span className="min-w-0 flex-1 text-ink-strong">{c.titulo}</span>
              <span className="text-xs text-ink-muted">{c.detalle}</span>
            </li>
          ))}
        </ul>
      </section>

      {revisionQuery.isError && (
        <AvisoEnLinea tono="error" titulo="No se pudo revisar el periodo">
          Recarga la página. Si el problema continúa, avisa a soporte.
        </AvisoEnLinea>
      )}

      <div>
        <Button
          onClick={() => setConfirmOpen(true)}
          disabled={motivoBloqueo !== null || ejecutar.isPending}
          aria-describedby={motivoBloqueo ? 'motivo-cierre-bloqueado' : undefined}
        >
          <CheckCircle2 className="mr-2 size-3.5" />
          Ejecutar cierre {periodo}
        </Button>
        {motivoBloqueo && (
          <p id="motivo-cierre-bloqueado" className="mt-2 text-xs text-ink-muted">
            {motivoBloqueo}
          </p>
        )}
      </div>

      <ConfirmarAccionDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        titulo={`¿Cerrar periodo ${periodo}?`}
        resumen={
          <p>
            Esta acción es irreversible. Después del cierre no podrán registrarse ni editarse
            movimientos con fecha de {MES_NOMBRES[mes]} {anio}. El cierre aplica a todas las
            sucursales.
          </p>
        }
        verbo="Cerrar periodo"
        tono="peligro"
        pendiente={ejecutar.isPending}
        onConfirmar={ejecutarCierre}
      />
    </div>
  );
}
