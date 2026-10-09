import { useState } from 'react';
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
import { useEditarCabeceraFactura } from '@/features/cxp/api/useFacturas';
import { useRetenciones } from '@/features/cxp/api/useRetenciones';
import type { FacturaDetalle } from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

/** Edición previa a autorización. Conserva el control de versión del pasivo. */
export function EditarDatosP8({ factura }: { factura: FacturaDetalle }) {
  const [obra, setObra] = useState(factura.obra ?? '');
  const [concepto, setConcepto] = useState(factura.conceptoRetencion ?? '');
  const [fecha, setFecha] = useState(factura.fechaContabilizacion.slice(0, 10));
  const [error, setError] = useState('');
  const permisoCatalogo = useHasPermission(PermisosCanonicos.CuentasPorPagarRetencionesLeer);
  const catalogo = useRetenciones(permisoCatalogo);
  const editar = useEditarCabeceraFactura();
  return (
    <form
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card text-sm"
      onSubmit={(e) => {
        e.preventDefault();
        setError('');
        editar.mutate(
          {
            id: factura.id,
            versionEsperada: factura.version,
            command: {
              folioProveedor: factura.folioProveedor,
              serieProveedor: factura.serieProveedor,
              fechaVencimiento: factura.fechaVencimiento,
              fechaContabilizacion: `${fecha}T00:00:00Z`,
              obra: obra.trim() || null,
              conceptoRetencion: concepto || null,
            },
          },
          {
            onError: (e) =>
              setError(
                esApiError(e)
                  ? (e.problem.detail ?? e.message)
                  : 'No se pudieron guardar los datos.',
              ),
          },
        );
      }}
    >
      <h2 className="text-lg font-semibold">Datos del pasivo</h2>
      <div>
        <Label htmlFor="edit-p8-obra">Obra</Label>
        <Input
          id="edit-p8-obra"
          maxLength={120}
          value={obra}
          onChange={(e) => setObra(e.target.value)}
        />
      </div>
      <div>
        <Label htmlFor="edit-p8-fecha">Fecha de contabilización</Label>
        <Input
          required
          id="edit-p8-fecha"
          type="date"
          value={fecha}
          onChange={(e) => setFecha(e.target.value)}
        />
      </div>
      <Label htmlFor="edit-p8-concepto">Concepto fiscal de retención</Label>
      <Select
        value={concepto || 'sin-concepto'}
        onValueChange={(v) => setConcepto(v === 'sin-concepto' ? '' : v)}
      >
        <SelectTrigger id="edit-p8-concepto">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="sin-concepto">Sin concepto: por confirmar</SelectItem>
          {[
            ...new Set([
              ...(catalogo.data?.filter((r) => r.activa).map((r) => r.concepto) ?? []),
              ...(concepto ? [concepto] : []),
            ]),
          ].map((c) => (
            <SelectItem key={c} value={c}>
              {c}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <p className="text-xs text-ink-muted">
        Supuesto SAT, valida Fiscal (D03). Contabilidad administra la apertura y reapertura de
        periodos.
      </p>
      {error && (
        <p role="alert" className="text-danger-fg">
          {error}
        </p>
      )}
      {editar.isSuccess && (
        <p role="status" className="text-success-fg">
          Datos guardados.
        </p>
      )}
      <Button disabled={editar.isPending}>Guardar datos del pasivo</Button>
    </form>
  );
}
