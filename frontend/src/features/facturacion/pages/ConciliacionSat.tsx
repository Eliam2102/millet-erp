import { KpiCard, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
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
  TIPO_DIFERENCIA_SAT_LABEL,
  TipoDiferenciaSat,
  useConciliacionesSat,
  useConciliarCfdiSat,
  useDiferenciasSat,
  useMarcarDiferenciaSatRevisada,
  type ConciliacionSat as Corrida,
  type DiferenciaSat,
} from '@/features/facturacion/api/useConciliacionSat';
import { mostrarError } from '@/features/facturacion/lib/mostrar-error-cancelacion';
import {
  formatDate,
  formatDateTime,
  formatDateTimeWithSeconds,
  parseDateOnlyLocal,
} from '@/lib/datetime';
import { Upload } from 'lucide-react';
import { useRef, useState } from 'react';
import { toast } from 'sonner';

const TIPOS: readonly TipoDiferenciaSat[] = [
  TipoDiferenciaSat.EstatusDistinto,
  TipoDiferenciaSat.FaltanteEnErp,
  TipoDiferenciaSat.FaltanteEnSat,
  TipoDiferenciaSat.TotalDistinto,
  TipoDiferenciaSat.TipoDistinto,
  TipoDiferenciaSat.ReceptorDistinto,
];

const PENDIENTES = 'pendientes';
const REVISADAS = 'revisadas';
const TODAS = 'todas';

function periodo(c: Corrida): string {
  return `${formatDate(parseDateOnlyLocal(c.desde))} al ${formatDate(parseDateOnlyLocal(c.hasta))}`;
}

/** Lunes a domingo de la semana pasada (la conciliación es semanal), en formato de input date. */
function semanaPasada(): { desde: string; hasta: string } {
  const hoy = new Date();
  const lunes = new Date(
    hoy.getFullYear(),
    hoy.getMonth(),
    hoy.getDate() - ((hoy.getDay() + 6) % 7) - 7,
  );
  const domingo = new Date(lunes.getFullYear(), lunes.getMonth(), lunes.getDate() + 6);
  const iso = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  return { desde: iso(lunes), hasta: iso(domingo) };
}

/**
 * <c>&lt;ConciliacionSat/&gt;</c> — FAC-10: conciliación semanal de los CFDI
 * emitidos contra el SAT. Se sube el archivo de metadatos que da el SAT (portal o
 * descarga masiva) con su periodo; cada corrida queda registrada con sus
 * diferencias, y cada diferencia se marca como revisada al atenderla.
 */
export function ConciliacionSat() {
  const corridas = useConciliacionesSat();
  const [seleccion, setSeleccion] = useState<string | undefined>(undefined);
  const lista = corridas.data ?? [];
  const corrida = lista.find((c) => c.id === seleccion) ?? lista[0];

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo={<>Conciliación de CFDI contra el SAT</>}
        descripcion={
          <>
            Compara los CFDI emitidos que reporta el SAT contra los timbrados en el ERP: faltantes,
            estatus, total, tipo y receptor.
          </>
        }
      />

      <FormConciliar onConciliada={(c) => setSeleccion(c.id)} />

      {corridas.isError ? (
        <div role="alert">
          No se pudieron cargar las conciliaciones.{' '}
          <Button variant="secondary" onClick={() => void corridas.refetch()}>
            Reintentar
          </Button>
        </div>
      ) : corridas.isLoading ? (
        <p role="status">Cargando conciliaciones…</p>
      ) : !corrida ? (
        <p className="text-ink-muted">
          Aún no hay conciliaciones. Sube el archivo de metadatos del SAT de la semana para hacer la
          primera.
        </p>
      ) : (
        <DetalleCorrida corrida={corrida} corridas={lista} onSeleccionar={setSeleccion} />
      )}
    </div>
  );
}

function FormConciliar({ onConciliada }: { onConciliada: (c: Corrida) => void }) {
  const conciliar = useConciliarCfdiSat();
  const archivoInput = useRef<HTMLInputElement>(null);
  const inicial = semanaPasada();
  const [archivo, setArchivo] = useState<File | null>(null);
  const [desde, setDesde] = useState(inicial.desde);
  const [hasta, setHasta] = useState(inicial.hasta);
  const faltante = !archivo
    ? 'Elige el archivo de metadatos del SAT.'
    : !desde || !hasta
      ? 'Indica el periodo que cubre el archivo.'
      : hasta < desde
        ? 'La fecha final no puede ser anterior a la inicial.'
        : null;

  function enviar() {
    if (faltante || !archivo) return;
    conciliar.mutate(
      { archivo, desde, hasta, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (c) => {
          toast.success(
            c.sinDiferencias
              ? 'Conciliación sin diferencias.'
              : `Conciliación con ${c.conDiferencias} CFDI con diferencias.`,
          );
          setArchivo(null);
          if (archivoInput.current) archivoInput.current.value = '';
          onConciliada(c);
        },
        onError: (error) => mostrarError(error, 'No se pudo conciliar el archivo del SAT.'),
      },
    );
  }

  return (
    <section
      aria-label="Nueva conciliación"
      className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <div>
        <h2 className="font-medium">Nueva conciliación</h2>
        <p className="text-xs text-ink-muted">
          Descarga del portal del SAT los metadatos de los CFDI emitidos del periodo (archivo .txt o
          el .zip del paquete) y súbelo aquí.
        </p>
      </div>
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <Label htmlFor="conciliacion-archivo" className="text-xs text-ink-muted">
            Archivo del SAT *
          </Label>
          <div className="flex items-center gap-2">
            <Button type="button" variant="outline" onClick={() => archivoInput.current?.click()}>
              Elegir archivo
            </Button>
            <span className="text-xs text-ink-muted">{archivo?.name ?? 'Sin archivo'}</span>
          </div>
          <Input
            ref={archivoInput}
            className="hidden"
            id="conciliacion-archivo"
            type="file"
            accept=".txt,.zip"
            onChange={(e) => setArchivo(e.target.files?.[0] ?? null)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="conciliacion-desde" className="text-xs text-ink-muted">
            Desde *
          </Label>
          <Input
            id="conciliacion-desde"
            type="date"
            value={desde}
            onChange={(e) => setDesde(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="conciliacion-hasta" className="text-xs text-ink-muted">
            Hasta *
          </Label>
          <Input
            id="conciliacion-hasta"
            type="date"
            value={hasta}
            onChange={(e) => setHasta(e.target.value)}
          />
        </div>
        <Button
          onClick={enviar}
          disabled={!!faltante || conciliar.isPending}
          title={faltante ?? undefined}
        >
          <Upload className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
          {conciliar.isPending ? 'Conciliando…' : 'Conciliar'}
        </Button>
        {faltante && <p className="text-xs text-ink-muted">{faltante}</p>}
      </div>
    </section>
  );
}

function DetalleCorrida({
  corrida,
  corridas,
  onSeleccionar,
}: {
  corrida: Corrida;
  corridas: Corrida[];
  onSeleccionar: (id: string) => void;
}) {
  const [tipo, setTipo] = useState<TipoDiferenciaSat | undefined>(undefined);
  const [vista, setVista] = useState<string>(PENDIENTES);
  const revisada = vista === PENDIENTES ? false : vista === REVISADAS ? true : undefined;
  const diferencias = useDiferenciasSat(corrida.id, tipo, revisada);
  const conteos = Object.fromEntries(
    corrida.porTipo.map((p) => [
      p.tipo,
      vista === PENDIENTES ? p.total - p.revisadas : vista === REVISADAS ? p.revisadas : p.total,
    ]),
  ) as Record<number, number>;
  const items = diferencias.data ?? [];

  return (
    <section
      aria-label="Resultado de la conciliación"
      className="flex min-h-0 flex-1 flex-col gap-5"
    >
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <Label htmlFor="conciliacion-corrida" className="text-xs text-ink-muted">
            Conciliación
          </Label>
          <Select value={corrida.id} onValueChange={onSeleccionar}>
            <SelectTrigger id="conciliacion-corrida" className="w-[32rem] max-w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {corridas.map((c) => (
                <SelectItem key={c.id} value={c.id}>
                  {periodo(c)} · {formatDateTimeWithSeconds(c.ejecutadaEn)} · {c.nombreArchivo}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <p className="text-xs text-ink-muted">
          Archivo <span className="font-mono">{corrida.nombreArchivo}</span>
        </p>
        <Badge
          variant={
            corrida.sinDiferencias
              ? 'success'
              : corrida.diferenciasPendientes > 0
                ? 'warning'
                : 'neutral'
          }
        >
          {corrida.sinDiferencias
            ? 'Sin diferencias'
            : corrida.diferenciasPendientes > 0
              ? 'Con diferencias por revisar'
              : 'Diferencias revisadas'}
        </Badge>
      </div>

      {corrida.sinDiferencias && (
        <p role="status" className="text-ink-secondary">
          Los {corrida.cfdiSat} CFDI del SAT y los {corrida.cfdiErp} timbrados en el ERP del{' '}
          {periodo(corrida)} cuadran en estatus, total, tipo y receptor. La corrida queda
          registrada.
        </p>
      )}

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <KpiCard etiqueta="CFDI en el SAT" valor={corrida.cfdiSat} tono="info" />
        <KpiCard
          etiqueta="Timbrados en el ERP"
          valor={corrida.cfdiErp}
          tono="info"
          subdato="en el periodo"
        />
        <KpiCard etiqueta="Coinciden" valor={corrida.coincidentes} tono="success" />
        <KpiCard
          etiqueta="Con diferencias"
          valor={corrida.conDiferencias}
          tono={corrida.diferenciasPendientes > 0 ? 'warning' : 'neutral'}
          subdato={`${corrida.diferenciasPendientes} por revisar`}
        />
      </div>

      {!corrida.sinDiferencias && (
        <ListadoModulo
          tabs={
            <TabsEstado
              aria-label="Filtrar por tipo de diferencia"
              opciones={TIPOS.map((t) => ({ valor: t, label: TIPO_DIFERENCIA_SAT_LABEL[t] }))}
              valor={tipo}
              conteos={conteos}
              onChange={(v) => setTipo(v as TipoDiferenciaSat | undefined)}
            />
          }
          filtros={
            <div className="flex items-center gap-2">
              <Label htmlFor="conciliacion-vista" className="text-xs text-ink-muted">
                Mostrar
              </Label>
              <Select value={vista} onValueChange={setVista}>
                <SelectTrigger id="conciliacion-vista" className="w-44">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={PENDIENTES}>Por revisar</SelectItem>
                  <SelectItem value={REVISADAS}>Revisadas</SelectItem>
                  <SelectItem value={TODAS}>Todas</SelectItem>
                </SelectContent>
              </Select>
              <span className="ml-auto text-xs text-ink-muted">{items.length} diferencia(s)</span>
            </div>
          }
        >
          <div className="min-h-0 flex-1 overflow-auto">
            {diferencias.isError ? (
              <div role="alert" className="p-4">
                No se pudieron cargar las diferencias.{' '}
                <Button variant="secondary" onClick={() => void diferencias.refetch()}>
                  Reintentar
                </Button>
              </div>
            ) : diferencias.isLoading ? (
              <p role="status" className="p-4">
                Cargando diferencias…
              </p>
            ) : !items.length ? (
              <p className="p-4 text-ink-muted">No hay diferencias con este filtro.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Diferencia</TableHead>
                    <TableHead>CFDI</TableHead>
                    <TableHead>Receptor</TableHead>
                    <TableHead>SAT</TableHead>
                    <TableHead>ERP</TableHead>
                    <TableHead>Revisión</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((d) => (
                    <FilaDiferencia key={d.id} d={d} conciliacionId={corrida.id} />
                  ))}
                </TableBody>
              </Table>
            )}
          </div>
        </ListadoModulo>
      )}
    </section>
  );
}

/** Diferencias cuyos valores traen un importe (total o faltante de un lado). */
function conImporte(d: DiferenciaSat): boolean {
  return (
    d.tipo === TipoDiferenciaSat.TotalDistinto ||
    d.tipo === TipoDiferenciaSat.FaltanteEnErp ||
    d.tipo === TipoDiferenciaSat.FaltanteEnSat
  );
}

function FilaDiferencia({ d, conciliacionId }: { d: DiferenciaSat; conciliacionId: string }) {
  const marcar = useMarcarDiferenciaSatRevisada();
  const [abierto, setAbierto] = useState(false);
  const [nota, setNota] = useState('');

  function enviar() {
    marcar.mutate(
      {
        conciliacionId,
        diferenciaId: d.id,
        nota: nota.trim() || null,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Diferencia marcada como revisada.');
          setAbierto(false);
        },
        onError: (error) => mostrarError(error, 'No se pudo marcar la diferencia como revisada.'),
      },
    );
  }

  return (
    <TableRow className="h-12 align-top" data-testid={`diferencia-${d.id}`}>
      <TableCell>
        <Badge
          variant={
            d.tipo === TipoDiferenciaSat.EstatusDistinto ||
            d.tipo === TipoDiferenciaSat.FaltanteEnErp
              ? 'danger'
              : 'warning'
          }
        >
          {TIPO_DIFERENCIA_SAT_LABEL[d.tipo]}
        </Badge>
      </TableCell>
      <TableCell>
        {d.folio ? (
          <span className="font-mono font-medium">{d.folio}</span>
        ) : (
          <span className="text-ink-muted">Sin folio en el ERP</span>
        )}
        <p className="font-mono text-2xs text-ink-muted">{d.uuid}</p>
      </TableCell>
      <TableCell className="font-mono text-xs">
        {d.rfcReceptor ?? <span className="text-ink-muted">—</span>}
      </TableCell>
      <TableCell className="tabular-nums">
        {d.valorSat ?? <span className="text-ink-muted">—</span>}
        {d.valorSat && conImporte(d) && (
          <p className="text-2xs text-ink-muted">
            {d.moneda ? `${d.moneda} (moneda del CFDI)` : 'Moneda no informada por el SAT'}
          </p>
        )}
      </TableCell>
      <TableCell className="tabular-nums">
        {d.valorErp ?? <span className="text-ink-muted">—</span>}
        {d.valorErp && conImporte(d) && d.moneda && (
          <p className="text-2xs text-ink-muted">{d.moneda}</p>
        )}
      </TableCell>
      <TableCell>
        {d.revisada ? (
          <div>
            <Badge variant="success">Revisada</Badge>
            {d.nota && <p className="mt-1">{d.nota}</p>}
            <p className="text-xs text-ink-muted">
              {d.revisadaPorNombre ?? 'Usuario sin nombre'} ·{' '}
              {d.revisadaEn ? formatDateTime(d.revisadaEn) : '—'}
            </p>
          </div>
        ) : abierto ? (
          <div className="flex min-w-64 flex-col gap-2">
            <Textarea
              aria-label="Nota de la revisión"
              placeholder="Qué se encontró o cómo se resolvió (opcional)"
              value={nota}
              maxLength={500}
              onChange={(e) => setNota(e.target.value)}
            />
            <div className="flex gap-2">
              <Button
                size="sm"
                variant="ghost"
                onClick={() => setAbierto(false)}
                disabled={marcar.isPending}
              >
                Volver
              </Button>
              <Button size="sm" onClick={enviar} disabled={marcar.isPending}>
                {marcar.isPending ? 'Guardando…' : 'Marcar revisada'}
              </Button>
            </div>
          </div>
        ) : (
          <Button size="sm" variant="outline" onClick={() => setAbierto(true)}>
            Marcar revisada
          </Button>
        )}
      </TableCell>
    </TableRow>
  );
}
