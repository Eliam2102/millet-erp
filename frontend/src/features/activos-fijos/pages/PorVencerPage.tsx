import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { PageHeader } from '@/components/erp/PageHeader';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { formatDate } from '@/lib/datetime';
import { useConservacion, type Pagina, type Vencimiento } from '../api/conservacion';
export function PorVencerPage() {
  const [sucursal, setSucursal] = useState<string | null>(null);
  const [offset, setOffset] = useState(0);
  const [misAvisos, setMisAvisos] = useState(false);
  const query = useConservacion<Pagina<Vencimiento> & { hoy: string }>(
    `${misAvisos ? 'avisos' : 'por-vencer'}?offset=${offset}${sucursal ? `&sucursalId=${sucursal}` : ''}`,
  );
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm">
      <PageHeader
        titulo="Por vencer"
        descripcion="Garantías, seguros, arrendamientos y próximos mantenimientos por sucursal."
      />
      <ListadoModulo
        filtros={
          <div className="flex items-center gap-3 p-4">
            <label className="flex items-center gap-2 text-xs">
              <input
                type="checkbox"
                checked={misAvisos}
                onChange={(e) => {
                  setMisAvisos(e.target.checked);
                  setOffset(0);
                }}
              />
              Mis avisos recibidos
            </label>
            <SucursalSelector
              value={sucursal}
              onChange={(id) => {
                setSucursal(id);
                setOffset(0);
              }}
            />
          </div>
        }
        pie={
          <div className="flex items-center justify-between p-3">
            <span>{query.data?.total ?? 0} vencimientos</span>
            <div className="flex gap-2">
              <Button
                variant="outline"
                disabled={offset === 0}
                title="Página anterior"
                onClick={() => setOffset(offset - 10)}
              >
                Anterior
              </Button>
              <Button
                variant="outline"
                disabled={offset + 10 >= (query.data?.total ?? 0)}
                title="Página siguiente"
                onClick={() => setOffset(offset + 10)}
              >
                Siguiente
              </Button>
            </div>
          </div>
        }
      >
        {query.isPending ? (
          <p className="p-4">Cargando vencimientos…</p>
        ) : query.error ? (
          <p role="alert" className="p-4 text-danger-fg">
            {query.error.message}
          </p>
        ) : query.data?.items.length === 0 ? (
          <p className="p-4">No hay vencimientos con este filtro.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Activo</TableHead>
                <TableHead>Documento o mantenimiento</TableHead>
                <TableHead>Vence</TableHead>
                <TableHead>Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {query.data?.items.map((v) => (
                <TableRow key={v.id}>
                  <TableCell>
                    <Link
                      className="font-mono text-brand"
                      to="/activos-fijos/activos/$id"
                      params={{ id: v.activoId }}
                    >
                      {v.numero}
                    </Link>
                    <p>{v.descripcion}</p>
                  </TableCell>
                  <TableCell>
                    {v.esMantenimiento ? 'Mantenimiento: ' : ''}
                    {v.referencia}
                  </TableCell>
                  <TableCell>{formatDate(v.vence)}</TableCell>
                  <TableCell>
                    <Badge variant={v.vence <= query.data.hoy ? 'danger' : 'warning'}>
                      {v.vence <= query.data.hoy ? 'Vencido' : 'Por vencer'}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </ListadoModulo>
    </main>
  );
}
