import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { AvisoEnLinea, Field } from '@/components/erp';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useEditarDevolucion } from '@/features/tesoreria/api/useTesoreria';
import {
  OrigenDevolucionCliente,
  type DevolucionClienteResponse,
} from '@/features/tesoreria/api/types';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { ClienteTesoreriaSelector } from './ClienteTesoreriaSelector';

/**
 * Edición inline (sin modal) de una devolución solicitada: borde punteado de
 * advertencia (edición). El tope usa <c>disponibleOrigen</c>, que ya excluye
 * esta devolución.
 */
export function EditarDevolucionForm({
  devolucion: d,
  onCerrar,
}: {
  devolucion: DevolucionClienteResponse;
  onCerrar: () => void;
}) {
  const keyFor = useBodyScopedIdempotencyKey();
  const editar = useEditarDevolucion();
  const esNc = d.origen === OrigenDevolucionCliente.NotaCredito;
  // La lectura enmascara la CLABE sin permiso: hay que volver a capturarla completa.
  const clabeEnmascarada = d.destinoClabe?.includes('*') === true;

  const [clienteId, setClienteId] = useState(d.clienteId);
  const [importe, setImporte] = useState(String(d.importe));
  const [motivo, setMotivo] = useState(d.motivo);
  const [banco, setBanco] = useState(d.destinoBanco ?? '');
  const [clabe, setClabe] = useState(clabeEnmascarada ? '' : (d.destinoClabe ?? ''));
  const [titular, setTitular] = useState(d.destinoTitular ?? '');
  const [aviso, setAviso] = useState<string | null>(null);

  const importeNum = Number(importe);
  const destinoVacio = !banco.trim() && !clabe.trim() && !titular.trim();
  const destinoCompleto = !!banco.trim() && /^\d{18}$/.test(clabe) && !!titular.trim();
  const valido =
    !!clienteId && importeNum > 0 && !!motivo.trim() && (destinoCompleto || (esNc && destinoVacio));

  function guardar() {
    setAviso(null);
    const body = {
      id: d.id,
      clienteId,
      importe: importeNum,
      motivo: motivo.trim(),
      banco: destinoVacio ? null : banco.trim(),
      clabe: destinoVacio ? null : clabe.trim(),
      titular: destinoVacio ? null : titular.trim(),
      versionEsperada: d.version,
    };
    editar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Devolución actualizada');
          onCerrar();
        },
        onError: (error) => {
          if (esApiError(error)) setAviso(error.problem.detail ?? error.problem.title);
          else toast.error('No se pudo actualizar la devolución.');
        },
      },
    );
  }

  return (
    <form
      aria-label="Editar devolución"
      className="space-y-3 rounded-lg border border-dashed border-warning bg-warning-note-bg/40 p-4"
      onSubmit={(e) => {
        e.preventDefault();
        if (valido) guardar();
      }}
      data-print="hidden"
    >
      {aviso && (
        <AvisoEnLinea tono="error" titulo="No se pudo actualizar">
          {aviso}
        </AvisoEnLinea>
      )}
      <FormSeccion numero={1} titulo="Datos de la operación">
        <FormCampo span={6}>
          <Field label="Cliente" required>
            {(control) => (
              <ClienteTesoreriaSelector
                id={control.id}
                value={clienteId}
                etiquetaInicial={d.clienteRazonSocial ?? undefined}
                disabled={esNc}
                onChange={(c) => setClienteId(c?.id ?? '')}
              />
            )}
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field
            label={`Importe (${d.moneda})`}
            required
            hint={`Disponible: ${formatoMonto(d.disponibleOrigen, d.moneda)}`}
          >
            <Input
              type="number"
              step="0.01"
              min="0"
              className="tabular-nums"
              value={importe}
              onChange={(e) => setImporte(e.target.value)}
            />
          </Field>
        </FormCampo>
        <FormCampo span={12}>
          <Field label="Motivo" required>
            <Textarea
              rows={2}
              maxLength={400}
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
            />
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Banco destino" required={!esNc} optional={esNc}>
            <Input maxLength={80} value={banco} onChange={(e) => setBanco(e.target.value)} />
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field
            label="CLABE destino"
            required={!esNc}
            optional={esNc}
            hint={
              clabeEnmascarada ? 'Por seguridad, vuelve a capturar la CLABE completa.' : undefined
            }
          >
            <Input
              inputMode="numeric"
              maxLength={18}
              className="font-mono"
              value={clabe}
              onChange={(e) => setClabe(e.target.value)}
            />
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Titular de la cuenta" required={!esNc} optional={esNc}>
            <Input maxLength={200} value={titular} onChange={(e) => setTitular(e.target.value)} />
          </Field>
        </FormCampo>
      </FormSeccion>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onCerrar} disabled={editar.isPending}>
          Cancelar
        </Button>
        <Button
          type="submit"
          disabled={!valido || editar.isPending}
          title={
            !valido ? 'Completa los campos obligatorios y revisa la cuenta destino.' : undefined
          }
        >
          {editar.isPending ? 'Guardando…' : 'Guardar'}
        </Button>
      </div>
    </form>
  );
}
