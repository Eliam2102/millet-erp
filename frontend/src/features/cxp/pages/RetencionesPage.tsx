import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import { Badge } from '@/components/ui/badge';
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
import {
  useRetenciones,
  useGuardarRetencion,
  type RetencionCatalogo,
} from '@/features/cxp/api/useRetenciones';
import { RetencionSchema, type RetencionForm } from '@/features/cxp/lib/retenciones-p8';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const vacia: RetencionForm = {
  concepto: '',
  descripcion: '',
  impuesto: '001',
  tasa: 0,
  fuente: '',
  activa: true,
  motivo: '',
};
export function RetencionesPage() {
  const query = useRetenciones();
  const guardar = useGuardarRetencion();
  const puedeEditar = useHasPermission(PermisosCanonicos.CuentasPorPagarRetencionesAdministrar);
  const [form, setForm] = useState<RetencionForm | null>(null);
  const [error, setError] = useState('');
  function editar(r: RetencionCatalogo) {
    setError('');
    setForm({
      id: r.id,
      version: r.version,
      concepto: r.concepto,
      descripcion: r.descripcion,
      impuesto: r.impuesto as RetencionForm['impuesto'],
      tasa: r.tasa,
      fuente: r.fuente,
      activa: r.activa,
      motivo: '',
    });
  }
  function enviar() {
    const parsed = RetencionSchema.safeParse(form);
    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? 'Revisa los datos.');
      return;
    }
    guardar.mutate(parsed.data, {
      onSuccess: () => {
        setForm(null);
        setError('');
      },
      onError: (e) => setError(esApiError(e) ? e.message : 'No se pudo guardar la retención.'),
    });
  }
  return (
    <div className="space-y-4 text-sm text-ink">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-semibold">Retenciones por concepto</h1>
          <p className="text-ink-muted">
            Consulta y ajusta las propuestas fiscales para la captura de pasivos.
          </p>
        </div>
        {puedeEditar && (
          <Button
            onClick={() => {
              setForm({ ...vacia });
              setError('');
            }}
          >
            Nueva retención
          </Button>
        )}
      </div>
      <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2 text-warning-note-fg">
        Supuesto SAT, valida Fiscal (D03). Las tasas dependen del concepto y del régimen del
        proveedor.
      </p>
      {query.isLoading && <p>Cargando retenciones…</p>}
      {query.isError && (
        <div role="alert">
          <p>No se pudo consultar el catálogo.</p>
          <Button variant="outline" onClick={() => query.refetch()}>
            Reintentar
          </Button>
        </div>
      )}
      <div className="rounded-lg bg-surface-card shadow-card overflow-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Concepto</TableHead>
              <TableHead>Descripción</TableHead>
              <TableHead>Impuesto</TableHead>
              <TableHead className="text-right">Tasa</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Fuente</TableHead>
              <TableHead>Acción</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {query.data?.map((r) => (
              <TableRow key={r.id}>
                <TableCell>{r.concepto}</TableCell>
                <TableCell>{r.descripcion}</TableCell>
                <TableCell>
                  {r.impuesto === '001' ? 'ISR' : r.impuesto === '002' ? 'IVA' : 'IEPS'}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {(r.tasa * 100).toLocaleString('es-MX', { maximumFractionDigits: 6 })} %
                </TableCell>
                <TableCell>
                  <Badge variant={r.activa ? 'success' : 'neutral'}>
                    {r.activa ? 'Activa' : 'Inactiva'}
                  </Badge>
                </TableCell>
                <TableCell>
                  {/^https?:\/\//.test(r.fuente) ? (
                    <a className="text-brand" href={r.fuente} target="_blank" rel="noreferrer">
                      Consultar fuente
                    </a>
                  ) : (
                    r.fuente
                  )}
                </TableCell>
                <TableCell>
                  {puedeEditar && (
                    <Button variant="outline" onClick={() => editar(r)}>
                      Editar
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {form && (
        <form
          className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card"
          onSubmit={(e) => {
            e.preventDefault();
            enviar();
          }}
        >
          <h2 className="text-lg font-semibold">
            {form.id ? 'Editar retención' : 'Nueva retención'}
          </h2>
          {(['concepto', 'descripcion', 'fuente', 'motivo'] as const).map((key) => (
            <div key={key}>
              <Label htmlFor={`ret-${key}`}>
                {
                  {
                    concepto: 'Concepto',
                    descripcion: 'Descripción',
                    fuente: 'Fuente fiscal',
                    motivo: 'Motivo del cambio',
                  }[key]
                }
              </Label>
              <Input
                id={`ret-${key}`}
                value={form[key]}
                onChange={(e) => setForm({ ...form, [key]: e.target.value })}
              />
            </div>
          ))}
          <Label htmlFor="ret-impuesto">Impuesto</Label>
          <Select
            value={form.impuesto}
            onValueChange={(v) => setForm({ ...form, impuesto: v as RetencionForm['impuesto'] })}
          >
            <SelectTrigger id="ret-impuesto">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="001">ISR</SelectItem>
              <SelectItem value="002">IVA</SelectItem>
              <SelectItem value="003">IEPS</SelectItem>
            </SelectContent>
          </Select>
          <div>
            <Label htmlFor="ret-tasa">Tasa (%)</Label>
            <Input
              id="ret-tasa"
              type="number"
              min="0"
              max="100"
              step="0.000001"
              value={form.tasa * 100}
              onChange={(e) => setForm({ ...form, tasa: Number(e.target.value) / 100 })}
            />
          </div>
          <div className="flex items-center gap-2">
            <Checkbox
              id="ret-activa"
              checked={form.activa}
              onCheckedChange={(v) => setForm({ ...form, activa: v === true })}
            />
            <Label htmlFor="ret-activa">Activa</Label>
          </div>
          {error && (
            <p role="alert" className="text-danger-fg">
              {error}
            </p>
          )}
          <div className="flex gap-2">
            <Button type="button" variant="outline" onClick={() => setForm(null)}>
              Cancelar
            </Button>
            <Button type="submit" disabled={guardar.isPending}>
              Guardar retención
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}
