import React, { useState } from 'react';
import { createRoot } from 'react-dom/client';
import './banco.css';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { MontoMovimiento } from '@/components/erp/display/MontoMovimiento';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetBody, SheetFooter } from '@/components/ui/sheet';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogBody, DialogFooter } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

function Campo({ l }: { l: string }) { return <div className="flex flex-col gap-1.5"><Label className="text-xs">{l}</Label><Input defaultValue="" placeholder={l} /></div>; }

function App() {
  const vista = new URLSearchParams(location.search).get('v') ?? 'resumen';
  const [open, setOpen] = useState(true);
  return <div className="min-h-screen bg-surface-page p-8 text-sm text-ink">
    {vista === 'resumen' && <div className="space-y-8">
      <div className="grid grid-cols-4 gap-3">
        <KpiCard etiqueta="Por autorizar" valor="12" subdato="3 vencen hoy" />
        <KpiCard etiqueta="Rechazadas" valor="2" />
        <KpiCard etiqueta="Vencen esta semana" valor="7" prioridad="atencion" />
        <KpiCard etiqueta="Pagado del mes" valor="$412,875.40" />
      </div>
      <div className="rounded-lg bg-surface-card p-6 shadow-card">
        <h3 className="mb-4 font-semibold">Factura de proveedor · muestra ficticia</h3>
        <ResumenImportes moneda="MXN" subtotal={3150} descuento={0} ivaTrasladado={504} ivaRetenido={0} isrRetenido={0} total={3654}
          estadoPago={[{etiqueta:'Anticipo aplicado',monto:0},{etiqueta:'Pagado',monto:0},{etiqueta:'Saldo pendiente',monto:3654,destacado:true},{etiqueta:'Elegible para pago',monto:3654,destacado:true}]}
          acciones={<><Button variant="ghost">Cancelar</Button><Button>Emitir y timbrar</Button></>} />
      </div>
      <div className="flex gap-6"><MontoMovimiento monto={1000} tipo="entrada" moneda="MXN" /><MontoMovimiento monto={500} tipo="salida" moneda="MXN" /></div>
    </div>}
    {vista === 'sheet' && <Sheet open={open} onOpenChange={setOpen}><SheetContent size="lg">
      <SheetHeader><SheetTitle>Capturar factura desde OC</SheetTitle><SheetDescription>Concilia un CFDI recibido con una OC autorizada.</SheetDescription></SheetHeader>
      <SheetBody className="space-y-6">
        <FormSeccion numero={1} titulo="Comprobante fiscal">
          <FormCampo span={6}><Campo l="Orden de compra *" /></FormCampo><FormCampo span={6}><Campo l="Proveedor" /></FormCampo>
          <FormCampo span={6}><Campo l="Serie" /></FormCampo><FormCampo span={6}><Campo l="Folio" /></FormCampo>
          <FormCampo span={3}><Campo l="Fecha documento *" /></FormCampo><FormCampo span={3}><Campo l="Fecha contabilización *" /></FormCampo><FormCampo span={3}><Campo l="Fecha vencimiento *" /></FormCampo><FormCampo span={3}><Campo l="Tipo de cambio" /></FormCampo>
        </FormSeccion>
        <FormSeccion numero={2} titulo="Importes del CFDI">
          <FormCampo span={3}><Campo l="Subtotal *" /></FormCampo><FormCampo span={3}><Campo l="Descuento" /></FormCampo><FormCampo span={3}><Campo l="IVA trasladado" /></FormCampo><FormCampo span={3}><Campo l="Retenciones" /></FormCampo>
        </FormSeccion>
        <ResumenImportes moneda="MXN" subtotal={3150} descuento={0} ivaTrasladado={504} total={3654} />
      </SheetBody>
      <SheetFooter><Button variant="ghost">Cancelar</Button><Button>Capturar factura</Button></SheetFooter>
    </SheetContent></Sheet>}
    {vista === 'dialog' && <Dialog open={open} onOpenChange={setOpen}><DialogContent size="md">
      <DialogHeader><DialogTitle>Cobrar DEMO-C5</DialogTitle></DialogHeader>
      <DialogBody><FormGrid><FormCampo span={6}><Campo l="Forma de pago *" /></FormCampo><FormCampo span={6}><Campo l="Importe *" /></FormCampo><FormCampo span={6}><Campo l="Canal" /></FormCampo><FormCampo span={6}><Campo l="Referencia" /></FormCampo></FormGrid></DialogBody>
      <DialogFooter><Button variant="ghost">Cancelar</Button><Button>Cobrar</Button></DialogFooter>
    </DialogContent></Dialog>}
  </div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App /></React.StrictMode>);
