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
  type TipoExpedienteProveedor,
} from '@/modules/datos-maestros/api/expediente-tipos';
import { mensajeErrorAdjunto } from '@/components/erp/adjuntos/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

function aplicaA(t: TipoExpedienteProveedor) {
  if (t.soloPersonaMoral && t.soloNacional) return 'Solo persona moral nacional';
  if (t.soloPersonaMoral) return 'Solo persona moral';
  if (t.soloNacional) return 'Solo nacional';
  return 'Todos';
}

/** Matriz de documentos del expediente de proveedor; edición solo con `expediente-configurar`. */
export function ExpedienteTiposPage() {
  const canConfigurar = useHasPermission(
    PermisosCanonicos.DatosMaestrosProveedoresExpedienteConfigurar,
  );
  const query = useTiposExpedienteProveedor();
  // 'nuevo' | id del tipo en edición | null
  const [editando, setEditando] = useState<string | null>(null);

  return (
    <div className="space-y-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-semibold tracking-tight">
          Documentos del expediente de proveedor
        </h1>
        {canConfigurar ? (
          <Button size="sm" onClick={() => setEditando('nuevo')} disabled={editando === 'nuevo'}>
            <Plus /> Nuevo documento
          </Button>
        ) : (
          <p className="text-xs text-ink-muted">
            Solo lectura: necesitas el permiso para configurar documentos del expediente.
          </p>
        )}
      </div>

      {editando === 'nuevo' && canConfigurar && (
        <TipoForm onClose={() => setEditando(null)} />
      )}

      {query.isError ? (
        <ErrorState
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || !query.data ? (
        <TableSkeleton rows={5} columns={[{ width: 'w-48' }, { width: 'w-24' }, { width: 'w-32' }]} />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Nombre</TableHead>
              <TableHead>Obligatorio</TableHead>
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
            {query.data.map((t) => (
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
        <TableCell>{tipo.obligatorio ? 'Sí' : 'No'}</TableCell>
        <TableCell className="tabular-nums">
          {tipo.vigenciaMeses == null
            ? 'Sin vigencia'
            : `${tipo.vigenciaMeses} ${tipo.vigenciaMeses === 1 ? 'mes' : 'meses'}`}
        </TableCell>
        <TableCell>{aplicaA(tipo)}</TableCell>
        <TableCell>
          <Badge variant={tipo.activo ? 'success' : 'neutral'}>{tipo.activo ? 'Activo' : 'Inactivo'}</Badge>
        </TableCell>
        {canConfigurar && (
          <TableCell className="text-right">
            <Button size="sm" variant="ghost" onClick={onEditar} aria-label={`Editar ${tipo.nombre}`}>
              Editar
            </Button>
          </TableCell>
        )}
      </TableRow>
      {editando && canConfigurar && (
        <TableRow>
          <TableCell colSpan={6}>
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
  const [obligatorio, setObligatorio] = useState(tipo?.obligatorio ?? false);
  const [vigencia, setVigencia] = useState(tipo?.vigenciaMeses?.toString() ?? '');
  const [soloMoral, setSoloMoral] = useState(tipo?.soloPersonaMoral ?? false);
  const [soloNacional, setSoloNacional] = useState(tipo?.soloNacional ?? false);
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
          obligatorio,
          vigenciaMeses: meses,
          soloPersonaMoral: soloMoral,
          soloNacional,
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
      <input id={`${ids}-${id}`} type="checkbox" checked={v} onChange={(e) => set(e.target.checked)} />
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
        <div className="grid gap-1.5">
          <label htmlFor={`${ids}-codigo`} className="text-xs font-medium text-ink-strong">
            Código
          </label>
          <Input
            id={`${ids}-codigo`}
            value={codigo}
            onChange={(e) => setCodigo(e.target.value)}
            readOnly={!!tipo}
            maxLength={50}
            className="font-mono"
          />
        </div>
        <div className="grid gap-1.5">
          <label htmlFor={`${ids}-nombre`} className="text-xs font-medium text-ink-strong">
            Nombre
          </label>
          <Input
            id={`${ids}-nombre`}
            value={nombre}
            onChange={(e) => setNombre(e.target.value)}
            maxLength={150}
          />
        </div>
        <div className="grid gap-1.5">
          <label htmlFor={`${ids}-vigencia`} className="text-xs font-medium text-ink-strong">
            Vigencia (meses)
          </label>
          <Input
            id={`${ids}-vigencia`}
            type="number"
            min={1}
            value={vigencia}
            onChange={(e) => setVigencia(e.target.value)}
            aria-describedby={`${ids}-vigencia-ayuda`}
          />
          <p id={`${ids}-vigencia-ayuda`} className="text-xs text-ink-muted">
            Vacía = el documento no vence.
          </p>
        </div>
      </div>
      <div className="flex flex-wrap gap-x-6 gap-y-2">
        {check('obligatorio', 'Obligatorio', obligatorio, setObligatorio)}
        {check('moral', 'Solo persona moral', soloMoral, setSoloMoral)}
        {check('nacional', 'Solo nacional', soloNacional, setSoloNacional)}
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
