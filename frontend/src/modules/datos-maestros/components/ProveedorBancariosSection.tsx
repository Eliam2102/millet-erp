import { useState } from 'react';
import { Pencil } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  InlineFormShell,
  FieldInline,
} from '@/modules/catalogos/components/InlineFormShell';
import { esApiError } from '@/lib/api';
import {
  useActualizarProveedor,
  useProveedorDatosBancarios,
} from '@/modules/datos-maestros/api';
import type { ProveedorDatosBancarios } from '@/modules/datos-maestros/api/types';
import { useHasAllPermissions, useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Sección "Datos bancarios" del detalle de proveedor (F1-ADM-05).
 * Visible solo con <c>datos_maestros.proveedores.bancarios-ver</c>;
 * el botón Editar (inline form ámbar, patrón
 * <c>frontend/docs/patrones-compras.md</c> §6.3) requiere además
 * <c>datos_maestros.proveedores.bancarios-editar</c>.
 *
 * <para>La CLABE llega enmascarada del backend (<c>clabeCompleta =
 * false</c>) salvo el permiso PII de Tesorería. El form de edición
 * NUNCA pre-llena la CLABE completa: campo vacío = no cambiar, y el
 * checkbox "Quitar la CLABE registrada" es la única forma de
 * limpiarla — mismo patrón que
 * <c>CuentaBancariaSheet</c> (Tesorería).</para>
 */
export interface ProveedorBancariosSectionProps {
  proveedorId: string;
}

export function ProveedorBancariosSection({
  proveedorId,
}: ProveedorBancariosSectionProps) {
  const canVer = useHasPermission(
    PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
  );
  // El guardado va por PATCH /catalogos/proveedores/{id}: la API exige catalogos.administrar.
  const canEditar = useHasAllPermissions([
    PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  ]);
  const [editando, setEditando] = useState(false);
  const query = useProveedorDatosBancarios(canVer ? proveedorId : null);

  if (!canVer) return null;

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return (
      <div className="mt-4 max-w-3xl">
        <ErrorState problem={problem} onRetry={() => query.refetch()} />
      </div>
    );
  }

  if (query.isLoading || query.data == null) {
    return (
      <div className="mt-4 max-w-3xl">
        <TableSkeleton
          rows={3}
          columns={[{ width: 'w-40' }, { width: 'w-56' }]}
        />
      </div>
    );
  }

  const datos = query.data;

  if (editando) {
    return (
      <div className="mt-4 max-w-3xl">
        <ProveedorBancariosForm
          proveedorId={proveedorId}
          datos={datos}
          onCancel={() => setEditando(false)}
          onSaved={() => setEditando(false)}
        />
      </div>
    );
  }

  return (
    <div className="mt-4 max-w-3xl rounded-md border bg-card p-4">
      <div className="mb-3 flex items-center justify-between">
        <h3 className="text-sm font-semibold">Datos bancarios</h3>
        {canEditar && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => setEditando(true)}
          >
            <Pencil className="mr-1.5 h-4 w-4" />
            Editar
          </Button>
        )}
      </div>
      <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3">
        <div>
          <dt className="text-xs text-muted-foreground">Banco</dt>
          <dd>{datos.banco ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">
            CLABE{!datos.clabeCompleta && datos.clabe != null && ' (enmascarada)'}
          </dt>
          <dd className="font-mono">{datos.clabe ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Beneficiario</dt>
          <dd>{datos.beneficiario ?? '—'}</dd>
        </div>
      </dl>
    </div>
  );
}

interface ProveedorBancariosFormProps {
  proveedorId: string;
  datos: ProveedorDatosBancarios;
  onCancel: () => void;
  onSaved: () => void;
}

function ProveedorBancariosForm({
  proveedorId,
  datos,
  onCancel,
  onSaved,
}: ProveedorBancariosFormProps) {
  const actualizar = useActualizarProveedor();
  const [banco, setBanco] = useState(datos.banco ?? '');
  const [beneficiario, setBeneficiario] = useState(datos.beneficiario ?? '');
  // CLABE nunca se pre-llena (llega enmascarada/es sensible): vacío = no
  // cambiar. El checkbox es la única forma de limpiarla explícitamente.
  const [clabe, setClabe] = useState('');
  const [limpiarClabe, setLimpiarClabe] = useState(false);

  const combinacionInvalida = limpiarClabe && clabe.trim() !== '';
  const clabeInvalida = clabe.trim() !== '' && !/^\d{18}$/.test(clabe.trim());

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (combinacionInvalida || clabeInvalida) return;

    const bancoVacio = banco.trim() === '';
    const beneficiarioVacio = beneficiario.trim() === '';
    const clabeNueva = clabe.trim();

    actualizar.mutate(
      {
        id: proveedorId,
        payload: {
          banco: bancoVacio ? null : banco.trim(),
          limpiarBanco: bancoVacio,
          beneficiario: beneficiarioVacio ? null : beneficiario.trim(),
          limpiarBeneficiario: beneficiarioVacio,
          // Vacío + sin checkbox = no se manda nada (no cambia). Checkbox
          // = limpiar explícito. Valor nuevo = set/replace.
          ...(clabeNueva !== ''
            ? { clabe: clabeNueva }
            : limpiarClabe
              ? { limpiarClabe: true }
              : {}),
        },
        // Key fresca por submit: este form puede reabrirse varias veces en
        // el mismo detalle montado (ADR-0020, patrón ProveedorDatosForm).
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Datos bancarios actualizados');
          onSaved();
        },
        onError: (error) => {
          toast.error(
            esApiError(error) ? error.problem.title : 'Error inesperado',
            {
              description: esApiError(error)
                ? (error.problem.detail ??
                  (error.traceId ? `Código: ${error.traceId}` : undefined))
                : undefined,
            },
          );
        },
      },
    );
  }

  return (
    <InlineFormShell
      ariaLabel="Editar datos bancarios"
      onSubmit={onSubmit}
      onCancel={onCancel}
      isPending={actualizar.isPending}
      submitLabel="Guardar cambios"
    >
      <FieldInline label="Banco">
        <Input
          maxLength={120}
          value={banco}
          onChange={(e) => setBanco(e.target.value)}
          placeholder="BBVA México"
        />
      </FieldInline>

      <FieldInline
        label="Nueva CLABE"
        hint="Vacío = no cambiar."
        error={
          combinacionInvalida
            ? 'No puedes capturar una CLABE nueva y quitarla a la vez.'
            : clabeInvalida
              ? 'La CLABE debe tener 18 dígitos.'
              : undefined
        }
      >
        <Input
          maxLength={18}
          className="font-mono"
          value={clabe}
          onChange={(e) => setClabe(e.target.value)}
          placeholder="032180000118359719"
          disabled={limpiarClabe}
        />
      </FieldInline>

      {datos.clabe != null && (
        <label className="flex cursor-pointer items-center gap-2 text-xs">
          <Checkbox
            checked={limpiarClabe}
            onCheckedChange={(v) => setLimpiarClabe(v === true)}
          />
          Quitar la CLABE registrada ({datos.clabe})
        </label>
      )}

      <FieldInline label="Beneficiario">
        <Input
          maxLength={254}
          value={beneficiario}
          onChange={(e) => setBeneficiario(e.target.value)}
          placeholder="Nombre del titular de la cuenta"
        />
      </FieldInline>
    </InlineFormShell>
  );
}
