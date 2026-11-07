import { errorUsuario } from '@/lib/error-usuario';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { ArticuloSelector, TextAreaField } from '@/components/erp';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { Field } from '@/features/almacen/components/internal/Field';
import { useCapturarRotura, useDimensionesRotura, useMotivosRotura } from '@/features/almacen/api/useSalidasRotura';
import { codigoSoporte, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * ALM-03: captura de la salida por rotura. Solo queda pendiente: el saldo no se mueve hasta que otro usuario con
 * facultad la autorice.
 */
export function CapturarRoturaSheet({ open, onOpenChange }: Props) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Salida por rotura</SheetTitle>
          <SheetDescription>
            Material que se rompió en el almacén. Queda pendiente de autorizar: el saldo baja, a costo promedio, hasta que un
            Jefe o Gerente de almacén distinto de quien captura la autorice.
          </SheetDescription>
        </SheetHeader>
        {open && <Formulario onListo={() => onOpenChange(false)} onCancel={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Formulario({ onListo, onCancel }: { onListo: () => void; onCancel: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const capturar = useCapturarRotura();
  const motivos = useMotivosRotura();
  const [articuloId, setArticuloId] = useState<string | null>(null);
  const [ubicacionId, setUbicacionId] = useState<string | null>(null);
  const [cantidad, setCantidad] = useState('');
  const [fecha, setFecha] = useState(hoyLocalISO());
  const [motivoCodigo, setMotivoCodigo] = useState('');
  const [comentario, setComentario] = useState<string | null>(null);
  const [areaClave, setAreaClave] = useState('');
  const [equipoClave, setEquipoClave] = useState('');
  // 86e3zf04y: la cuenta de merma puede exigir área o equipo (regla C-03); sin el dato la póliza queda en Rechazos.
  const dimensiones = useDimensionesRotura(ubicacionId, fecha);
  const exigeArea = dimensiones.data?.exigeArea ?? false;
  const exigeEquipo = dimensiones.data?.exigeEquipo ?? false;
  const [errores, setErrores] = useState<Record<string, string>>({});

  function enviar(e: React.FormEvent) {
    e.preventDefault();
    const n = Number(cantidad);
    const faltan: Record<string, string> = {};
    if (!articuloId) faltan.articulo = 'Elige el artículo.';
    if (!ubicacionId) faltan.ubicacion = 'Elige la ubicación de donde sale.';
    if (!(n > 0)) faltan.cantidad = 'La cantidad debe ser mayor que cero.';
    if (!motivoCodigo) faltan.motivo = 'Elige el motivo.';
    if (motivoCodigo === 'otro' && !comentario?.trim()) faltan.comentario = 'Con «Otro» describe qué pasó.';
    if (exigeArea && !areaClave.trim() && !equipoClave.trim())
      faltan.area = `La cuenta ${dimensiones.data?.cuentaCodigo ?? 'de merma'} pide el área.`;
    if (exigeEquipo && !equipoClave.trim())
      faltan.equipo = `La cuenta ${dimensiones.data?.cuentaCodigo ?? 'de merma'} pide el equipo.`;
    setErrores(faltan);
    if (Object.keys(faltan).length > 0) return;
    capturar.mutate(
      {
        command: { ubicacionId: ubicacionId!, articuloId: articuloId!, cantidad: n, fecha, motivoCodigo, comentario: comentario?.trim() || null,
          areaClave: areaClave.trim() || null,
          equipoClave: equipoClave.trim() || null,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Rotura capturada: queda pendiente de autorizar');
          onListo();
        },
        onError: (error) =>
          toast.error(errorUsuario(error, 'No se pudo capturar la rotura'), {
            description: esApiError(error) ? codigoSoporte(error) : undefined,
          }),
      },
    );
  }

  return (
    <>
      <SheetBody>
        <form id="rotura-form" onSubmit={enviar} className="space-y-4" noValidate>
          <Field label="Artículo" required error={errores.articulo}>
            <ArticuloSelector
              value={articuloId}
              onChange={(id) => {
                setArticuloId(id);
                setUbicacionId(null);
              }}
            />
          </Field>
          <Field label="Ubicación (rack)" required error={errores.ubicacion}>
            <UbicacionBinSelector
              modo="saldo"
              articuloId={articuloId}
              subAlmacenId={null}
              requiereSubAlmacen={false}
              value={ubicacionId}
              onChange={setUbicacionId}
            />
          </Field>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Cantidad rota" required error={errores.cantidad}>
              <Input type="number" min="0" step="any" value={cantidad} onChange={(e) => setCantidad(e.target.value)} />
            </Field>
            <Field label="Fecha" required>
              <Input type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
            </Field>
          </div>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Área (clave)" required={exigeArea && !equipoClave.trim()} error={errores.area}>
              <Input
                aria-label="Área (clave)"
                value={areaClave}
                maxLength={40}
                onChange={(e) => setAreaClave(e.target.value)}
              />
            </Field>
            <Field label="Equipo (clave)" required={exigeEquipo} error={errores.equipo}>
              <Input
                aria-label="Equipo (clave)"
                value={equipoClave}
                maxLength={40}
                onChange={(e) => setEquipoClave(e.target.value)}
              />
            </Field>
          </div>
          {(exigeArea || exigeEquipo) && (
            <p className="text-xs text-ink-muted">
              La cuenta {dimensiones.data?.cuentaCodigo} de la póliza de merma pide {exigeEquipo ? 'área y equipo' : 'el área'} (regla
              C-03). Con el equipo, el área se toma de él.
            </p>
          )}
          <Field label="Motivo" required error={errores.motivo}>
            <Select value={motivoCodigo} onValueChange={setMotivoCodigo}>
              <SelectTrigger aria-label="Motivo">
                <SelectValue placeholder="Elige el motivo" />
              </SelectTrigger>
              <SelectContent>
                {(motivos.data ?? []).map((m) => (
                  <SelectItem key={m.codigo} value={m.codigo}>
                    {m.texto}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Field label="Comentario" required={motivoCodigo === 'otro'} error={errores.comentario}>
            <TextAreaField value={comentario} onChange={setComentario} maxLength={500} minRows={2} />
          </Field>
        </form>
      </SheetBody>
      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={capturar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="rotura-form" disabled={capturar.isPending}>
          {capturar.isPending ? 'Guardando…' : 'Capturar rotura'}
        </Button>
      </SheetFooter>
    </>
  );
}
