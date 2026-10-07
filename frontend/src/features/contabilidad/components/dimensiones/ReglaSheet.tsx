import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { Textarea } from '@/components/ui/textarea';
import { hoyLocalISO } from '@/lib/datetime';
import { useCrearRegla, useEditarRegla, useTiposDocumento } from '../../api/dimensiones';
import type { Dimension, Regla, Requerimiento } from '../../api/dimensiones-types';
import { DIMENSIONES, REQUERIMIENTOS } from '../../api/dimensiones-types';
import { ETIQUETA_DIMENSION, ETIQUETA_REQUERIMIENTO, mensajeError } from '../../lib/dimensiones';
import { SELECT_CLASS } from '../../lib/estilos';
import { CuentaSelector, type CuentaOpcion } from './CuentaSelector';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** null = nueva regla; con valor = editar (solo reglas que aún no han validado movimientos). */
  regla: Regla | null;
}

/** Sheet "Nueva regla" / "Editar regla" (patrón P4). Las reglas se crean como de prueba hasta que Contabilidad confirme su política. */
export function ReglaSheet({ open, onOpenChange, regla }: Props) {
  const tipos = useTiposDocumento();
  const crear = useCrearRegla();
  const editar = useEditarRegla();
  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(
    regla ? { id: regla.cuentaId, codigo: regla.cuentaCodigo, nombre: regla.cuentaNombre } : null);
  const [tipoId, setTipoId] = useState(regla?.tipoDocumentoId ?? '');
  const [dimension, setDimension] = useState<Dimension>(regla?.dimension ?? 'Dim2');
  const [requerimiento, setRequerimiento] = useState<Requerimiento>(regla?.requerimiento ?? 'Obligatorio');
  const [desde, setDesde] = useState(regla?.vigenteDesde ?? hoyLocalISO());
  const [hasta, setHasta] = useState(regla?.vigenteHasta ?? '');
  const [esPrueba, setEsPrueba] = useState(regla?.esPrueba ?? true);
  const [nota, setNota] = useState(regla?.nota ?? '');
  const [error, setError] = useState('');

  const pendiente = crear.isPending || editar.isPending;
  const faltaCuenta = !regla && !cuenta;

  function guardar(e: React.FormEvent) {
    e.preventDefault();
    if (faltaCuenta) { setError('Elige la cuenta o rama a la que aplica la regla.'); return; }
    setError('');
    const comun = { requerimiento, vigenteDesde: desde, vigenteHasta: hasta || null, nota: nota.trim() || null };
    const ok = { onSuccess: () => onOpenChange(false), onError: (x: unknown) => setError(mensajeError(x)) };
    if (regla) editar.mutate({ regla, body: comun }, ok);
    else crear.mutate({ ...comun, cuentaId: cuenta!.id, tipoDocumentoId: tipoId || null, dimension, esPrueba }, ok);
  }

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-sheet" data-print="hidden">
        <SheetHeader>
          <SheetTitle>{regla ? 'Editar regla' : 'Nueva regla de dimensión'}</SheetTitle>
          <SheetDescription>
            {regla
              ? 'La regla aún no ha validado movimientos, así que se puede corregir. Una vez usada solo se cierra y se crea otra, para conservar la historia.'
              : 'Define si una dimensión es obligatoria, opcional o no aplica para una cuenta (o rama) en un tipo de documento.'}
          </SheetDescription>
        </SheetHeader>
        <form onSubmit={guardar} className="flex flex-col gap-4 px-4 pb-6">
          <fieldset className="grid grid-cols-2 gap-x-4 gap-y-3.5">
            <legend className="mb-2 text-sm font-semibold">1. Combinación</legend>
            <div className="col-span-2 flex flex-col gap-1.5">
              <Label htmlFor="regla-cuenta">Cuenta o rama</Label>
              <CuentaSelector id="regla-cuenta" value={cuenta} onChange={setCuenta} disabled={!!regla} />
              <span className="text-xs text-ink-muted">Una regla en una cuenta que acumula aplica a todas sus cuentas debajo, salvo que tengan una regla propia.</span>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="regla-tipo">Tipo de documento</Label>
              <select id="regla-tipo" className={SELECT_CLASS} value={tipoId} disabled={!!regla} onChange={(e) => setTipoId(e.target.value)}>
                <option value="">Todos los tipos</option>
                {(tipos.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.clave} — {t.nombre}</option>)}
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="regla-dimension">Dimensión</Label>
              <select id="regla-dimension" className={SELECT_CLASS} value={dimension} disabled={!!regla} onChange={(e) => setDimension(e.target.value as Dimension)}>
                {DIMENSIONES.map((d) => <option key={d} value={d}>{ETIQUETA_DIMENSION[d]}</option>)}
              </select>
            </div>
          </fieldset>
          <fieldset className="grid grid-cols-2 gap-x-4 gap-y-3.5">
            <legend className="mb-2 text-sm font-semibold">2. Requerimiento y vigencia</legend>
            <div className="col-span-2 flex flex-col gap-1.5">
              <Label htmlFor="regla-req">Requerimiento</Label>
              <select id="regla-req" className={SELECT_CLASS} value={requerimiento} onChange={(e) => setRequerimiento(e.target.value as Requerimiento)}>
                {REQUERIMIENTOS.map((r) => <option key={r} value={r}>{ETIQUETA_REQUERIMIENTO[r]}</option>)}
              </select>
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="regla-desde">Vigente desde</Label>
              <Input id="regla-desde" type="date" required min={regla ? undefined : hoyLocalISO()} value={desde} onChange={(e) => setDesde(e.target.value)} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="regla-hasta">Vigente hasta <span className="font-normal text-ink-muted">(opcional)</span></Label>
              <Input id="regla-hasta" type="date" min={desde} value={hasta} onChange={(e) => setHasta(e.target.value)} />
            </div>
            <div className="col-span-2 flex flex-col gap-1.5">
              <Label htmlFor="regla-nota">Nota <span className="font-normal text-ink-muted">(opcional)</span></Label>
              <Textarea id="regla-nota" rows={2} maxLength={500} className="resize-none" value={nota} onChange={(e) => setNota(e.target.value)} />
            </div>
            {!regla && (
              <div className="col-span-2 flex items-center gap-2">
                <Checkbox id="regla-prueba" checked={esPrueba} onCheckedChange={(v) => setEsPrueba(v === true)} />
                <Label htmlFor="regla-prueba" className="font-normal">Regla de prueba (no es política confirmada por Contabilidad)</Label>
              </div>
            )}
          </fieldset>
          {error && <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">{error}</p>}
          <div className="flex justify-between border-t border-line-divider pt-4">
            <Button type="button" variant="ghost" size="lg" onClick={() => onOpenChange(false)}>Cancelar</Button>
            <Button type="submit" size="lg" disabled={pendiente}>{pendiente ? 'Guardando…' : regla ? 'Guardar cambios' : 'Crear regla'}</Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}
