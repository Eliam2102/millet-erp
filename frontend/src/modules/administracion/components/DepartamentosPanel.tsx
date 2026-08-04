import { useState } from 'react';
import { Pencil, Plus } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  EstatusCatalogo,
  type DepartamentoResponse,
} from '@/modules/administracion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { DepartamentoInlineForm } from '@/modules/administracion/components/DepartamentoInlineForm';

/**
 * Panel "Departamentos" del detalle de empresa. Espejo de
 * <c>SucursalesPanel</c> sin "Desactivar" — el backend MVP no expone
 * el endpoint; solo POST y PATCH. Si más adelante se agrega, se
 * espeja la pieza aquí.
 */
export interface DepartamentosPanelProps {
  empresaId: string;
  departamentos: readonly DepartamentoResponse[];
}

export function DepartamentosPanel({
  empresaId,
  departamentos,
}: DepartamentosPanelProps) {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);

  const canGestionar = useHasPermission(
    PermisosCanonicos.AdminDepartamentosGestionar,
  );

  return (
    <section className="space-y-3">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-base font-semibold">Departamentos</h3>
          <p className="text-xs text-muted-foreground">
            Catálogo organizacional compartido. Total: {departamentos.length}.
          </p>
        </div>
        {canGestionar && !agregando && (
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              setAgregando(true);
              setEditandoId(null);
            }}
          >
            <Plus className="mr-1 h-4 w-4" />
            Agregar departamento
          </Button>
        )}
      </header>

      {agregando && canGestionar && (
        <DepartamentoInlineForm
          empresaId={empresaId}
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {departamentos.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay departamentos registrados.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {departamentos.map((d) => {
            const editando = editandoId === d.id;
            const activo = d.estatus === EstatusCatalogo.Activo;
            return (
              <li key={d.id} className="px-3 py-2">
                {editando && canGestionar ? (
                  <DepartamentoInlineForm
                    empresaId={empresaId}
                    departamento={d}
                    onCancel={() => setEditandoId(null)}
                    onSaved={() => setEditandoId(null)}
                  />
                ) : (
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="font-mono text-sm font-semibold">
                      {d.clave}
                    </span>
                    <span className="flex-1 truncate text-sm">{d.nombre}</span>
                    {activo ? (
                      <Badge variant="secondary">Activo</Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        Inactivo
                      </Badge>
                    )}
                    {canGestionar && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                          setEditandoId(d.id);
                          setAgregando(false);
                        }}
                        aria-label={`Editar departamento ${d.clave}`}
                      >
                        <Pencil className="h-3.5 w-3.5" />
                      </Button>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
