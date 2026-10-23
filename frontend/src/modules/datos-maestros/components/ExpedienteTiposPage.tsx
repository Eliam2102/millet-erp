import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { PageHeader } from '@/components/erp/PageHeader';
import { useId, useState } from 'react';
import { Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { InlineFormShell } from '@/modules/catalogos/components/InlineFormShell';
import {
  useGuardarTipoExpedienteProveedor,
  useTiposExpedienteProveedor,
  AplicaOrigenDocumento,
  type TipoExpedienteProveedor,
} from '@/modules/datos-maestros/api/expediente-tipos';
import { mensajeErrorAdjunto } from '@/components/erp/adjuntos/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const ORIGEN_ETIQUETA: Record<AplicaOrigenDocumento, string> = {
  [AplicaOrigenDocumento.Todos]: 'Todos los orígenes',
  [AplicaOrigenDocumento.SoloNacional]: 'Solo nacional',
  [AplicaOrigenDocumento.SoloExtranjero]: 'Solo extranjero',
};

function aplicaA(t: TipoExpedienteProveedor) {
  const origen = ORIGEN_ETIQUETA[t.aplicaOrigen];
  return t.soloPersonaMoral ? `${origen} · solo persona moral` : origen;
}

/** Matriz de documentos del expediente de proveedor; edición solo con `expediente-configurar`. */
export function ExpedienteTiposPage() {
  const canConfigurar = useHasPermission(
    PermisosCanonicos.DatosMaestrosProveedoresExpedienteConfigurar,
  );
  const query = useTiposExpedienteProveedor();
  // 'nuevo' | id del tipo en edición | null
  const [editando, setEditando] = useState<string | null>(null);

  const [busqueda, setBusqueda] = useState('');
  const filtrados = (query.data ?? []).filter((item) =>
    [item.nombre].some((v) =>
      v.toLocaleLowerCase('es-MX').includes(busqueda.toLocaleLowerCase('es-MX')),
    ),
  );
  const pagina = usePaginacionLocal(filtrados, busqueda);
  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Documentos del expediente de proveedor</>}
        acciones={
          <>
            {canConfigurar ? (
              <Button
                size="sm"
                onClick={() => setEditando('nuevo')}
                disabled={editando === 'nuevo'}
              >
                <Plus /> Nuevo documento
              </Button>
            ) : (
              <p className="text-xs text-ink-muted">
                Solo lectura: necesitas el permiso para configurar documentos del expediente.
              </p>
            )}
          </>
        }
      />

      {editando === 'nuevo' && canConfigurar && <TipoForm onClose={() => setEditando(null)} />}

      <ListadoModulo
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Buscar en el catálogo"
                placeholder="Clave o nombre"
                value={busqueda}
                onChange={(e) => setBusqueda(e.target.value)}
              />
            }
            total={`${filtrados.length} registros`}
          />
        }
        pie={
          <PaginacionTabla
            total={filtrados.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.onChange}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading || !query.data ? (
          <TableSkeleton
            rows={5}
            columns={[{ width: 'w-48' }, { width: 'w-24' }, { width: 'w-24' }, { width: 'w-32' }]}
          />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>Obligatorio moral</TableHead>
                <TableHead>Obligatorio física</TableHead>
                <TableHead>Vigencia</TableHead>
                <TableHead>Aplica a</TableHead>
                <TableHead>Activo</TableHead>
                {canConfigurar && (
                  <TableHead>
                    <span className="sr-only">Acciones</span>
                  </TableHead>
                )}
              </TableRow>
            </TableHeader>
            <TableBody>
              {pagina.visibles.map((t) => (
                <FilaTipo
                  key={t.id}
                  tipo={t}
                  canConfigurar={canConfigurar}
                  editando={editando === t.id}
                  onEditar={() => setEditando(t.id)}
                  onClose={() => setEditando(null)}
                />
              ))}
            </TableBody>
          </Table>
        )}
      </ListadoModulo>
    </div>
  );
}

function FilaTipo({
  tipo,
  canConfigurar,
  editando,
  onEditar,
  onClose,
}: {
  tipo: TipoExpedienteProveedor;
  canConfigurar: boolean;
  editando: boolean;
  onEditar: () => void;
  onClose: () => void;
}) {
  return (
    <>
      <TableRow data-tipo={tipo.codigo}>
        <TableCell className="font-medium text-ink">{tipo.nombre}</TableCell>
        <TableCell>{tipo.obligatorioPersonaMoral ? 'Sí' : 'No'}</TableCell>
        <TableCell>{tipo.obligatorioPersonaFisica ? 'Sí' : 'No'}</TableCell>
        <TableCell className="tabular-nums">
          {tipo.vigenciaMeses == null
            ? 'Sin vigencia'
            : `${tipo.vigenciaMeses} ${tipo.vigenciaMeses === 1 ? 'mes' : 'meses'}`}
        </TableCell>
        <TableCell>{aplicaA(tipo)}</TableCell>
        <TableCell>
          <Badge variant={tipo.activo ? 'success' : 'neutral'}>
            {tipo.activo ? 'Activo' : 'Inactivo'}
          </Badge>
        </TableCell>
        {canConfigurar && (
          <TableCell className="text-right">
            <Button
              size="sm"
              variant="ghost"
              onClick={onEditar}
              aria-label={`Editar ${tipo.nombre}`}
            >
              Editar
            </Button>
          </TableCell>
        )}
      </TableRow>
      {editando && canConfigurar && (
        <TableRow>
          <TableCell colSpan={7}>
            <TipoForm tipo={tipo} onClose={onClose} />
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

function TipoForm({ tipo, onClose }: { tipo?: TipoExpedienteProveedor; onClose: () => void }) {
  const ids = useId();
  const idempotencyKey = useFormIdempotencyKey();
  const guardar = useGuardarTipoExpedienteProveedor();
  const [codigo, setCodigo] = useState(tipo?.codigo ?? '');
  const [nombre, setNombre] = useState(tipo?.nombre ?? '');
  const [obligatorioMoral, setObligatorioMoral] = useState(tipo?.obligatorioPersonaMoral ?? false);
  const [obligatorioFisica, setObligatorioFisica] = useState(
    tipo?.obligatorioPersonaFisica ?? false,
  );
  const [vigencia, setVigencia] = useState(tipo?.vigenciaMeses?.toString() ?? '');
  const [soloMoral, setSoloMoral] = useState(tipo?.soloPersonaMoral ?? false);
  const [aplicaOrigen, setAplicaOrigen] = useState<AplicaOrigenDocumento>(
    tipo?.aplicaOrigen ?? AplicaOrigenDocumento.Todos,
  );
  const [activo, setActivo] = useState(tipo?.activo ?? true);
  const [error, setError] = useState<string | null>(null);

  async function enviar(e: React.FormEvent) {
    e.preventDefault();
    const meses = vigencia.trim() === '' ? null : Number(vigencia);
    if (!nombre.trim() || (!tipo && !codigo.trim())) {
      setError('Captura el código y el nombre del documento.');
      return;
    }
    if (meses != null && (!Number.isInteger(meses) || meses < 1)) {
      setError('La vigencia debe ser un número entero de meses (1 o más), o déjala vacía.');
      return;
    }
    setError(null);
    try {
      await guardar.mutateAsync({
        tipoId: tipo?.id,
        version: tipo?.version,
        idempotencyKey,
        payload: {
          codigo: tipo?.codigo ?? codigo.trim(),
          nombre: nombre.trim(),
          obligatorioPersonaMoral: obligatorioMoral,
          obligatorioPersonaFisica: obligatorioFisica,
          vigenciaMeses: meses,
          soloPersonaMoral: soloMoral,
          aplicaOrigen,
          activo,
        },
      });
      toast.success(tipo ? 'Documento actualizado.' : 'Documento creado.');
      onClose();
    } catch (err) {
      setError(mensajeErrorAdjunto(err));
    }
  }

  const check = (id: string, label: string, v: boolean, set: (b: boolean) => void) => (
    <label htmlFor={`${ids}-${id}`} className="flex items-center gap-2 text-sm text-ink">
      <input
        id={`${ids}-${id}`}
        type="checkbox"
        checked={v}
        onChange={(e) => set(e.target.checked)}
      />
      {label}
    </label>
  );

  return (
    <InlineFormShell
      ariaLabel={tipo ? `Editar documento ${tipo.nombre}` : 'Nuevo documento del expediente'}
      onSubmit={enviar}
      onCancel={onClose}
      isPending={guardar.isPending}
      submitLabel={tipo ? 'Guardar' : 'Crear documento'}
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <FormGrid>
          <FormCampo span={6}>
            <label htmlFor={`${ids}-codigo`} className="text-xs font-medium text-ink-strong">
              Código
            </label>
          </FormCampo>
          <FormCampo span={6}>
            <Input
              id={`${ids}-codigo`}
              value={codigo}
              onChange={(e) => setCodigo(e.target.value)}
              readOnly={!!tipo}
              maxLength={50}
              className="font-mono"
            />
          </FormCampo>
        </FormGrid>
        <FormGrid>
          <FormCampo span={6}>
            <label htmlFor={`${ids}-nombre`} className="text-xs font-medium text-ink-strong">
              Nombre
            </label>
          </FormCampo>
          <FormCampo span={6}>
            <Input
              id={`${ids}-nombre`}
              value={nombre}
              onChange={(e) => setNombre(e.target.value)}
              maxLength={150}
            />
          </FormCampo>
        </FormGrid>
        <FormGrid>
          <FormCampo span={6}>
            <label htmlFor={`${ids}-vigencia`} className="text-xs font-medium text-ink-strong">
              Vigencia (meses)
            </label>
          </FormCampo>
          <FormCampo span={6}>
            <Input
              id={`${ids}-vigencia`}
              type="number"
              min={1}
              value={vigencia}
              onChange={(e) => setVigencia(e.target.value)}
              aria-describedby={`${ids}-vigencia-ayuda`}
            />
          </FormCampo>
          <FormCampo span={6}>
            <p id={`${ids}-vigencia-ayuda`} className="text-xs text-ink-muted">
              Vacía = el documento no vence.
            </p>
          </FormCampo>
        </FormGrid>
      </div>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <FormGrid>
          <FormCampo span={6}>
            <label htmlFor={`${ids}-origen`} className="text-xs font-medium text-ink-strong">
              Aplica a
            </label>
          </FormCampo>
          <FormCampo span={6}>
            <select
              id={`${ids}-origen`}
              value={aplicaOrigen}
              onChange={(e) => setAplicaOrigen(Number(e.target.value) as AplicaOrigenDocumento)}
              className="flex h-ctl-xl max-sm:min-h-touch w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
            >
              <option value={AplicaOrigenDocumento.Todos}>
                {ORIGEN_ETIQUETA[AplicaOrigenDocumento.Todos]}
              </option>
              <option value={AplicaOrigenDocumento.SoloNacional}>
                {ORIGEN_ETIQUETA[AplicaOrigenDocumento.SoloNacional]}
              </option>
              <option value={AplicaOrigenDocumento.SoloExtranjero}>
                {ORIGEN_ETIQUETA[AplicaOrigenDocumento.SoloExtranjero]}
              </option>
            </select>
          </FormCampo>
        </FormGrid>
      </div>
      <div className="flex flex-wrap gap-x-6 gap-y-2">
        {check(
          'obligatorio-moral',
          'Obligatorio para persona moral',
          obligatorioMoral,
          setObligatorioMoral,
        )}
        {check(
          'obligatorio-fisica',
          'Obligatorio para persona física',
          obligatorioFisica,
          setObligatorioFisica,
        )}
        {check('moral', 'Solo persona moral', soloMoral, setSoloMoral)}
        {check('activo', 'Activo', activo, setActivo)}
      </div>
      {error && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
    </InlineFormShell>
  );
}
