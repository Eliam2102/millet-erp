import { ReporteShell, SucursalSelector, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Textarea } from '@/components/ui/textarea';
import {
  ESTADO_CONTINGENCIA_LABEL,
  TIPO_CONTINGENCIA_LABEL,
  useContingencias,
  useRegistrarContingencia,
  useRegularizarContingencia,
  useReporteContingencias,
  type ContingenciaItem,
  type EstadoContingencia,
  type TipoContingencia,
} from '@/features/facturacion/api/useContingencias';
import {
  useComprobantesConEfectosPendientes,
  useReintentarEfectos,
} from '@/features/facturacion/api/useFacturas';
import { mostrarError } from '@/features/facturacion/lib/mostrar-error-cancelacion';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { formatDate, formatDateTime, parseDateOnlyLocal, parseLocalToUtc } from '@/lib/datetime';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { toast } from 'sonner';

const ESTADOS: ReadonlyArray<EstadoContingencia | undefined> = [
  'Abierta',
  'Regularizada',
  'Descartada',
  undefined,
];

const TIPOS_MANUALES: ReadonlyArray<TipoContingencia> = ['Sat', 'Internet', 'Pac', 'Certificado', 'Otra'];

function mesActual(): string {
  const hoy = new Date();
  return `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}`;
}

/**
 * <c>&lt;BandejaContingencias/&gt;</c> — bitácora de contingencias de timbrado
 * (F6.8). El ERP registra solo las caídas de PAC o de certificado al timbrar; SAT,
 * internet y otras se capturan a mano. Cada una se regulariza a más tardar el día 3
 * del mes siguiente: con el CFDI final al timbrar su documento, o descartada.
 */
/**
 * M-285 · Facturas timbradas con efectos pendientes (póliza, aviso a A+W, NC de ranura): el CFDI ya
 * existe; solo falta aplicar lo que quedó pendiente, sin volver a timbrar.
 */
function TimbradosConEfectosPendientes({ puedeReintentar }: { puedeReintentar: boolean }) {
  const query = useComprobantesConEfectosPendientes();
  const reintentar = useReintentarEfectos();
  const items = query.data ?? [];
  if (items.length === 0) return null;

  return (
    <div data-testid="efectos-pendientes">
      <AvisoEnLinea tono="advertencia" titulo={`${items.length} factura(s) timbrada(s) con efectos pendientes`}>
        <ul className="mt-1 space-y-1">
          {items.map((c) => (
            <li key={c.comprobanteId} className="flex flex-wrap items-center gap-2">
              <Link
                to="/facturacion/facturas/$id"
                params={{ id: c.comprobanteId }}
                className="font-mono text-xs underline"
              >
                {c.folio}
              </Link>
              <span>{c.pendientes.map((e) => e.descripcion).join(', ')}</span>
              {puedeReintentar && (
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  disabled={reintentar.isPending}
                  onClick={() =>
                    reintentar.mutate(
                      { id: c.comprobanteId, idempotencyKey: crypto.randomUUID() },
                      {
                        onSuccess: (r) =>
                          r.some((e) => e.estado === 'Pendiente')
                            ? toast.error(`${c.folio}: siguen efectos pendientes; revisa el motivo en la factura.`)
                            : toast.success(`${c.folio}: efectos aplicados.`),
                        onError: () => toast.error(`${c.folio}: no se pudieron reintentar los efectos.`),
                      },
                    )
                  }
                >
                  Reintentar pendientes
                </Button>
              )}
            </li>
          ))}
        </ul>
      </AvisoEnLinea>
    </div>
  );
}

export function BandejaContingencias() {
  const [periodo, setPeriodo] = useState(mesActual);
  const [estadoIdx, setEstadoIdx] = useState(0);
  const [registrando, setRegistrando] = useState(false);
  const [anio, mes] = periodo.split('-').map(Number) as [number, number];
  const estado = ESTADOS[estadoIdx];
  const query = useContingencias({ anio, mes, estado });
  const puedeRegistrar = useHasPermission(PermisosCanonicos.FacturacionComprobantesReintentarTimbrado);
  const puedeReporte = useHasPermission(PermisosCanonicos.FacturacionReportesLeer);
  const reporte = useReporteContingencias(anio, mes, puedeReporte);
  const data = query.data;
  const items = data?.items ?? [];

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Contingencias de timbrado</>}
        descripcion={
          <>
            Caídas del PAC, el SAT, internet o el certificado. Regulariza cada una a más tardar el
            día 3 del mes siguiente.
          </>
        }
        acciones={
          <Button
            onClick={() => setRegistrando(true)}
            disabled={!puedeRegistrar || registrando}
            title={
              puedeRegistrar
                ? undefined
                : 'Necesitas el permiso para reintentar timbrados para registrar contingencias.'
            }
          >
            Registrar contingencia
          </Button>
        }
      />

      {data && (data.vencidas > 0 || data.porVencer > 0) && (
        <div data-testid="alerta-contingencias">
          <AvisoEnLinea
            tono={data.vencidas > 0 ? 'error' : 'advertencia'}
            titulo={
              data.vencidas > 0
                ? `${data.vencidas} contingencia(s) vencida(s) sin regularizar`
                : `${data.porVencer} contingencia(s) por regularizar`
            }
          >
            Su mes ya cerró. Timbra o descarta el documento afectado, o regulariza a mano las que no
            tienen documento.
            {data.vencidas > 0 && data.porVencer > 0 && ` Además, ${data.porVencer} vencen el día 3.`}
          </AvisoEnLinea>
        </div>
      )}

      <TimbradosConEfectosPendientes puedeReintentar={puedeRegistrar} />

      {registrando && <FormRegistrar onClose={() => setRegistrando(false)} />}

      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <Label htmlFor="periodo-contingencias" className="text-xs text-ink-muted">
            Mes del evento
          </Label>
          <Input
            id="periodo-contingencias"
            type="month"
            value={periodo}
            onChange={(e) => e.target.value && setPeriodo(e.target.value)}
            className="w-44"
          />
        </div>
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={ESTADOS.map((e, i) => ({
              valor: i + 1,
              label: e ? ESTADO_CONTINGENCIA_LABEL[e] : 'Todas',
            }))}
            valor={estadoIdx + 1}
            conteos={undefined}
            onChange={(v) => setEstadoIdx((v ?? 1) - 1)}
          />
        }
      >
        <div className="min-h-0 flex-1 overflow-auto">
          {query.isError ? (
            <div role="alert" className="p-4">
              No se pudo cargar la bitácora.{' '}
              <Button variant="secondary" onClick={() => void query.refetch()}>
                Reintentar
              </Button>
            </div>
          ) : query.isLoading ? (
            <p role="status" className="p-4">
              Cargando contingencias…
            </p>
          ) : !items.length ? (
            <p className="p-4 text-ink-muted">No hay contingencias en este mes y estado.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Ocurrió</TableHead>
                  <TableHead>Tipo</TableHead>
                  <TableHead>Documento</TableHead>
                  <TableHead>Motivo</TableHead>
                  <TableHead>Registró</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Regularización</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((c) => (
                  <FilaContingencia key={c.id} c={c} puedeRegularizar={puedeRegistrar} />
                ))}
              </TableBody>
            </Table>
          )}
        </div>
      </ListadoModulo>

      {puedeReporte && (
        <section aria-label="Reporte mensual" className="flex flex-col gap-2">
          <ReporteShell
            reporte={reporte.data}
            isLoading={reporte.isLoading}
            error={esApiError(reporte.error) ? reporte.error : undefined}
            onRetry={() => void reporte.refetch()}
            nombreArchivo={`contingencias-timbrado-${periodo}`}
            nivelTitulo="h2"
          />
        </section>
      )}
    </main>
  );
}

function EstadoContingenciaBadge({ c }: { c: ContingenciaItem }) {
  if (c.estado === 'Abierta')
    return (
      <Badge variant={c.vencida ? 'danger' : c.porVencer ? 'warning' : 'neutral'}>
        {c.vencida ? 'Vencida' : 'Abierta'}
      </Badge>
    );
  return (
    <Badge variant={c.estado === 'Regularizada' ? 'success' : 'neutral'}>
      {ESTADO_CONTINGENCIA_LABEL[c.estado]}
    </Badge>
  );
}

function FilaContingencia({ c, puedeRegularizar }: { c: ContingenciaItem; puedeRegularizar: boolean }) {
  const regularizar = useRegularizarContingencia();
  const [abierto, setAbierto] = useState(false);
  const [nota, setNota] = useState('');
  const manualSinDocumento = c.estado === 'Abierta' && !c.comprobanteId;

  function enviar() {
    if (!nota.trim()) {
      toast.error('Indica cómo se regularizó.');
      return;
    }
    regularizar.mutate(
      { id: c.id, nota: nota.trim(), idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Contingencia regularizada.');
          setAbierto(false);
        },
        onError: (error) => mostrarError(error, 'No se pudo regularizar la contingencia.'),
      },
    );
  }

  return (
    <TableRow className="h-12 align-top" data-testid={`contingencia-${c.id}`}>
      <TableCell className="whitespace-nowrap">{formatDateTime(c.ocurridaAt)}</TableCell>
      <TableCell>
        <p>{TIPO_CONTINGENCIA_LABEL[c.tipo]}</p>
        <p className="text-xs text-ink-muted">{c.origen === 'Automatica' ? 'Automática' : 'Manual'}</p>
      </TableCell>
      <TableCell>
        {c.comprobanteId && c.comprobanteFolio ? (
          <>
            <Link
              to="/facturacion/facturas/$id"
              params={{ id: c.comprobanteId }}
              className="font-mono font-medium"
            >
              {c.comprobanteFolio}
            </Link>
            {c.comprobanteEstado && <p className="text-xs text-ink-muted">{c.comprobanteEstado}</p>}
          </>
        ) : (
          <span className="text-ink-muted">Sin documento</span>
        )}
      </TableCell>
      <TableCell className="max-w-80">
        <p className="break-words">{c.motivo}</p>
        {c.errorCodigo && <p className="font-mono text-xs text-ink-muted">{c.errorCodigo}</p>}
      </TableCell>
      <TableCell>{c.registradaPor ?? 'Sistema'}</TableCell>
      <TableCell>
        <EstadoContingenciaBadge c={c} />
        {c.estado === 'Abierta' && (
          <p className="mt-1 text-xs text-ink-muted">
            A más tardar {formatDate(parseDateOnlyLocal(c.fechaLimite))}
          </p>
        )}
      </TableCell>
      <TableCell>
        {c.estado !== 'Abierta' ? (
          <div data-testid="regularizacion">
            {c.uuidFinal && <p className="font-mono text-xs">{c.uuidFinal}</p>}
            {c.notaCierre && <p>{c.notaCierre}</p>}
            <p className="text-xs text-ink-muted">
              {c.cerradaAt ? formatDateTime(c.cerradaAt) : '—'}
              {c.cerradaPor ? ` · ${c.cerradaPor}` : ''}
            </p>
          </div>
        ) : manualSinDocumento ? (
          abierto ? (
            <div className="flex min-w-64 flex-col gap-2">
              <Textarea
                aria-label="Cómo se regularizó"
                placeholder="Cómo se regularizó (obligatorio)"
                value={nota}
                maxLength={2000}
                onChange={(e) => setNota(e.target.value)}
              />
              <div className="flex gap-2">
                <Button size="sm" variant="ghost" onClick={() => setAbierto(false)} disabled={regularizar.isPending}>
                  Volver
                </Button>
                <Button size="sm" onClick={enviar} disabled={regularizar.isPending}>
                  {regularizar.isPending ? 'Guardando…' : 'Regularizar'}
                </Button>
              </div>
            </div>
          ) : (
            <Button
              size="sm"
              variant="outline"
              onClick={() => setAbierto(true)}
              disabled={!puedeRegularizar}
              title={puedeRegularizar ? undefined : 'Necesitas el permiso para reintentar timbrados.'}
            >
              Regularizar
            </Button>
          )
        ) : (
          <p className="text-xs text-ink-muted">Se regulariza al timbrar o descartar el documento.</p>
        )}
      </TableCell>
    </TableRow>
  );
}

function FormRegistrar({ onClose }: { onClose: () => void }) {
  const registrar = useRegistrarContingencia();
  const [sucursalId, setSucursalId] = useState<string | null>(null);
  const [tipo, setTipo] = useState<TipoContingencia>('Sat');
  const [motivo, setMotivo] = useState('');
  const [ocurrida, setOcurrida] = useState('');
  const faltante = !sucursalId
    ? 'Elige la sucursal.'
    : !ocurrida
      ? 'Indica cuándo ocurrió.'
      : !motivo.trim()
        ? 'Indica el motivo.'
        : null;

  function enviar() {
    if (faltante || !sucursalId) return;
    registrar.mutate(
      {
        body: { sucursalId, tipo, motivo: motivo.trim(), ocurridaAt: parseLocalToUtc(ocurrida).toISOString() },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Contingencia registrada.');
          onClose();
        },
        onError: (error) => mostrarError(error, 'No se pudo registrar la contingencia.'),
      },
    );
  }

  return (
    <section
      aria-label="Registrar contingencia"
      className="flex flex-col gap-3 rounded-md border border-dashed border-primary p-4"
    >
      <h2 className="font-medium">Registrar contingencia</h2>
      <p className="text-ink-muted">
        Las caídas del PAC y del certificado al timbrar se registran solas. Aquí se capturan las del
        SAT, de internet u otras.
      </p>
      <div className="grid gap-3 sm:grid-cols-3">
        <div className="flex flex-col gap-1">
          <Label className="text-xs text-ink-muted">Sucursal</Label>
          <SucursalSelector value={sucursalId} onChange={(id) => setSucursalId(id ?? null)} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="tipo-contingencia" className="text-xs text-ink-muted">
            Tipo
          </Label>
          <Select value={tipo} onValueChange={(v) => setTipo(v as TipoContingencia)}>
            <SelectTrigger id="tipo-contingencia">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {TIPOS_MANUALES.map((t) => (
                <SelectItem key={t} value={t}>
                  {TIPO_CONTINGENCIA_LABEL[t]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="ocurrida-contingencia" className="text-xs text-ink-muted">
            Ocurrió
          </Label>
          <Input
            id="ocurrida-contingencia"
            type="datetime-local"
            value={ocurrida}
            onChange={(e) => setOcurrida(e.target.value)}
          />
        </div>
      </div>
      <div className="flex flex-col gap-1">
        <Label htmlFor="motivo-contingencia" className="text-xs text-ink-muted">
          Motivo
        </Label>
        <Textarea
          id="motivo-contingencia"
          value={motivo}
          maxLength={2000}
          onChange={(e) => setMotivo(e.target.value)}
          placeholder="Qué falló y qué documentos se vieron afectados"
        />
      </div>
      <div className="flex items-center gap-2">
        <Button variant="ghost" onClick={onClose} disabled={registrar.isPending}>
          Cancelar
        </Button>
        <Button onClick={enviar} disabled={!!faltante || registrar.isPending} title={faltante ?? undefined}>
          {registrar.isPending ? 'Guardando…' : 'Registrar'}
        </Button>
        {faltante && <p className="text-xs text-ink-muted">{faltante}</p>}
      </div>
    </section>
  );
}
