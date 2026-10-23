import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { SheetBody } from '@/components/ui/sheet';
import { useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Field } from '@/components/erp';
import { Textarea } from '@/components/ui/textarea';
import { hoyLocalISO } from '@/lib/datetime';
import { useCondicionesPago } from '@/features/catalogos/api';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { TipoRespuestaProveedorOc, type OrdenCompraDetalleResponse } from '../api/types';
import {
  RegistrarRespuestaProveedorSchema,
  type RegistrarRespuestaProveedorValues,
} from '../schemas/respuesta-proveedor';

export interface RegistrarRespuestaProveedorSheetProps {
  oc: OrdenCompraDetalleResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  isPending?: boolean;
  /** Rechazo del servidor (p. ej. fecha anterior a la OC), mostrado dentro del formulario. */
  errorServidor?: string | null;
  onSubmit: (values: RegistrarRespuestaProveedorValues) => Promise<void>;
}

/** F1-COM-11: sheet «Registrar respuesta del proveedor». Patrón remount on open. */
export function RegistrarRespuestaProveedorSheet({
  oc,
  open,
  onOpenChange,
  isPending,
  errorServidor,
  onSubmit,
}: RegistrarRespuestaProveedorSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent size="lg" side="right" className="">
        <SheetHeader>
          <SheetTitle>Registrar respuesta del proveedor</SheetTitle>
          <SheetDescription>
            Orden <span className="font-mono">{oc.folio}</span> ·{' '}
            {oc.proveedorRazonSocial ?? oc.proveedorClave ?? 'Proveedor'}
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          {open && (
            <RespuestaForm
              oc={oc}
              isPending={isPending}
              errorServidor={errorServidor}
              onSubmit={onSubmit}
              onClose={() => onOpenChange(false)}
            />
          )}
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}

interface FilaParcialidad {
  key: number;
  cantidad: string;
  fecha: string;
}

function RespuestaForm({
  oc,
  isPending,
  errorServidor,
  onSubmit,
  onClose,
}: {
  oc: OrdenCompraDetalleResponse;
  isPending?: boolean;
  errorServidor?: string | null;
  onSubmit: (values: RegistrarRespuestaProveedorValues) => Promise<void>;
  onClose: () => void;
}) {
  const hoy = hoyLocalISO();
  const condicion = useCondicionesPago().data?.find((c) => c.id === oc.condicionesPagoId);
  const pendientes = oc.lineas
    .map((l) => ({ linea: l, pendiente: l.cantidad - l.cantidadRecibida }))
    .filter((x) => x.pendiente > 0);
  const fechaEsperada = oc.fechaEntregaEsperada?.slice(0, 10) ?? '';

  const [tipo, setTipo] = useState<TipoRespuestaProveedorOc | null>(null);
  const [fechaRespuesta, setFechaRespuesta] = useState('');
  const [contacto, setContacto] = useState('');
  const [evidencia, setEvidencia] = useState('');
  const [archivo, setArchivo] = useState<File>();
  const [texto, setTexto] = useState('');
  const [siguienteKey, setSiguienteKey] = useState(pendientes.length);
  const [filas, setFilas] = useState<Record<string, FilaParcialidad[]>>({});
  const [error, setError] = useState<string | null>(null);
  const confirma = tipo === TipoRespuestaProveedorOc.Confirmacion;

  const cambiarFila = (lineaId: string, key: number, parche: Partial<FilaParcialidad>) =>
    setFilas((f) => ({
      ...f,
      [lineaId]: (f[lineaId] ?? []).map((r) => (r.key === key ? { ...r, ...parche } : r)),
    }));
  const agregarFila = (lineaId: string) => {
    setFilas((f) => ({
      ...f,
      [lineaId]: [...(f[lineaId] ?? []), { key: siguienteKey, cantidad: '', fecha: fechaEsperada }],
    }));
    setSiguienteKey((k) => k + 1);
  };
  const quitarFila = (lineaId: string, key: number) =>
    setFilas((f) => ({ ...f, [lineaId]: (f[lineaId] ?? []).filter((r) => r.key !== key) }));

  async function enviar(e: React.FormEvent) {
    e.preventDefault();
    const compromisos = confirma
      ? pendientes.flatMap(({ linea }) =>
          (filas[linea.id] ?? [])
            .map((r) => ({
              lineaOrdenCompraId: linea.id,
              cantidad: Number(r.cantidad),
              fechaPrometida: r.fecha,
            })),
        )
      : [];
    const result = RegistrarRespuestaProveedorSchema.safeParse({
      tipo,
      fechaRespuesta: fechaRespuesta || undefined,
      archivo,
      evidencia,
      contactoProveedor: contacto.trim() || undefined,
      motivo: texto.trim() || undefined,
      compromisos,
    });
    if (!result.success) {
      setError(result.error.issues[0]?.message ?? 'Revisa los datos capturados.');
      return;
    }
    setError(null);
    await onSubmit(result.data);
  }

  return (
    <form onSubmit={enviar} className="space-y-5" noValidate>
      <FormSeccion titulo="Condiciones pactadas">
        <FormCampo span={6}>
          <p className="text-xs text-ink-muted">Condición de pago</p>
          <p className="text-sm">
            {condicion
              ? condicion.diasCredito > 0
                ? `${condicion.nombre} (${condicion.diasCredito} días)`
                : condicion.nombre
              : '—'}
          </p>
        </FormCampo>
        <FormCampo span={6}>
          <p className="text-xs text-ink-muted">Fecha de entrega esperada</p>
          <p className="text-sm tabular-nums">
            {oc.fechaEntregaEsperada ? (
              <DateTimeDisplay value={oc.fechaEntregaEsperada} variant="corta" />
            ) : (
              '—'
            )}
          </p>
        </FormCampo>
      </FormSeccion>
      <FormSeccion titulo="1. Evidencia">
        <FormCampo span={12}>
          <Field label="Archivo de evidencia" required>
            <Input type="file" onChange={(e) => setArchivo(e.target.files?.[0])} />
          </Field>
          <p className="text-xs text-ink-muted">Adjunta la respuesta del proveedor. Los demás datos son opcionales.</p>
        </FormCampo>
      </FormSeccion>
      <FormSeccion titulo="2. Datos opcionales de la respuesta">
        <FormCampo span={12}>
          <div className="space-y-2" role="radiogroup" aria-label="Respuesta del proveedor">
            <div className="flex items-center gap-2">
              <input id="resp-tipo-sin-clasificar" type="radio" name="tipo-respuesta" checked={tipo === null}
                onChange={() => setTipo(null)} className="accent-brand" />
              <Label htmlFor="resp-tipo-sin-clasificar">Sin clasificar</Label>
            </div>
            <div className="flex items-center gap-2">
              <input
                id="resp-tipo-confirma"
                type="radio"
                name="tipo-respuesta"
                checked={confirma}
                onChange={() => setTipo(TipoRespuestaProveedorOc.Confirmacion)}
                className="accent-brand"
              />
              <Label htmlFor="resp-tipo-confirma">El proveedor confirma</Label>
            </div>
            <div className="flex items-center gap-2">
              <input
                id="resp-tipo-rechaza"
                type="radio"
                name="tipo-respuesta"
                checked={tipo === TipoRespuestaProveedorOc.Rechazo}
                onChange={() => setTipo(TipoRespuestaProveedorOc.Rechazo)}
                className="accent-brand"
              />
              <Label htmlFor="resp-tipo-rechaza">El proveedor rechaza</Label>
            </div>
          </div>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Fecha de respuesta" optional>
            <Input
              type="date"
              value={fechaRespuesta}
              max={hoy}
              onChange={(e) => setFechaRespuesta(e.target.value)}
            />
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Contacto del proveedor" optional>
            <Input value={contacto} maxLength={120} onChange={(e) => setContacto(e.target.value)} />
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Referencia de la evidencia" optional>
            <Input
              value={evidencia}
              maxLength={300}
              placeholder="Correo, folio o documento del proveedor"
              onChange={(e) => setEvidencia(e.target.value)}
            />
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field
            label={tipo === TipoRespuestaProveedorOc.Rechazo ? 'Motivo del rechazo' : 'Comentario'}
            required={tipo === TipoRespuestaProveedorOc.Rechazo}
            optional={tipo !== TipoRespuestaProveedorOc.Rechazo}
          >
            <Textarea
              value={texto}
              maxLength={500}
              rows={3}
              onChange={(e) => setTexto(e.target.value)}
            />
          </Field>
        </FormCampo>
      </FormSeccion>

      {confirma && (
        <FormSeccion titulo="3. Fechas prometidas (opcionales)">
          <FormCampo span={12}>
            {pendientes.map(({ linea, pendiente }) => {
              const lineaFilas = filas[linea.id] ?? [];
              const suma = lineaFilas.reduce((acc, r) => acc + (Number(r.cantidad) || 0), 0);
              return (
                <div key={linea.id} className="space-y-2 rounded-md border border-line-divider p-3">
                  <p className="text-sm text-ink">
                    Partida {linea.posicion} · {linea.articuloNombre ?? linea.articuloClave} ·
                    pendiente <span className="tabular-nums">{pendiente}</span> {linea.unidadMedida}
                  </p>
                  {lineaFilas.map((r, i) => (
                    <div key={r.key} className="flex items-end gap-2">
                      <div className="space-y-1">
                        <Label htmlFor={`resp-cant-${r.key}`}>Cantidad</Label>
                        <Input
                          id={`resp-cant-${r.key}`}
                          type="number"
                          aria-label={`Cantidad (partida ${linea.posicion}, parcialidad ${i + 1})`}
                          step="any"
                          min="0"
                          className="tabular-nums"
                          value={r.cantidad}
                          onChange={(e) =>
                            cambiarFila(linea.id, r.key, { cantidad: e.target.value })
                          }
                        />
                      </div>
                      <div className="space-y-1">
                        <Label htmlFor={`resp-fecha-${r.key}`}>Fecha prometida</Label>
                        <Input
                          id={`resp-fecha-${r.key}`}
                          type="date"
                          aria-label={`Fecha prometida (partida ${linea.posicion}, parcialidad ${i + 1})`}
                          value={r.fecha}
                          onChange={(e) => cambiarFila(linea.id, r.key, { fecha: e.target.value })}
                        />
                      </div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label="Quitar parcialidad"
                        onClick={() => quitarFila(linea.id, r.key)}
                      >
                        <Trash2 className="size-4" aria-hidden="true" />
                      </Button>
                    </div>
                  ))}
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    onClick={() => agregarFila(linea.id)}
                  >
                    <Plus className="size-4" aria-hidden="true" /> Agregar parcialidad
                  </Button>
                  {lineaFilas.length > 0 && suma !== pendiente && (
                    <p role="note" className="text-xs text-warning-note-fg">
                      Confirmado {suma} de {pendiente} {linea.unidadMedida}: la orden no cambia;
                      quedará marcada con diferencias para Compras.
                    </p>
                  )}
                </div>
              );
            })}
          </FormCampo>
        </FormSeccion>
      )}

      {(error ?? errorServidor) && (
        <p role="alert" className="text-sm text-danger-fg">
          {error ?? errorServidor}
        </p>
      )}

      <div className="sticky bottom-0 flex justify-between gap-2 border-t border-line-row bg-surface-card p-4">
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" disabled={isPending}>
          Registrar respuesta
        </Button>
      </div>
    </form>
  );
}
