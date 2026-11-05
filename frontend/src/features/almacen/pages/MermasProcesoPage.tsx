import { useState } from 'react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton, TabsEstado, TextAreaField } from '@/components/erp';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useDecidirMerma, useMermasProceso, type MermaProceso } from '@/features/almacen/api/useMermasProceso';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const ESTADOS = { 1: 'PENDIENTE', 2: 'APLICADA', 3: 'RECHAZADA' } as const;
const ESTADO_UI: Record<MermaProceso['estado'], { texto: string; variante: BadgeProps['variant'] }> = {
  PENDIENTE: { texto: 'Pendiente de autorizar', variante: 'warning' },
  APLICADA: { texto: 'Aplicada', variante: 'success' },
  RECHAZADA: { texto: 'Rechazada', variante: 'neutral' },
};
const num = (n: number | null, dec: number) =>
  n === null ? '—' : n.toLocaleString('es-MX', { minimumFractionDigits: dec, maximumFractionDigits: dec });

/**
 * A4.4 — Desperdicio de la mesa de corte y merma por etapa y motivo. Todo lo informa A+W; aquí se consulta por mesa y
 * etapa, y la merma pendiente se autoriza o se rechaza. Sin autorización la merma no se aplica ni avisa a Contabilidad.
 */
export function MermasProcesoPage() {
  const [tab, setTab] = useState<number | undefined>(undefined);
  const [autorizando, setAutorizando] = useState<MermaProceso | null>(null);
  const [rechazando, setRechazando] = useState<MermaProceso | null>(null);
  const [motivoRechazo, setMotivoRechazo] = useState<string | null>(null);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.AlmacenMermasAutorizar);
  const query = useMermasProceso({});
  const decidir = useDecidirMerma();
  const todas = query.data ?? [];
  const items = tab ? todas.filter((m) => m.estado === ESTADOS[tab as 1 | 2 | 3]) : todas;
  const conteos = {
    1: todas.filter((m) => m.estado === 'PENDIENTE').length,
    2: todas.filter((m) => m.estado === 'APLICADA').length,
    3: todas.filter((m) => m.estado === 'RECHAZADA').length,
  };
  const aplicadas = todas.filter((m) => m.estado === 'APLICADA');
  const desperdicioM2 = aplicadas.filter((m) => m.tipo === 'DESPERDICIO_MESA').reduce((s, m) => s + m.m2, 0);
  const valorAplicado = aplicadas.reduce((s, m) => s + m.valorMxn, 0);

  function resolver(accion: 'autorizar' | 'rechazar', m: MermaProceso) {
    decidir.mutate(
      { id: m.id, accion, motivo: motivoRechazo ?? undefined },
      {
        onSuccess: () => {
          toast.success(accion === 'autorizar' ? 'Merma autorizada y aplicada' : 'Merma rechazada');
          setAutorizando(null);
          setRechazando(null);
          setMotivoRechazo(null);
        },
        onError: (e) => {
          toast.error(esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo guardar la decisión');
          setAutorizando(null);
        },
      },
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Desperdicio y merma"
        descripcion="Desperdicio diario de cada mesa de corte y merma por etapa y motivo, como los informa A+W. La merma se aplica hasta que se autoriza."
      />

      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <KpiCard etiqueta="Desperdicio de mesas aplicado" valor={`${num(desperdicioM2, 2)} m²`} tono="info" />
        <KpiCard
          etiqueta="Mermas pendientes de autorizar"
          valor={conteos[1]}
          tono={conteos[1] > 0 ? 'warning' : 'success'}
          subdato="No bajan producción en proceso hasta autorizarse"
        />
        <KpiCard
          etiqueta="Valor absorbido en el costo del PT"
          valor={<MoneyDisplay amount={valorAplicado} currency="MXN" strong />}
          tono="neutral"
          subdato="m² × costo promedio de la hoja"
        />
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 1, label: 'Pendientes' },
              { valor: 2, label: 'Aplicadas' },
              { valor: 3, label: 'Rechazadas' },
            ]}
            valor={tab}
            onChange={setTab}
            conteos={conteos}
            aria-label="Filtrar por estado"
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudo cargar el desperdicio y la merma"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={6} columns={[{ width: 'w-24' }, { width: 'w-32' }, { width: 'w-48' }, { width: 'w-20' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            title="Sin registros"
            description="A+W aún no ha informado desperdicio de mesa ni merma con este filtro."
          />
        ) : (
          <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
            {conteos[1] > 0 && !puedeAutorizar && (
              <AvisoEnLinea tono="advertencia">
                Hay mermas pendientes. Solo quien tiene la facultad de autorizar mermas puede aplicarlas o rechazarlas.
              </AvisoEnLinea>
            )}
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Fecha</TableHead>
                    <TableHead>Tipo</TableHead>
                    <TableHead>Mesa o etapa</TableHead>
                    <TableHead>Motivo</TableHead>
                    <TableHead>Artículo</TableHead>
                    <TableHead className="text-right">Kg</TableHead>
                    <TableHead className="text-right">m²</TableHead>
                    <TableHead className="text-right">Valor</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead>Acciones</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((m) => (
                    <TableRow key={m.id} className="hover:bg-surface-subtle">
                      <TableCell className="whitespace-nowrap">
                        <DateTimeDisplay value={m.fecha} variant="corta" />
                      </TableCell>
                      <TableCell>{m.tipo === 'MERMA' ? 'Merma' : 'Desperdicio de mesa'}</TableCell>
                      <TableCell className="whitespace-nowrap">
                        {m.mesa ? <span className="font-mono text-xs">{m.mesa}</span> : <span className="capitalize">{m.etapa}</span>}
                      </TableCell>
                      <TableCell>{m.motivo}</TableCell>
                      <TableCell>
                        <span className="font-mono text-xs">{m.articuloClave ?? m.articuloId}</span>
                        {m.articuloDescripcion ? ` · ${m.articuloDescripcion}` : ''}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{num(m.kg, 2)}</TableCell>
                      <TableCell className="text-right tabular-nums">{num(m.m2, 2)}</TableCell>
                      <TableCell className="text-right">
                        <MoneyDisplay amount={m.valorMxn} currency="MXN" />
                      </TableCell>
                      <TableCell>
                        <Badge variant={ESTADO_UI[m.estado].variante}>{ESTADO_UI[m.estado].texto}</Badge>
                        {m.autorizadoPor && (
                          <div className="text-xs text-ink-muted">
                            {m.estado === 'RECHAZADA' ? 'Rechazó' : 'Autorizó'} {m.autorizadoPor}
                          </div>
                        )}
                        {m.motivoRechazo && <div className="text-xs text-ink-muted">{m.motivoRechazo}</div>}
                      </TableCell>
                      <TableCell>
                        {m.estado === 'PENDIENTE' &&
                          (puedeAutorizar ? (
                            <div className="flex flex-wrap gap-2">
                              <Button size="sm" onClick={() => setAutorizando(m)}>
                                Autorizar
                              </Button>
                              <Button size="sm" variant="outline" onClick={() => setRechazando(m)}>
                                Rechazar
                              </Button>
                            </div>
                          ) : (
                            <span className="text-xs text-ink-muted">Requiere autorización</span>
                          ))}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </div>
        )}
      </ListadoModulo>

      <ConfirmarAccionDialog
        open={autorizando !== null}
        onOpenChange={(abierto) => !abierto && setAutorizando(null)}
        titulo="Autorizar merma"
        resumen={
          autorizando
            ? `${autorizando.etapa} · ${autorizando.motivo}: ${num(autorizando.kg, 2)} kg, ${num(autorizando.m2, 2)} m². Baja la producción en proceso por ${autorizando.valorMxn.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' })} y ese valor se absorbe en el costo del producto terminado.`
            : ''
        }
        verbo="Autorizar merma"
        tono="primario"
        pendiente={decidir.isPending}
        onConfirmar={() => autorizando && resolver('autorizar', autorizando)}
      />
      <ConfirmarAccionDialog
        open={rechazando !== null}
        onOpenChange={(abierto) => {
          if (!abierto) {
            setRechazando(null);
            setMotivoRechazo(null);
          }
        }}
        titulo="Rechazar merma"
        resumen={
          <div className="space-y-2">
            <p>La merma queda rechazada y no baja la producción en proceso. Indica el motivo.</p>
            <TextAreaField value={motivoRechazo} onChange={setMotivoRechazo} maxLength={500} minRows={2} />
          </div>
        }
        verbo="Rechazar merma"
        tono="peligro"
        pendiente={decidir.isPending}
        onConfirmar={() => {
          if (!motivoRechazo?.trim()) {
            toast.error('Indica el motivo del rechazo.');
            return;
          }
          if (rechazando) resolver('rechazar', rechazando);
        }}
      />
    </div>
  );
}
