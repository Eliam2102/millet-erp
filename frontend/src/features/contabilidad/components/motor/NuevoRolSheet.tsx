import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useSucursalesContables } from '../../api/dimensiones';
import { useCrearRol } from '../../api/motor';
import type { CrearRolRequest } from '../../api/motor-types';
import { SELECT_CLASS } from '../../lib/estilos';
import { CODIGO_ROL_MAXIMO, CODIGO_ROL_PATRON, mensajeMotor } from '../../lib/motor';
import { CuentaSelector, type CuentaOpcion } from '../dimensiones/CuentaSelector';

const MONEDAS = ['MXN', 'USD', 'EUR'] as const;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Alta de un rol de cuenta (C1.4). Sucursal y moneda son filtros opcionales: un rol con filtro gana sobre el general
 * del mismo código. Sin cuenta, los eventos que lo usen quedan rechazados («Rol sin cuenta») hasta asignarla.
 */
export function NuevoRolSheet({ open, onOpenChange }: Props) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent size="form" side="right" data-print="hidden">
        {open && <Formulario onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Formulario({ onClose }: { onClose: () => void }) {
  const crear = useCrearRol();
  const keyFor = useBodyScopedIdempotencyKey();
  // Los nombres de sucursal salen del catálogo de dimensiones: solo se consultan con su permiso (sin él, «Sucursal específica»).
  const puedeLeerSucursales = useHasPermission(PermisosCanonicos.ContabilidadDimensionesLeer);
  const sucursales = useSucursalesContables({ enabled: puedeLeerSucursales });
  const [codigo, setCodigo] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(null);
  const [sucursalId, setSucursalId] = useState('');
  const [moneda, setMoneda] = useState('');
  const [vigenteDesde, setVigenteDesde] = useState('');
  const [error, setError] = useState('');

  const codigoLimpio = codigo.trim();
  const codigoInvalido = codigoLimpio.length > 0 && !CODIGO_ROL_PATRON.test(codigoLimpio);
  const deshabilitado = !codigoLimpio
    ? 'Escribe el código del rol'
    : codigoInvalido
      ? 'El código va en mayúsculas, dígitos y guion bajo'
      : !descripcion.trim()
        ? 'Describe el rol'
        : undefined;

  function guardar() {
    if (deshabilitado) return;
    setError('');
    const body: CrearRolRequest = {
      codigo: codigoLimpio,
      descripcion: descripcion.trim(),
      cuentaId: cuenta?.id ?? null,
      sucursalId: sucursalId || null,
      moneda: moneda || null,
      vigenteDesde: vigenteDesde || null,
    };
    crear.mutate(
      { body, idempotencyKey: keyFor(body) },
      {
        onSuccess: (r) => {
          toast.success(`Rol ${r.codigo} creado.`);
          onClose();
        },
        onError: (e) => setError(mensajeMotor(e)),
      },
    );
  }

  return (
    <>
      <SheetHeader>
        <SheetTitle>Nuevo rol de cuenta</SheetTitle>
        <SheetDescription className="text-xs text-ink-muted">
          Las plantillas del motor usan roles, no cuentas: aquí se decide a qué cuenta va cada rol.
        </SheetDescription>
      </SheetHeader>
      <SheetBody className="grid grid-cols-12 gap-4">
        <div className="col-span-12 flex flex-col gap-1.5 sm:col-span-5">
          <Label htmlFor="rol-codigo">
            Código<span aria-hidden="true"> *</span>
          </Label>
          <Input
            id="rol-codigo"
            className="font-mono"
            maxLength={CODIGO_ROL_MAXIMO}
            value={codigo}
            aria-invalid={codigoInvalido || undefined}
            aria-describedby="rol-codigo-ayuda"
            onChange={(e) => setCodigo(e.target.value.toUpperCase())}
          />
          <span id="rol-codigo-ayuda" className={codigoInvalido ? 'text-xs text-danger-fg' : 'text-xs text-ink-muted'}>
            Mayúsculas, dígitos y guion bajo (p. ej. IVA_ACREDITABLE).
          </span>
        </div>
        <div className="col-span-12 flex flex-col gap-1.5 sm:col-span-7">
          <Label htmlFor="rol-descripcion">
            Descripción<span aria-hidden="true"> *</span>
          </Label>
          <Input id="rol-descripcion" value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
        </div>
        <div className="col-span-12 flex flex-col gap-1.5">
          <Label htmlFor="rol-cuenta">Cuenta</Label>
          <CuentaSelector
            id="rol-cuenta"
            value={cuenta}
            onChange={setCuenta}
            soloAfectables
            vacio="Sin cuenta por ahora"
          />
          <span className="text-xs text-ink-muted">
            Sin cuenta, los eventos que usen el rol quedan rechazados hasta asignarla.
          </span>
        </div>
        <div className="col-span-12 flex flex-col gap-1.5 sm:col-span-6">
          <Label htmlFor="rol-sucursal">Sucursal</Label>
          <select
            id="rol-sucursal"
            className={SELECT_CLASS}
            value={sucursalId}
            onChange={(e) => setSucursalId(e.target.value)}
          >
            <option value="">Todas las sucursales</option>
            {(sucursales.data ?? [])
              .filter((s) => s.activa)
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {s.clave} · {s.nombre}
                </option>
              ))}
          </select>
        </div>
        <div className="col-span-6 flex flex-col gap-1.5 sm:col-span-3">
          <Label htmlFor="rol-moneda">Moneda</Label>
          <select id="rol-moneda" className={SELECT_CLASS} value={moneda} onChange={(e) => setMoneda(e.target.value)}>
            <option value="">Todas</option>
            {MONEDAS.map((m) => (
              <option key={m} value={m}>
                {m}
              </option>
            ))}
          </select>
        </div>
        <div className="col-span-6 flex flex-col gap-1.5 sm:col-span-3">
          <Label htmlFor="rol-vigente">Vigente desde</Label>
          <Input id="rol-vigente" type="date" value={vigenteDesde} onChange={(e) => setVigenteDesde(e.target.value)} />
        </div>
        <p className="col-span-12 text-xs text-ink-muted">
          Un rol con sucursal o moneda gana sobre el general del mismo código. Sin fecha, aplica desde hoy.
        </p>
        {error && (
          <p role="alert" className="col-span-12 rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
            {error}
          </p>
        )}
      </SheetBody>
      <SheetFooter>
        <Button variant="ghost" onClick={onClose} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button onClick={guardar} disabled={!!deshabilitado || crear.isPending} title={deshabilitado}>
          {crear.isPending ? 'Guardando…' : 'Crear rol'}
        </Button>
      </SheetFooter>
    </>
  );
}
