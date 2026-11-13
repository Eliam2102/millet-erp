import { useTamanoPagina } from '@/components/erp/data/useTamanoPagina';
import { useState, type FormEvent } from 'react';
import { Camera } from 'lucide-react';
import { toast } from 'sonner';
import { Breadcrumbs, EmptyState, ErrorState, PaginacionTabla, TableSkeleton,
} from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle,
} from '@/components/ui/sheet';
import { Textarea } from '@/components/ui/textarea';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';
import { useAccionCaso, useCasos } from '../api/hooks';
import type { CasoDanoDto, EstadoCaso } from '../api/types';
import { mensajeApi } from '../lib/errores';
import { reducirFoto } from '../lib/imagen';
import { estadoCasoUi, etapaTexto } from '../lib/casos-ui';
import { CasoDetalle } from './CasoDetalle';
import { TramoDetenidoBanner } from './TramoDetenidoBanner';

const selectClass = 'h-ctl-xl rounded-md border border-line-control bg-surface-card px-3 text-sm';

/** Casos de daño, rechazo o devolución (F1-PT-06, M-351, M-352): lista a la izquierda y detalle a la derecha (patrón P3); en móvil se apilan. */
export function CasosDanoPage() {
  const { limit: limiteTabla } = useTamanoPagina();
  const operar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const [estado, setEstado] = useState<EstadoCaso | ''>('');
  const [pedido, setPedido] = useState('');
  const [offset, setOffset] = useState(0);
  const [elegido, setElegido] = useState<string | null>(null);
  const [registrando, setRegistrando] = useState(false);
  const q = useCasos({ estado: estado || undefined, pedido: pedido.trim() || undefined, page: offset / limiteTabla + 1,
    pageSize: limiteTabla,
  });
  const items = q.data?.items ?? [];
  const activo = items.find((c) => c.id === elegido) ?? null;

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Producto terminado' }, { label: 'Casos de daño' }]} />
      <PageHeader
        titulo="Casos de daño"
        descripcion="Registra el daño, el rechazo en sitio o la devolución de piezas; Calidad dictamina y, si ya hay factura, se corrige con nota de crédito o cancelación."
        acciones={operar ? <Button onClick={() => setRegistrando(true)}>Registrar caso</Button> : undefined}
      />
      <TramoDetenidoBanner />
      <div className="grid min-h-0 items-start gap-4 lg:grid-cols-[320px_minmax(0,1fr)]">
        <aside aria-label="Lista de casos" className="flex flex-col gap-3 rounded-lg bg-surface-card p-3 shadow-card-flat">
          <div className="flex flex-wrap gap-2">
            <Input className="min-w-0 flex-1" aria-label="Pedido" placeholder="Número de pedido" value={pedido} onChange={(e) => { setPedido(e.target.value); setOffset(0); }} />
            <select aria-label="Estado del caso" className={selectClass} value={estado} onChange={(e) => { setEstado(e.target.value as EstadoCaso | ''); setOffset(0); }}>
              <option value="">Todos</option>
              <option value="Abierto">Abiertos</option>
              <option value="Dictaminado">Dictaminados</option>
              <option value="Cerrado">Cerrados</option>
            </select>
          </div>
          {q.isError ? (
            <ErrorState title="No se pudieron cargar los casos" onRetry={() => void q.refetch()} />
          ) : q.isLoading ? (
            <TableSkeleton rows={6} columns={[{ width: 'w-full' }, { width: 'w-full' }]} />
          ) : items.length === 0 ? (
            <EmptyState title="No hay casos para esos filtros" />
          ) : (
            <ul className="divide-y divide-line-row">
              {items.map((c) => (
                <li key={c.id}>
                  <button
                    type="button"
                    aria-current={c.id === elegido ? 'true' : undefined}
                    onClick={() => setElegido(c.id)}
                    className={cn('flex w-full flex-col gap-1 px-2 py-2 text-left hover:bg-surface-subtle', c.id === elegido && 'border-l-[3px] border-brand bg-surface-selected',
                    )}
                  >
                    <span className="flex items-center justify-between gap-2">
                      <span className="truncate font-mono text-xs">{c.folio}</span>
                      <Badge variant={estadoCasoUi[c.estado].tono}>{estadoCasoUi[c.estado].texto}</Badge>
                    </span>
                    <span className="text-xs text-ink-muted">Pedido {c.numeroPedido} · {etapaTexto[c.etapa]}</span>
                    <span className="truncate text-xs">{c.motivo}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <PaginacionTabla total={q.data?.total ?? 0} offset={offset} limit={limiteTabla} onChange={setOffset} />
        </aside>
        <section aria-label="Detalle del caso" className="min-w-0">
          {activo ? (
            <CasoDetalle caso={activo} operar={operar} />
          ) : (
            <EmptyState title="Elige un caso de la lista para ver su detalle" />
          )}
        </section>
      </div>
      {registrando && (
        <RegistrarCasoSheet onCerrar={() => setRegistrando(false)} onListo={(c: CasoDanoDto) => { setRegistrando(false); setElegido(c.id); }} />
      )}
    </main>
  );
}

function RegistrarCasoSheet({ onCerrar, onListo,
}: { onCerrar: () => void; onListo: (c: CasoDanoDto) => void;
}) {
  const accion = useAccionCaso();
  const [etiquetas, setEtiquetas] = useState('');
  const [motivo, setMotivo] = useState('');
  const [foto, setFoto] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const lista = [...new Set(etiquetas.split(/[\n,;]+/).map((l) => l.trim()).filter(Boolean),
    ),
  ];
  const lm = motivo.trim().length;
  const valido = lista.length > 0 && lm >= 5 && lm <= 500;

  const tomarFoto = async (archivo: File | undefined) => {
    if (!archivo) return;
    try { setFoto(await reducirFoto(archivo)); } catch { setError('No se pudo leer la foto. Elige otra imagen JPEG o PNG.'); }
  };
  const enviar = (e: FormEvent) => {
    e.preventDefault();
    if (!valido) return;
    setError(null);
    accion
      .mutateAsync({ tipo: 'registrar', body: { etiquetas: lista, motivo: motivo.trim(), ...(foto ? { evidencia: foto } : {}) },
      })
      .then((c) => { toast.success(`Caso ${c.folio} registrado`);
        onListo(c);
      })
      .catch((err) => setError(mensajeApi(err)));
  };

  return (
    <Sheet open onOpenChange={(o) => !o && !accion.isPending && onCerrar()}>
      <SheetContent>
        <form onSubmit={enviar} className="flex min-h-0 flex-1 flex-col">
          <SheetHeader>
            <SheetTitle>Registrar caso de daño</SheetTitle>
            <SheetDescription>
              Las piezas pasan a revisión hasta que Calidad dictamine. Todas deben ser del mismo
              pedido.
            </SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <Label htmlFor="pt-caso-etiquetas">Etiquetas de las piezas</Label>
              <Textarea
                id="pt-caso-etiquetas"
                rows={5}
                placeholder="Una etiqueta por renglón"
                value={etiquetas}
                onChange={(e) => setEtiquetas(e.target.value)}
              />
              <p className="text-xs text-ink-muted">
                {lista.length} {lista.length === 1 ? 'pieza' : 'piezas'}.
              </p>
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="pt-caso-motivo">Motivo</Label>
              <Textarea
                id="pt-caso-motivo"
                rows={3}
                maxLength={500}
                aria-invalid={lm > 0 && lm < 5}
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
              />
              <p className="text-xs text-ink-muted">Entre 5 y 500 caracteres ({lm}).</p>
            </div>
            <div className="flex flex-col gap-2">
              <span className="text-sm font-medium">Foto del daño (opcional)</span>
              <div className="flex flex-wrap items-center gap-3">
                <label className="inline-flex min-h-9 cursor-pointer items-center gap-2 rounded-md border border-line px-3 text-sm font-medium focus-within:ring-2 focus-within:ring-ring">
                  <Camera className="size-4" aria-hidden="true" />
                  {foto ? 'Cambiar foto' : 'Tomar o elegir foto'}
                  <input
                    type="file"
                    accept="image/jpeg,image/png"
                    capture="environment"
                    className="sr-only"
                    aria-label="Foto del daño"
                    onChange={(e) => void tomarFoto(e.target.files?.[0])}
                  />
                </label>
                {foto && (
                  <img
                    src={foto}
                    alt="Vista previa de la foto del daño"
                    className="h-16 rounded-md border border-line object-cover"
                  />
                )}
              </div>
              <p className="text-xs text-ink-muted">
                JPEG o PNG; se reduce a 1280 px. Puedes agregarla después.
              </p>
            </div>
            {error && (
              <AvisoEnLinea tono="error" titulo="No se registró el caso">
                {error}
              </AvisoEnLinea>
            )}
          </SheetBody>
          <SheetFooter>
            <Button type="button" variant="outline" onClick={onCerrar}>
              Cancelar
            </Button>
            <Button type="submit" disabled={!valido || accion.isPending}>
              Registrar caso
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
