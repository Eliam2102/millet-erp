import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { ArbolDocumentos } from './ArbolDocumentos';
import type { TipoDocumentoTrazabilidad } from './types';
import { useArbolDocumentos } from '@/features/compras/ordenes/api/useArbolDocumentos';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export function TrazabilidadComprasDialog({ tipo, id }: { tipo: TipoDocumentoTrazabilidad; id: string }) {
  const [open, setOpen] = useState(false);
  const permitido = useHasPermission(PermisosCanonicos.ComprasOrdenesLeer);
  const query = useArbolDocumentos(tipo, id, { enabled: permitido && open });
  if (!permitido) return null;
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <Button variant="outline" size="sm" onClick={() => setOpen(true)}>Ver trazabilidad</Button>
      <DialogContent className="max-h-[85vh] overflow-auto sm:max-w-5xl">
        <DialogHeader><DialogTitle>Trazabilidad documental</DialogTitle></DialogHeader>
        {query.isLoading && <TableSkeleton rows={3} />}
        {query.isError && <ErrorState title="No se pudo cargar la trazabilidad" onRetry={() => void query.refetch()} />}
        {query.data && <ArbolDocumentos raiz={query.data} tipoActual={tipo} idActual={id} />}
      </DialogContent>
    </Dialog>
  );
}
