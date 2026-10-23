import { useState, type ReactNode } from 'react';
import { CheckCircle2, Send, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';
import {
  useAplicarComprobacion,
  useAprobarPasivoComprobacion,
  useAutorizarComprobacion,
  useAutorizarNivel1Aduanales,
  useAutorizarNivel2Aduanales,
  useEnviarComprobacionARevision,
  useRechazarComprobacion,
} from '@/features/cxp/api/useComprobaciones';
import {
  EstadoComprobacionGastos,
  TipoComprobacionGastos,
  TipoComprobacionGastosLabels,
  type ComprobacionGastosListItem,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

/** Campos mínimos de la comprobación: la lista y el detalle los comparten. */
export type ComprobacionAccionable = Pick<
  ComprobacionGastosListItem,
  'id' | 'version' | 'estado' | 'tipo' | 'montoTotal' | 'moneda'
>;

type Dialogo = 'autorizar' | 'n1' | 'n2' | 'aplicar' | 'aprobarPasivo' | 'rechazar' | null;

/**
 * Acciones de workflow de una comprobación de gastos. Misma lógica en la fila
 * de la bandeja y en el sub-topbar del detalle (CXP-29). Autorizar, firmas y
 * aplicar pasan por confirmación con resumen (CXP-14); el rechazo pide motivo
 * (CXP-13). El estado pendiente es propio de cada instancia.
 */
export function AccionesComprobacion({
  c,
  responsableNombre,
  className,
}: {
  c: ComprobacionAccionable;
  responsableNombre?: string | null;
  className?: string;
}) {
  const enviar = useEnviarComprobacionARevision();
  const autorizar = useAutorizarComprobacion();
  const aplicar = useAplicarComprobacion();
  const aprobarPasivo = useAprobarPasivoComprobacion();
  const autN1 = useAutorizarNivel1Aduanales();
  const autN2 = useAutorizarNivel2Aduanales();
  const rechazar = useRechazarComprobacion();
  const puedeAprobarN1 = useHasPermission(PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel1);
  const puedeAprobarN2 = useHasPermission(PermisosCanonicos.CuentasPorPagarComprobacionesAprobarNivel2);
  const puedeCapturar = useHasPermission(PermisosCanonicos.CuentasPorPagarComprobacionesCapturar);
  const [dialogo, setDialogo] = useState<Dialogo>(null);
  const [errorMotivo, setErrorMotivo] = useState<string | null>(null);

  const pendiente =
    enviar.isPending || autorizar.isPending || aplicar.isPending ||
    autN1.isPending || autN2.isPending || rechazar.isPending || aprobarPasivo.isPending;
  const base = { id: c.id, versionEsperada: c.version };
  const esAduanal = c.tipo === TipoComprobacionGastos.GastosAduanales;
  const esCajaChica = c.tipo === TipoComprobacionGastos.ReembolsoCajaChica;

  function ejecutar(etiqueta: string, p: Promise<unknown>) {
    p.then(
      () => toast.success(etiqueta),
      (error) =>
        toast.error(
          esApiError(error) ? (error.problem.detail ?? error.problem.title) : 'Error al ejecutar la acción.',
        ),
    ).finally(() => setDialogo(null));
  }

  function confirmar() {
    const idempotencyKey = crypto.randomUUID();
    switch (dialogo) {
      case 'autorizar':
        return ejecutar('Comprobación autorizada', autorizar.mutateAsync({ ...base, idempotencyKey }));
      case 'n1':
        return ejecutar('Firma Nivel 1 aplicada', autN1.mutateAsync({ ...base, idempotencyKey }));
      case 'n2':
        return ejecutar(
          'Firma Nivel 2 aplicada',
          autN2.mutateAsync({ ...base, idempotencyKey }),
        );
      case 'aplicar':
        return ejecutar('Comprobación aplicada', aplicar.mutateAsync({ ...base, idempotencyKey }));
      case 'aprobarPasivo':
        return ejecutar(
          'Pasivo aprobado; reposición generada',
          aprobarPasivo.mutateAsync({ ...base, idempotencyKey }),
        );
    }
  }

  const tipo = TipoComprobacionGastosLabels[c.tipo];
  const monto = <MoneyDisplay amount={c.montoTotal} currency={c.moneda} strong />;
  const quien = responsableNombre ? ` de ${responsableNombre}` : '';
  const resumen: Record<Exclude<Dialogo, 'rechazar' | null>, { titulo: string; verbo: string; texto: ReactNode }> = {
    autorizar: {
      titulo: 'Autorizar comprobación',
      verbo: 'Autorizar comprobación',
      texto: <>Autorizas la comprobación de {tipo}{quien} por {monto}.</>,
    },
    n1: {
      titulo: 'Firma Nivel 1',
      verbo: 'Aplicar firma Nivel 1',
      texto: <>Firmas en Nivel 1 la comprobación de {tipo}{quien} por {monto}. Falta la firma Nivel 2 de Dirección de Finanzas.</>,
    },
    n2: {
      titulo: 'Firma Nivel 2 (Dirección de Finanzas)',
      verbo: 'Aplicar firma Nivel 2',
      texto: <>Firmas en Nivel 2 la comprobación de {tipo}{quien} por {monto}. El sistema valida que no sea la misma persona que firmó el Nivel 1.</>,
    },
    aplicar: {
      titulo: 'Aplicar comprobación',
      verbo: 'Aplicar comprobación',
      texto: <>Se aplicará la comprobación de {tipo}{quien} por {monto}; no se puede deshacer.</>,
    },
    aprobarPasivo: {
      titulo: 'Aprobar pasivo',
      verbo: 'Aprobar pasivo',
      texto: <>Se aprobará el pasivo de la comprobación de {tipo}{quien} por {monto} y se generará su reposición para Tesorería; no se puede deshacer.</>,
    },
  };
  const abierto = dialogo !== null && dialogo !== 'rechazar' ? resumen[dialogo] : null;

  return (
    <div className={cn('flex flex-wrap items-center justify-end gap-1 empty:hidden', className)}>
      {c.estado === EstadoComprobacionGastos.Borrador && puedeCapturar && (
        <Button
          variant="outline"
          size="sm"
          disabled={pendiente}
          onClick={() =>
            ejecutar(
              'Enviada a revisión',
              enviar.mutateAsync({ ...base, idempotencyKey: crypto.randomUUID() }),
            )
          }
        >
          <Send className="mr-1 size-3.5" aria-hidden="true" />
          Enviar
        </Button>
      )}
      {c.estado === EstadoComprobacionGastos.PorRevisar && !esAduanal && !esCajaChica && puedeAprobarN1 && (
        <Button variant="outline" size="sm" onClick={() => setDialogo('autorizar')} disabled={pendiente}>
          <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
          Autorizar
        </Button>
      )}
      {esCajaChica &&
        (c.estado === EstadoComprobacionGastos.PendienteAprobarPasivo ||
          c.estado === EstadoComprobacionGastos.PorRevisar) &&
        puedeAprobarN1 && (
          <Button variant="outline" size="sm" onClick={() => setDialogo('aprobarPasivo')} disabled={pendiente}>
            <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
            Aprobar pasivo
          </Button>
        )}
      {c.estado === EstadoComprobacionGastos.PorRevisar && esAduanal && puedeAprobarN1 && (
        <Button variant="outline" size="sm" onClick={() => setDialogo('n1')} disabled={pendiente}>
          <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
          Firma N1
        </Button>
      )}
      {c.estado === EstadoComprobacionGastos.AutorizadaNivel1 && puedeAprobarN2 && (
        <Button variant="outline" size="sm" onClick={() => setDialogo('n2')} disabled={pendiente}>
          <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
          Firma N2 (DF)
        </Button>
      )}
      {c.estado === EstadoComprobacionGastos.Autorizada && puedeAprobarN1 && (
        <Button variant="outline" size="sm" onClick={() => setDialogo('aplicar')} disabled={pendiente}>
          Aplicar
        </Button>
      )}
      {(c.estado === EstadoComprobacionGastos.PorRevisar ||
        c.estado === EstadoComprobacionGastos.PendienteAprobarPasivo ||
        c.estado === EstadoComprobacionGastos.AutorizadaNivel1) &&
        puedeAprobarN1 && (
          <Button
            variant="ghost"
            size="sm"
            className="text-danger-fg"
            onClick={() => {
              setErrorMotivo(null);
              setDialogo('rechazar');
            }}
            disabled={pendiente}
          >
            <XCircle className="mr-1 size-3.5" aria-hidden="true" />
            Rechazar
          </Button>
        )}

      {abierto && (
        <ConfirmarAccionDialog
          open
          onOpenChange={(o) => !o && setDialogo(null)}
          titulo={abierto.titulo}
          resumen={abierto.texto}
          verbo={abierto.verbo}
          tono="primario"
          pendiente={pendiente}
          onConfirmar={confirmar}
        />
      )}
      {dialogo === 'rechazar' && (
        <MotivoDialog
          titulo="Rechazar comprobación"
          descripcion={<>Comprobación de {tipo}{quien}. El motivo queda registrado.</>}
          etiqueta="Motivo del rechazo"
          min={5}
          max={500}
          verbo="Rechazar comprobación"
          error={errorMotivo}
          onCerrar={() => setDialogo(null)}
          onConfirmar={async (motivo) => {
            try {
              await rechazar.mutateAsync({
                ...base,
                command: { motivo },
                idempotencyKey: crypto.randomUUID(),
              });
              toast.success('Comprobación rechazada');
              setDialogo(null);
            } catch (e) {
              setErrorMotivo(
                esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo rechazar la comprobación.',
              );
            }
          }}
        />
      )}
    </div>
  );
}
