import { useState } from 'react';
import { toast } from 'sonner';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { TextAreaField } from '@/components/erp';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useFormIdempotencyKey, esApiError } from '@/lib/api';
import { errorUsuario } from '@/lib/error-usuario';
import { formatCantidad } from '@/lib/format-cantidad';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { Field } from '@/features/almacen/components/internal/Field';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import { TipoSubAlmacen, EstadoMovimiento, type SalidaDetalle } from '@/features/almacen/api/types';
import {
  useCorreccionesSalida,
  useDecidirCorreccion,
  useSolicitarCorreccion,
  useTiposCorreccion,
  type CorreccionConsumo,
  type EstadoCorreccion,
} from '@/features/almacen/api/useCorreccionesConsumo';

const ESTADO_UI: Record<EstadoCorreccion, { texto: string; variante: BadgeProps['variant'] }> = {
  PENDIENTE: { texto: 'Pendiente de Costos', variante: 'warning' },
  AUTORIZADA: { texto: 'Autorizada', variante: 'success' },
  RECHAZADA: { texto: 'Rechazada', variante: 'destructive' },
};

const num = (n: number) => formatCantidad(n, 4);

/**
 * M-334 / M-331 — Correcciones de una salida por consumo: consumido, corregido y neto por partida, las correcciones
 * pedidas y, para Costos, autorizar o rechazar. La corrección autorizada entra en el mes corriente ligada a la salida.
 */
export function CorreccionesConsumoSeccion({ salida }: { salida: SalidaDetalle }) {
  const query = useCorreccionesSalida(salida.id);
  const puedeSolicitar = useHasPermission(PermisosCanonicos.AlmacenDevolucionesInternasCapturar);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.AlmacenCorreccionesConsumoAutorizar);
  const decidir = useDecidirCorreccion();
  const [solicitarOpen, setSolicitarOpen] = useState(false);
  const [autorizando, setAutorizando] = useState<CorreccionConsumo | null>(null);
  const [rechazando, setRechazando] = useState<CorreccionConsumo | null>(null);
  const [motivoRechazo, setMotivoRechazo] = useState<string | null>(null);

  const datos = query.data;
  const hayPendiente = datos?.correcciones.some((c) => c.estado === 'PENDIENTE') ?? false;
  const registrada = salida.estado === EstadoMovimiento.Registrado;

  function resolver(accion: 'autorizar' | 'rechazar', c: CorreccionConsumo) {
    decidir.mutate(
      { id: c.id, accion, motivo: motivoRechazo ?? undefined },
      {
        onSuccess: (r) => {
          toast.success(
            accion === 'autorizar'
              ? `Corrección autorizada: se registró ${r.folioDevolucion ?? 'la devolución'} en el mes corriente`
              : 'Corrección rechazada',
          );
          setAutorizando(null);
          setRechazando(null);
          setMotivoRechazo(null);
        },
        onError: (e) => toast.error(errorUsuario(e)),
      },
    );
  }

  if (!datos) return null;
  return (
    <section className="space-y-2" aria-labelledby="correcciones-consumo">
      <div className="flex items-center justify-between gap-2">
        <h2 id="correcciones-consumo" className="text-sm font-semibold uppercase tracking-wide text-ink-muted">
          Correcciones de consumo
        </h2>
        {puedeSolicitar && registrada && (
          <Button size="sm" variant="outline" disabled={hayPendiente || datos.neto <= 0} onClick={() => setSolicitarOpen(true)}>
            Solicitar corrección
          </Button>
        )}
      </div>
      <p className="text-xs text-ink-muted">
        La autoriza Costos. Se registra en el mes corriente, al costo con que salió, aunque la salida sea de un mes cerrado.
      </p>

      <div className="overflow-x-auto rounded-md border">
        <Table className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50">
            <TableRow>
              <TableHead className="px-3 py-2 text-left">Artículo</TableHead>
              <TableHead className="px-3 py-2 text-right">Consumido</TableHead>
              <TableHead className="px-3 py-2 text-right">Corregido</TableHead>
              <TableHead className="px-3 py-2 text-right">Neto</TableHead>
              <TableHead className="px-3 py-2 text-right">Por autorizar</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {datos.lineas.map((l) => (
              <TableRow key={l.lineaSalidaId} className="border-t">
                <TableCell className="px-3 py-2">
                  {l.articuloClave && <span className="font-mono text-xs text-ink-muted">{l.articuloClave} </span>}
                  {l.articuloDescripcion ?? ''}
                </TableCell>
                <TableCell className="px-3 py-2 text-right tabular-nums">{num(l.consumido)} {l.unidadMedida}</TableCell>
                <TableCell className="px-3 py-2 text-right tabular-nums">{num(l.corregido)}</TableCell>
                <TableCell className="px-3 py-2 text-right font-semibold tabular-nums">{num(l.neto)}</TableCell>
                <TableCell className="px-3 py-2 text-right tabular-nums">{num(l.pendienteDeAutorizar)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {datos.correcciones.length > 0 && (
        <ul className="space-y-2">
          {datos.correcciones.map((c) => (
            <li key={c.id} className="rounded-md border p-3 text-sm">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant={ESTADO_UI[c.estado].variante}>{ESTADO_UI[c.estado].texto}</Badge>
                  <span className="font-medium">{c.tipoTexto}</span>
                  <span className="tabular-nums text-ink-muted">· {num(c.cantidad)}</span>
                  {c.folioDevolucion && <span className="font-mono text-xs">{c.folioDevolucion}</span>}
                </div>
                {c.estado === 'PENDIENTE' && puedeAutorizar && (
                  <div className="flex gap-2">
                    <Button size="sm" variant="outline" onClick={() => setRechazando(c)}>
                      Rechazar
                    </Button>
                    <Button size="sm" onClick={() => setAutorizando(c)}>
                      Autorizar
                    </Button>
                  </div>
                )}
              </div>
              <p className="mt-1">{c.motivo}</p>
              <p className="mt-1 text-xs text-ink-muted">
                Solicitó {c.solicitadoPor} · <DateTimeDisplay value={c.solicitadoAt} />
                {c.decididoPor && (
                  <>
                    {' '}· {c.estado === 'RECHAZADA' ? 'Rechazó' : 'Autorizó'} {c.decididoPor}
                  </>
                )}
              </p>
              {c.motivoRechazo && <p className="mt-1 text-xs text-ink-muted">Motivo del rechazo: {c.motivoRechazo}</p>}
            </li>
          ))}
        </ul>
      )}

      {solicitarOpen && (
        <SolicitarCorreccionSheet salida={salida} saldos={datos.lineas} onClose={() => setSolicitarOpen(false)} />
      )}

      <ConfirmarAccionDialog
        open={autorizando !== null}
        onOpenChange={(abierto) => !abierto && setAutorizando(null)}
        titulo="Autorizar corrección de consumo"
        resumen={
          autorizando
            ? `${autorizando.tipoTexto} de ${num(autorizando.cantidad)} sobre ${autorizando.folioSalida}. Se registra hoy, en el mes corriente, al costo con que salió.`
            : ''
        }
        verbo="Autorizar corrección"
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
        titulo="Rechazar corrección de consumo"
        resumen={
          <div className="space-y-2">
            <p>La corrección queda rechazada y el inventario no cambia. Indica el motivo.</p>
            <TextAreaField value={motivoRechazo} onChange={setMotivoRechazo} maxLength={500} minRows={2} />
          </div>
        }
        verbo="Rechazar corrección"
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
    </section>
  );
}

function SolicitarCorreccionSheet({
  salida,
  saldos,
  onClose,
}: {
  salida: SalidaDetalle;
  saldos: { lineaSalidaId: string; neto: number; unidadMedida: string }[];
  onClose: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const solicitar = useSolicitarCorreccion();
  const tipos = useTiposCorreccion();
  const subs = useSubAlmacenes({ limit: 500 });
  const [tipo, setTipo] = useState('ERROR_CAPTURA');
  const [motivo, setMotivo] = useState<string | null>(null);
  const [subAlmacenId, setSubAlmacenId] = useState(salida.subAlmacenId);
  const [partidas, setPartidas] = useState<Record<string, { cantidad: string; ubicacionId: string | null }>>(() =>
    Object.fromEntries(
      salida.lineas.map((l) => [l.id, { cantidad: '', ubicacionId: null }]),
    ),
  );
  const danado = tipo === 'DEVOLUCION_DANADA';
  const opcionesSub = (subs.data?.items ?? []).filter((s) =>
    danado ? s.tipo === TipoSubAlmacen.MaterialEnRevision : s.tipo !== TipoSubAlmacen.MaterialEnRevision,
  );

  function enviar() {
    const lineas = salida.lineas
      .map((l) => ({ l, p: partidas[l.id] }))
      .filter(({ p }) => p && Number(p.cantidad) > 0)
      .map(({ l, p }) => ({ lineaSalidaOrigenId: l.id, cantidad: Number(p.cantidad), ubicacionId: p.ubicacionId ?? '' }));
    if (!motivo?.trim()) return toast.error('Indica el motivo de la corrección.');
    if (lineas.length === 0) return toast.error('Captura la cantidad a corregir en al menos una partida.');
    if (lineas.some((x) => !x.ubicacionId)) return toast.error('Elige la ubicación a donde regresa cada partida.');
    solicitar.mutate(
      {
        command: { salidaOrigenId: salida.id, subAlmacenDestinoId: subAlmacenId, tipo, motivo: motivo.trim(), observaciones: null, lineas },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Corrección solicitada: queda pendiente de que Costos la autorice');
          onClose();
        },
        onError: (e) => toast.error(esApiError(e) ? e.problem.detail ?? e.problem.title : errorUsuario(e)),
      },
    );
  }

  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Solicitar corrección de {salida.folio}</SheetTitle>
          <SheetDescription>
            No mueve el inventario hasta que Costos la autorice. No puede pasar de lo consumido neto.
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          <div className="space-y-4">
            <Field label="Tipo de corrección" required>
              <Select
                value={tipo}
                onValueChange={(v) => {
                  setTipo(v);
                  setSubAlmacenId(v === 'DEVOLUCION_DANADA' ? '' : salida.subAlmacenId);
                }}
              >
                <SelectTrigger aria-label="Tipo de corrección">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(tipos.data ?? []).map((t) => (
                    <SelectItem key={t.codigo} value={t.codigo}>
                      {t.texto}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Regresa al sub-almacén" required>
              <Select value={subAlmacenId} onValueChange={setSubAlmacenId}>
                <SelectTrigger aria-label="Sub-almacén destino">
                  <SelectValue placeholder={danado ? 'Material en revisión' : 'Elige el sub-almacén'} />
                </SelectTrigger>
                <SelectContent>
                  {opcionesSub.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.clave} · {s.nombre}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Motivo" required>
              <TextAreaField value={motivo} onChange={setMotivo} maxLength={500} minRows={2} />
            </Field>
            {salida.lineas.map((l) => {
              const saldo = saldos.find((s) => s.lineaSalidaId === l.id);
              const p = partidas[l.id] ?? { cantidad: '', ubicacionId: null };
              return (
                <div key={l.id} className="space-y-2 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3">
                  <div className="text-sm">
                    <span className="font-mono text-xs text-ink-muted">{l.articuloClave} </span>
                    {l.articuloDescripcion} · neto {num(saldo?.neto ?? 0)} {l.unidadMedida}
                  </div>
                  <div className="grid gap-2 sm:grid-cols-2">
                    <Field label="Cantidad a corregir">
                      <Input
                        type="number"
                        inputMode="decimal"
                        min="0"
                        step="0.0001"
                        aria-label={`Cantidad a corregir de ${l.articuloClave ?? 'la partida'}`}
                        value={p.cantidad}
                        onChange={(e) => setPartidas({ ...partidas, [l.id]: { ...p, cantidad: e.target.value } })}
                      />
                    </Field>
                    <Field label="Ubicación (rack)">
                      <UbicacionBinSelector
                        modo="asignacion"
                        articuloId={l.articuloId}
                        subAlmacenId={subAlmacenId || null}
                        value={p.ubicacionId}
                        onChange={(id) => setPartidas({ ...partidas, [l.id]: { ...p, ubicacionId: id } })}
                      />
                    </Field>
                  </div>
                </div>
              );
            })}
          </div>
        </SheetBody>
        <SheetFooter>
          <Button variant="ghost" onClick={onClose} disabled={solicitar.isPending}>
            Cancelar
          </Button>
          <Button onClick={enviar} disabled={solicitar.isPending || !subAlmacenId}>
            {solicitar.isPending ? 'Enviando…' : 'Solicitar corrección'}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
