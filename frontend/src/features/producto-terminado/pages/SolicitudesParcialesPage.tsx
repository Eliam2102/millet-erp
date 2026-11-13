import { useTamanoPagina } from '@/components/erp/data/useTamanoPagina';
import { useState, type FormEvent } from 'react';
import { toast } from 'sonner';
import { Breadcrumbs, EmptyState, ErrorState, PaginacionTabla, TableSkeleton,
} from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle,
} from '@/components/ui/sheet';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Textarea } from '@/components/ui/textarea';
import { useSucursales } from '@/features/catalogos/api';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useResolverSalidaParcial, useSolicitudesParciales } from '../api/hooks';
import type { EstadoSolicitudParcial, SolicitudParcialDto } from '../api/types';
import { mensajeApi } from '../lib/errores';
import { useNombreUsuario } from '../lib/usuarios';
import { TramoDetenidoBanner } from './TramoDetenidoBanner';
const selectClass = 'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm';
const tabs: { valor: EstadoSolicitudParcial | ''; label: string }[] = [
  { valor: 'Pendiente', label: 'Pendientes' },
  { valor: 'Autorizada', label: 'Autorizadas' },
  { valor: 'Rechazada', label: 'Rechazadas' },
  { valor: '', label: 'Todas' },
];
const estadoUi: Record<EstadoSolicitudParcial, { texto: string; tono: 'warning' | 'success' | 'danger' }> = {
  Pendiente: { texto: 'Pendiente', tono: 'warning' },
  Autorizada: { texto: 'Autorizada', tono: 'success' },
  Rechazada: { texto: 'Rechazada', tono: 'danger' },
};
const nivelTexto = { credito: 'Crédito y Cobranza', ventas: 'Gerencia de Ventas (suplencia)',
} as const;

/** Bandeja de solicitudes de salida parcial (M-245): Crédito y Cobranza autoriza o rechaza; Gerencia de Ventas solo como suplente y con su motivo. */
export function SolicitudesParcialesPage() {
  const { limit: limiteTabla } = useTamanoPagina();
  const [estado, setEstado] = useState<EstadoSolicitudParcial | ''>('Pendiente');
  const [pedido, setPedido] = useState('');
  const [offset, setOffset] = useState(0);
  const [resolviendo, setResolviendo] = useState<{ s: SolicitudParcialDto; autorizar: boolean;
  } | null>(null);
  const puedeCredito = useHasPermission(P.ProductoTerminadoSalidaAprobarCredito);
  const puedeVentas = useHasPermission(P.ProductoTerminadoSalidaAprobarVentas);
  const puedeResolver = puedeCredito || puedeVentas;
  const nombreUsuario = useNombreUsuario();
  const sucursales = useSucursales();
  const nombreSucursal = (id: string) => sucursales.data?.items.find((s) => s.id === id)?.nombre ?? 'Sin sucursal';
  const q = useSolicitudesParciales({ estado: estado || undefined, pedido: pedido.trim() || undefined, page: offset / limiteTabla + 1,
    pageSize: limiteTabla,
  });
  const items = q.data?.items ?? [];

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Producto terminado' }, { label: 'Solicitudes de salida parcial' }]} />
      <PageHeader
        titulo="Solicitudes de salida parcial"
        descripcion="Pedidos que piden salir sin estar completos en producto terminado. Crédito y Cobranza autoriza; Gerencia de Ventas solo cuando Crédito no puede, con el motivo de la suplencia."
      />
      <TramoDetenidoBanner />
      {q.isError ? (
        <ErrorState title="No se pudieron cargar las solicitudes" onRetry={() => void q.refetch()} />
      ) : (
        <div className="flex min-h-80 flex-1 flex-col">
          <ListadoModulo
            tabs={
              <Tabs value={estado || 'todas'} onValueChange={(v) => { setEstado(v === 'todas' ? '' : (v as EstadoSolicitudParcial)); setOffset(0); }}>
                <TabsList aria-label="Estado de la solicitud" className="gap-3 px-1">
                  {tabs.map((t) => (
                    <TabsTrigger key={t.label} value={t.valor || 'todas'}>{t.label}</TabsTrigger>
                  ))}
                </TabsList>
              </Tabs>
            }
            filtros={
              <div className="flex flex-wrap items-center gap-3">
                <Input className="w-56" aria-label="Pedido" placeholder="Número de pedido" value={pedido} onChange={(e) => { setPedido(e.target.value); setOffset(0); }} />
                {q.data && (
                  <span className="text-xs text-ink-muted">{q.data.total} solicitudes</span>
                )}
              </div>
            }
            pie={<PaginacionTabla total={q.data?.total ?? 0} offset={offset} limit={limiteTabla} onChange={setOffset} />}
          >
            {q.isLoading ? (
              <TableSkeleton />
            ) : items.length === 0 ? (
              <EmptyState title="No hay solicitudes para esos filtros" />
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Pedido</TableHead>
                    <TableHead>Sucursal</TableHead>
                    <TableHead>Piezas</TableHead>
                    <TableHead>Motivo</TableHead>
                    <TableHead>Solicitó</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead>Resolución</TableHead>
                    <TableHead>Acciones</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((s) => (
                    <TableRow key={s.id}>
                      <TableCell className="font-mono text-xs">{s.numeroPedido}</TableCell>
                      <TableCell>{nombreSucursal(s.sucursalId)}</TableCell>
                      <TableCell className="tabular-nums" title={s.etiquetas.join(', ')}>{s.etiquetas.length} de {s.esperadas} · {s.enPt} en PT</TableCell>
                      <TableCell className="max-w-72 whitespace-normal break-words">{s.motivo}</TableCell>
                      <TableCell>
                        {nombreUsuario(s.solicitadaPorId)}
                        <p className="text-xs text-ink-muted tabular-nums"><DateTimeDisplay value={s.solicitadaAt} variant="datetime" /></p>
                      </TableCell>
                      <TableCell><Badge variant={estadoUi[s.estado].tono}>{estadoUi[s.estado].texto}</Badge></TableCell>
                      <TableCell className="max-w-72 whitespace-normal break-words text-xs">
                        {s.estado === 'Pendiente' ? (
                          <span className="text-ink-muted">—</span>
                        ) : (
                          <>
                            {nombreUsuario(s.resueltaPorId)}{s.nivel ? ` · ${nivelTexto[s.nivel]}` : ''}
                            {s.resueltaAt && (
                              <>
                                {' '}
                                · <DateTimeDisplay value={s.resueltaAt} variant="datetime" />
                              </>
                            )}
                            {s.motivoResolucion && <p>{s.motivoResolucion}</p>}
                            {s.suplencia && (
                              <p className="text-ink-muted">Suplencia: {s.suplencia}</p>
                            )}
                          </>
                        )}
                      </TableCell>
                      <TableCell>
                        {s.estado === 'Pendiente' && puedeResolver ? (
                          <div className="flex flex-wrap gap-2">
                            <Button
                              size="sm"
                              onClick={() => setResolviendo({ s, autorizar: true })}
                            >
                              Autorizar
                            </Button>
                            <Button
                              size="sm"
                              variant="outline"
                              onClick={() => setResolviendo({ s, autorizar: false })}
                            >
                              Rechazar
                            </Button>
                          </div>
                        ) : s.estado === 'Pendiente' ? (
                          <span className="text-xs text-ink-muted">Sin permiso para resolver</span>
                        ) : null}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </ListadoModulo>
        </div>
      )}
      {resolviendo && (
        <ResolverSheet
          {...resolviendo}
          puedeCredito={puedeCredito}
          puedeVentas={puedeVentas}
          onCerrar={() => setResolviendo(null)}
        />
      )}
    </main>
  );
}

function ResolverSheet({
  s,
  autorizar,
  puedeCredito,
  puedeVentas,
  onCerrar,
}: {
  s: SolicitudParcialDto;
  autorizar: boolean;
  puedeCredito: boolean;
  puedeVentas: boolean;
  onCerrar: () => void;
}) {
  const resolver = useResolverSalidaParcial();
  const niveles = (['credito', 'ventas'] as const).filter((n) =>
    n === 'credito' ? puedeCredito : puedeVentas,
  );
  const [nivel, setNivel] = useState<'credito' | 'ventas' | ''>(
    niveles.length === 1 ? niveles[0] : '',
  );
  const [suplencia, setSuplencia] = useState('');
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const lm = motivo.trim().length;
  const ls = suplencia.trim().length;
  const valido =
    lm >= 5 &&
    lm <= 500 &&
    (!autorizar || (nivel !== '' && (nivel !== 'ventas' || (ls >= 5 && ls <= 500))));

  const enviar = (e: FormEvent) => {
    e.preventDefault();
    if (!valido) return;
    setError(null);
    resolver
      .mutateAsync({
        id: s.id,
        body: {
          autorizar,
          motivo: motivo.trim(),
          ...(autorizar && nivel ? { nivel } : {}),
          ...(autorizar && nivel === 'ventas' ? { suplencia: suplencia.trim() } : {}),
        },
      })
      .then(() => {
        toast.success(autorizar ? 'Salida parcial autorizada' : 'Salida parcial rechazada');
        onCerrar();
      })
      .catch((err) => setError(mensajeApi(err)));
  };

  return (
    <Sheet open onOpenChange={(o) => !o && !resolver.isPending && onCerrar()}>
      <SheetContent>
        <form onSubmit={enviar} className="flex min-h-0 flex-1 flex-col">
          <SheetHeader>
            <SheetTitle>
              {autorizar ? 'Autorizar salida parcial' : 'Rechazar salida parcial'}
            </SheetTitle>
            <SheetDescription>
              Pedido {s.numeroPedido} · {s.etiquetas.length} piezas. Solicitud: {s.motivo}
            </SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col gap-4">
            {autorizar && (
              <div className="flex flex-col gap-1">
                <Label htmlFor="pt-parcial-nivel">Nivel de autorización</Label>
                <select
                  id="pt-parcial-nivel"
                  className={selectClass}
                  required
                  value={nivel}
                  onChange={(e) => setNivel(e.target.value as 'credito' | 'ventas' | '')}
                >
                  <option value="">Selecciona un nivel</option>
                  {niveles.map((n) => (
                    <option key={n} value={n}>
                      {n === 'credito' ? 'Crédito y Cobranza' : 'Gerencia de Ventas (suplente)'}
                    </option>
                  ))}
                </select>
              </div>
            )}
            {autorizar && nivel === 'ventas' && (
              <div className="flex flex-col gap-1">
                <Label htmlFor="pt-parcial-suplencia">
                  Por qué Crédito y Cobranza no pudo autorizar
                </Label>
                <Input
                  id="pt-parcial-suplencia"
                  required
                  minLength={5}
                  maxLength={500}
                  value={suplencia}
                  onChange={(e) => setSuplencia(e.target.value)}
                />
              </div>
            )}
            <div className="flex flex-col gap-1">
              <Label htmlFor="pt-parcial-resolucion">
                {autorizar ? 'Motivo de la autorización' : 'Motivo del rechazo'}
              </Label>
              <Textarea
                id="pt-parcial-resolucion"
                rows={3}
                maxLength={500}
                aria-invalid={lm > 0 && lm < 5}
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
              />
              <p className="text-xs text-ink-muted">Entre 5 y 500 caracteres ({lm}).</p>
            </div>
            {error && (
              <AvisoEnLinea tono="error" titulo="No se registró la resolución">
                {error}
              </AvisoEnLinea>
            )}
          </SheetBody>
          <SheetFooter>
            <Button type="button" variant="outline" onClick={onCerrar}>
              Cancelar
            </Button>
            <Button type="submit" disabled={!valido || resolver.isPending}>
              {autorizar ? 'Autorizar' : 'Rechazar'}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
