import { problemaUsuario } from '@/lib/error-usuario';
import { errorUsuario } from '@/lib/error-usuario';
import { useState } from 'react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, Field, TableSkeleton, TextAreaField } from '@/components/erp';
import { DecimalField } from '@/components/erp/forms/DecimalField';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useAlmacenes } from '@/features/almacen/api/useAlmacenes';
import {
  useAclararDiferencia,
  useCorridasConciliacion,
  useDiferenciasConciliacion,
  useEjecutarConciliacion,
  useEventosDelDia,
  useExistenciaConciliacion,
  type CorridaConciliacion,
  type DiferenciaConciliacion,
  type EventosDelDia,
  type ExistenciaLinea,
} from '@/features/almacen/api/useConciliacionDiaria';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const num = (n: number) => n.toLocaleString('es-MX', { maximumFractionDigits: 4 });
const llave = (l: { articuloId: string; productoAwVarianteId: string | null; lote: string | null }) =>
  `${l.articuloId}|${l.productoAwVarianteId ?? ''}|${l.lote ?? ''}`;
const hoy = () => new Date().toLocaleDateString('en-CA');
const errorTexto = (e: unknown, defecto: string) => errorUsuario(e, defecto);

/**
 * A4.6 — Conciliación diaria del ERP contra A+W. Se captura el físico por hoja/medida/lote y no hay tolerancia: toda
 * diferencia de menos es una salida no planificada que baja la existencia y queda por aclarar con la mesa de corte.
 */
export function ConciliacionDiariaPage() {
  const puedeCorrer = useHasPermission(PermisosCanonicos.AlmacenInventariosCrear);
  const [almacenId, setAlmacenId] = useState<string | undefined>(undefined);
  const [fecha, setFecha] = useState(hoy());
  const [fuente, setFuente] = useState<'AW' | 'CONTEO'>('AW');
  const [fisico, setFisico] = useState<Record<string, number | null>>({});
  const [ultima, setUltima] = useState<CorridaConciliacion | null>(null);
  const [aclarando, setAclarando] = useState<DiferenciaConciliacion | null>(null);
  const [motivo, setMotivo] = useState<string | null>(null);

  const almacenes = useAlmacenes({ limit: 200 });
  const [confirmarPendientes, setConfirmarPendientes] = useState<string | null>(null);
  const existencia = useExistenciaConciliacion(almacenId, fecha);
  const eventos = useEventosDelDia(almacenId, fecha);
  const diferencias = useDiferenciasConciliacion();
  const corridas = useCorridasConciliacion();
  const ejecutar = useEjecutarConciliacion();
  const aclarar = useAclararDiferencia();

  const filas = existencia.data ?? [];
  const capturadas = filas.filter((f) => fisico[llave(f)] != null);
  const pendientes = (diferencias.data ?? []).filter((d) => d.estado === 'PENDIENTE');
  const valorPendiente = pendientes.filter((d) => d.sentido === 'FALTANTE').reduce((s, d) => s + d.valorMxn, 0);

  function conciliar(confirmar = false) {
    if (!almacenId || capturadas.length === 0) return;
    ejecutar.mutate(
      {
        almacenId,
        fecha,
        fuente,
        ...(confirmar && { confirmarPendientes: true }),
        lineas: capturadas.map((f) => ({
          articuloId: f.articuloId,
          productoAwVarianteId: f.productoAwVarianteId,
          lote: f.lote,
          fisico: fisico[llave(f)] as number,
        })),
      },
      {
        onSuccess: (c) => {
          setConfirmarPendientes(null);
          setUltima(c);
          setFisico({});
          toast.success(c.diferencias === 0 ? 'Conciliación sin diferencias' : `Conciliación con ${c.diferencias} diferencia(s)`);
        },
        onError: (e) => {
          if (esApiError(e) && e.code === 'CONCILIACION_EVENTOS_PENDIENTES') {
            setConfirmarPendientes(e.problem.detail ?? 'Hay eventos del día sin aplicar.');
            return;
          }
          setConfirmarPendientes(null);
          toast.error(errorTexto(e, 'No se pudo conciliar'));
        },
      },
    );
  }

  function guardarAclaracion() {
    if (!aclarando) return;
    if (!motivo?.trim()) {
      toast.error('Indica el motivo que aclaró la mesa de corte.');
      return;
    }
    aclarar.mutate(
      { id: aclarando.id, motivo },
      {
        onSuccess: () => {
          toast.success('Diferencia aclarada');
          setAclarando(null);
          setMotivo(null);
        },
        onError: (e) => {
          toast.error(errorTexto(e, 'No se pudo guardar la aclaración'));
          setAclarando(null);
        },
      },
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Conciliación diaria"
        descripcion="Compara la existencia del ERP con el físico por hoja, medida y lote, y aclara con la mesa de corte cada diferencia sin tolerancia."
      />

      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <KpiCard
          etiqueta="Diferencias por aclarar"
          valor={diferencias.isSuccess ? pendientes.length : <span className="text-ink-muted">—</span>}
          tono={pendientes.length > 0 ? 'warning' : 'success'}
          subdato="Esperan la aclaración de la mesa de corte"
        />
        <KpiCard
          etiqueta="Valor de salidas no planificadas por aclarar"
          valor={<MoneyDisplay amount={diferencias.isSuccess ? valorPendiente : null} currency="MXN" />}
          tono="neutral"
        />
        <KpiCard etiqueta="Corridas registradas" valor={corridas.isSuccess ? (corridas.data?.length ?? 0) : <span className="text-ink-muted">—</span>} tono="info" />
      </div>

      <Tabs defaultValue="conciliar">
        <TabsList aria-label="Secciones de la conciliación">
          <TabsTrigger value="conciliar">Conciliar</TabsTrigger>
          <TabsTrigger value="aclarar" count={diferencias.isSuccess ? pendientes.length : undefined}>
            Por aclarar
          </TabsTrigger>
          <TabsTrigger value="historico">Histórico de diferencias</TabsTrigger>
          <TabsTrigger value="corridas">Corridas</TabsTrigger>
        </TabsList>

        <TabsContent value="conciliar" className="space-y-4 pt-4">
          <div className="flex flex-wrap items-end gap-3">
            <Field label="Almacén" className="gap-1">
              {(c) => (
                <Select value={almacenId ?? ''} onValueChange={(v) => { setAlmacenId(v); setFisico({}); setUltima(null); }}>
                  <SelectTrigger {...c} className="w-72">
                    <SelectValue placeholder="Elige el almacén de MP" />
                  </SelectTrigger>
                  <SelectContent>
                    {(almacenes.data?.items ?? []).map((a) => (
                      <SelectItem key={a.id} value={a.id}>
                        {a.clave} · {a.nombre}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </Field>
            <Field label="Día a conciliar" className="gap-1">
              <Input
                type="date"
                value={fecha}
                max={hoy()}
                onChange={(e) => { setFecha(e.target.value); setFisico({}); setUltima(null); }}
                className="w-44"
              />
            </Field>
            <Field label="Físico reportado por" className="gap-1">
              {(c) => (
                <Select value={fuente} onValueChange={(v) => setFuente(v as 'AW' | 'CONTEO')}>
                  <SelectTrigger {...c} className="w-48">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="AW">A+W</SelectItem>
                    <SelectItem value="CONTEO">Conteo físico</SelectItem>
                  </SelectContent>
                </Select>
              )}
            </Field>
            {puedeCorrer && (
              <Button onClick={() => conciliar()} disabled={!almacenId || capturadas.length === 0 || ejecutar.isPending}>
                Conciliar {capturadas.length > 0 ? `(${capturadas.length})` : ''}
              </Button>
            )}
          </div>

          {almacenId && eventos.isSuccess && (eventos.data.pendientes.length > 0 || eventos.data.mesa.length > 0) && (
            <EventosDelDiaPanel eventos={eventos.data} />
          )}

          {ultima &&
            (ultima.diferencias === 0 ? (
              <p role="status" className="rounded-lg border border-line-row bg-surface-card p-3 text-sm">
                <Badge variant="success">Sin diferencias</Badge> Corrida del <DateTimeDisplay value={ultima.fecha} variant="corta" /> registrada: {ultima.lineasComparadas}{' '}
                partidas comparadas, sin salidas no planificadas.
              </p>
            ) : (
              <AvisoEnLinea tono="advertencia">
                Corrida del <DateTimeDisplay value={ultima.fecha} variant="corta" />: {ultima.diferencias} diferencia(s) en {ultima.lineasComparadas} partidas. Las salidas no planificadas ya bajaron la existencia y esperan la aclaración de la mesa de corte.
              </AvisoEnLinea>
            ))}

          {!almacenId ? (
            <EmptyState title="Elige un almacén" description="Se muestra su existencia teórica para capturar el físico." />
          ) : existencia.isError ? (
            <ErrorState
              title="No se pudo cargar la existencia"
              problem={problemaUsuario(existencia.error)}
              onRetry={() => existencia.refetch()}
            />
          ) : existencia.isLoading ? (
            <TableSkeleton rows={6} columns={[{ width: 'w-24' }, { width: 'w-48' }, { width: 'w-20' }]} />
          ) : filas.length === 0 ? (
            <EmptyState title="Sin existencia" description="Este almacén no tiene existencia que conciliar." />
          ) : (
            <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
              <TableContainer>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Artículo</TableHead>
                      <TableHead>Medida</TableHead>
                      <TableHead>Lote</TableHead>
                      <TableHead className="text-right">Existencia ERP</TableHead>
                      <TableHead>Unidad</TableHead>
                      <TableHead className="w-44">Físico reportado</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {filas.map((f: ExistenciaLinea) => (
                      <TableRow key={llave(f)} className="hover:bg-surface-subtle">
                        <TableCell>
                          <span className="font-mono text-xs">{f.articuloClave ?? f.articuloId}</span>
                          {f.articuloDescripcion ? ` · ${f.articuloDescripcion}` : ''}
                        </TableCell>
                        <TableCell className="whitespace-nowrap">{f.medida ?? '—'}</TableCell>
                        <TableCell className="font-mono text-xs">{f.lote ?? '—'}</TableCell>
                        <TableCell className="text-right tabular-nums">{num(f.teorico)}</TableCell>
                        <TableCell>{f.unidad}</TableCell>
                        <TableCell>
                          <DecimalField
                            value={fisico[llave(f)] ?? null}
                            onChange={(v) => setFisico((p) => ({ ...p, [llave(f)]: v }))}
                            min={0}
                            step={f.productoAwVarianteId ? 1 : 0.0001}
                            disabled={!puedeCorrer}
                            inputProps={{ 'aria-label': `Físico de ${f.articuloClave ?? ''} ${f.medida ?? ''} ${f.lote ?? ''}` }}
                          />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            </div>
          )}
        </TabsContent>

        <TabsContent value="aclarar" className="pt-4">
          <TablaDiferencias
            items={pendientes}
            cargando={diferencias.isLoading}
            error={diferencias.isError ? diferencias.error : null}
            vacio="No hay diferencias por aclarar."
            onAclarar={puedeCorrer ? setAclarando : undefined}
          />
        </TabsContent>

        <TabsContent value="historico" className="pt-4">
          <TablaDiferencias
            items={diferencias.data ?? []}
            cargando={diferencias.isLoading}
            error={diferencias.isError ? diferencias.error : null}
            vacio="Aún no hay diferencias registradas."
          />
        </TabsContent>

        <TabsContent value="corridas" className="pt-4">
          {corridas.isLoading ? (
            <TableSkeleton rows={4} columns={[{ width: 'w-24' }, { width: 'w-32' }, { width: 'w-20' }]} />
          ) : (corridas.data ?? []).length === 0 ? (
            <EmptyState title="Sin corridas" description="Aún no se ha conciliado ningún día." />
          ) : (
            <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
              <TableContainer>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Día</TableHead>
                      <TableHead>Almacén</TableHead>
                      <TableHead>Físico de</TableHead>
                      <TableHead className="text-right">Partidas comparadas</TableHead>
                      <TableHead className="text-right">Diferencias</TableHead>
                      <TableHead>Corrida</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {(corridas.data ?? []).map((c) => (
                      <TableRow key={c.id} className="hover:bg-surface-subtle">
                        <TableCell className="whitespace-nowrap">
                          <DateTimeDisplay value={c.fecha} variant="corta" />
                        </TableCell>
                        <TableCell className="font-mono text-xs">{c.almacenClave ?? c.almacenId}</TableCell>
                        <TableCell>{c.fuente === 'AW' ? 'A+W' : 'Conteo físico'}</TableCell>
                        <TableCell className="text-right tabular-nums">{c.lineasComparadas}</TableCell>
                        <TableCell className="text-right">
                          <Badge variant={c.diferencias === 0 ? 'success' : 'warning'}>
                            {c.diferencias === 0 ? 'Sin diferencias' : c.diferencias}
                          </Badge>
                        </TableCell>
                        <TableCell className="whitespace-nowrap">
                          <DateTimeDisplay value={c.ejecutadaAt} variant="corta" />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            </div>
          )}
        </TabsContent>
      </Tabs>

      <ConfirmarAccionDialog
        open={confirmarPendientes !== null}
        onOpenChange={(abierto) => !abierto && setConfirmarPendientes(null)}
        titulo="Hay eventos sin aplicar"
        resumen={`${confirmarPendientes ?? ''} Si concilias ahora, el teórico no los incluye y pueden salir diferencias que no lo son. ¿Conciliar de todos modos?`}
        verbo="Conciliar de todos modos"
        tono="primario"
        pendiente={ejecutar.isPending}
        onConfirmar={() => conciliar(true)}
      />

      <ConfirmarAccionDialog
        open={aclarando !== null}
        onOpenChange={(abierto) => {
          if (!abierto) {
            setAclarando(null);
            setMotivo(null);
          }
        }}
        titulo="Aclarar con la mesa de corte"
        resumen={
          <div className="space-y-2">
            <p>
              {aclarando
                ? `${aclarando.articuloClave ?? ''} ${aclarando.medida ?? ''} ${aclarando.lote ?? ''}: ${num(aclarando.diferencia)} ${aclarando.unidad}. `
                : ''}
              Anota qué pasó. La salida ya bajó la existencia: aclararla no descuenta otra vez.
            </p>
            <TextAreaField value={motivo} onChange={setMotivo} maxLength={500} minRows={2} />
          </div>
        }
        verbo="Guardar aclaración"
        tono="primario"
        pendiente={aclarar.isPending}
        onConfirmar={guardarAclaracion}
      />
    </div>
  );
}

/** M-225: antes de conciliar, lo del día que aún no cuadra con el teórico (app sin aplicar) y los consumos/mermas de mesa. */
function EventosDelDiaPanel({ eventos }: { eventos: EventosDelDia }) {
  return (
    <section aria-labelledby="eventos-dia-titulo" className="space-y-2 rounded-lg border border-line-row bg-surface-card p-3 text-sm">
      <h3 id="eventos-dia-titulo" className="font-semibold">Eventos del día</h3>
      {eventos.pendientes.length > 0 && (
        <AvisoEnLinea tono="advertencia">
          {eventos.pendientes.length} evento(s) de la app sin aplicar:{' '}
          {eventos.pendientes.map((p) => `${p.tipo} ${p.referencia ?? ''} (${p.estado}${p.error ? `: ${p.error}` : ''})`).join('; ')}
        </AvisoEnLinea>
      )}
      {eventos.mesa.length > 0 && (
        <TableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Tipo</TableHead>
                <TableHead>Referencia</TableHead>
                <TableHead>Mesa</TableHead>
                <TableHead className="text-right">Cantidad</TableHead>
                <TableHead>Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {eventos.mesa.map((m, i) => (
                <TableRow key={`${m.referencia}-${i}`}>
                  <TableCell>{m.tipo}</TableCell>
                  <TableCell className="font-mono text-xs">{m.referencia ?? '—'}</TableCell>
                  <TableCell>{m.mesa ?? '—'}</TableCell>
                  <TableCell className="text-right tabular-nums">{num(m.cantidad)}</TableCell>
                  <TableCell>{m.estado}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </section>
  );
}

function TablaDiferencias(props: {
  items: DiferenciaConciliacion[];
  cargando: boolean;
  error: unknown;
  vacio: string;
  onAclarar?: (d: DiferenciaConciliacion) => void;
}) {
  if (props.error) return <ErrorState title="No se pudieron cargar las diferencias" />;
  if (props.cargando) return <TableSkeleton rows={4} columns={[{ width: 'w-24' }, { width: 'w-48' }, { width: 'w-20' }]} />;
  if (props.items.length === 0) return <EmptyState title="Sin diferencias" description={props.vacio} />;
  return (
    <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
      <TableContainer>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Día</TableHead>
              <TableHead>Artículo</TableHead>
              <TableHead>Medida</TableHead>
              <TableHead>Lote</TableHead>
              <TableHead className="text-right">ERP</TableHead>
              <TableHead className="text-right">Físico</TableHead>
              <TableHead className="text-right">Diferencia</TableHead>
              <TableHead className="text-right">Valor</TableHead>
              <TableHead>Salida</TableHead>
              <TableHead>Estado</TableHead>
              {props.onAclarar && <TableHead>Acciones</TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {props.items.map((d) => (
              <TableRow key={d.id} className="hover:bg-surface-subtle">
                <TableCell className="whitespace-nowrap">
                  <DateTimeDisplay value={d.fecha} variant="corta" />
                </TableCell>
                <TableCell>
                  <span className="font-mono text-xs">{d.articuloClave ?? d.articuloId}</span>
                  {d.articuloDescripcion ? ` · ${d.articuloDescripcion}` : ''}
                </TableCell>
                <TableCell className="whitespace-nowrap">{d.medida ?? '—'}</TableCell>
                <TableCell className="font-mono text-xs">{d.lote ?? '—'}</TableCell>
                <TableCell className="text-right tabular-nums">{num(d.teorico)}</TableCell>
                <TableCell className="text-right tabular-nums">{num(d.fisico)}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {d.diferencia > 0 ? '+' : ''}
                  {num(d.diferencia)} {d.unidad}
                </TableCell>
                <TableCell className="text-right">
                  {d.sentido === 'FALTANTE' ? <MoneyDisplay amount={d.valorMxn} currency="MXN" /> : '—'}
                </TableCell>
                <TableCell className="whitespace-nowrap">
                  {d.folioMovimiento ? (
                    <span className="font-mono text-xs">{d.folioMovimiento}</span>
                  ) : (
                    <span className="text-xs text-ink-muted">Sobrante: sin salida</span>
                  )}
                </TableCell>
                <TableCell>
                  <Badge variant={d.estado === 'PENDIENTE' ? 'warning' : 'success'}>
                    {d.estado === 'PENDIENTE' ? 'Por aclarar' : 'Aclarada'}
                  </Badge>
                  {d.motivoAclaracion && <div className="text-xs text-ink-muted">{d.motivoAclaracion}</div>}
                </TableCell>
                {props.onAclarar && (
                  <TableCell>
                    <Button size="sm" onClick={() => props.onAclarar?.(d)}>
                      Aclarar
                    </Button>
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </div>
  );
}
