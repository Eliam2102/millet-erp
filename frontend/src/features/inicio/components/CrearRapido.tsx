import { useState } from 'react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useNuevaRequisicion } from '@/features/compras/components/nueva-requisicion-context';
import { useNuevaOrdenCompra } from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import { useRegistrarGestion } from '@/features/cxc/components/registrar-gestion-context';
import { NuevaRecepcionSheet } from '@/features/almacen/components/NuevaRecepcionSheet';
import { CapturarFacturaSheet } from '@/features/cxp/components/CapturarFacturaSheet';
import { accionesInicio } from '../config';

export function CrearRapido() {
  const permisos = useAuthStore(s => s.permisos);
  const acciones = accionesInicio.filter(a => permisos.includes(a.permiso));
  return <Card className="min-w-0 bg-surface-card p-4 shadow-card-flat">
    <h2 className="text-lg font-semibold">Crear rápido</h2>
    <div className="mt-2 flex flex-wrap gap-2">{acciones.map(a => <AccionCrear key={a.id} accion={a} />)}</div>
    {!acciones.length && <p className="mt-2 text-xs text-ink-muted">Sin acciones de captura para tu perfil.</p>}
  </Card>;
}
function AccionCrear({ accion }: { accion: typeof accionesInicio[number] }) {
  const rq = useNuevaRequisicion();
  const oc = useNuevaOrdenCompra();
  const gestion = useRegistrarGestion();
  const [abierto, setAbierto] = useState(false);
  const abrir = () => {
    if (accion.id === 'rq') rq.abrir();
    else if (accion.id === 'oc') oc.abrir();
    else if (accion.id === 'gestion') gestion.abrir();
    else setAbierto(true);
  };
  return <>
    <Button variant="outline" size="sm" onClick={abrir}>＋ {accion.titulo}</Button>
    {abierto && accion.id === 'recepcion' && <NuevaRecepcionSheet open onOpenChange={setAbierto} />}
    {abierto && accion.id === 'factura' && <CapturarFacturaSheet open onOpenChange={setAbierto} />}
  </>;
}
