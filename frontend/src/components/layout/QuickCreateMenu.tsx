import {
  BookOpen,
  Building2,
  CircleDollarSign,
  CreditCard,
  FileText,
  Plus,
  Receipt,
  ShoppingCart,
  type LucideIcon,
} from 'lucide-react';
import { Link } from '@tanstack/react-router';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Button } from '@/components/ui/button';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useNuevaRequisicion } from '@/features/compras/components/nueva-requisicion-context';
import { useNuevaOrdenCompra } from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import { useNuevoPedido } from '@/features/facturacion/components/nuevo-pedido-context';
import { useNuevoAnticipo } from '@/features/facturacion/components/nuevo-anticipo-context';
import { useNuevoRepp } from '@/features/facturacion/components/nuevo-repp-context';
import { useNuevaCartaPorte } from '@/features/facturacion/components/nueva-carta-porte-context';
import { useNuevaLineaCredito } from '@/features/cxc/components/nueva-linea-credito-context';
import { useRegistrarGestion } from '@/features/cxc/components/registrar-gestion-context';
import { useNuevaPropuesta } from '@/features/cxc/components/nueva-propuesta-context';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;QuickCreateMenu/&gt;</c> — popover del botón "+" del topbar.
 * Muestra acciones de creación rápida agrupadas por módulo, gateadas
 * por permisos del usuario actual (si no tiene permiso, la acción no
 * aparece; si un módulo entero queda sin acciones, la categoría se
 * omite).
 *
 * <para>Diseño polish (design/frontend-polish): inspirado en el
 * pattern Zoho Books — categorías horizontales con items debajo. Las
 * acciones que abren un Sheet/modal usan <c>onClick</c>; las que
 * navegan a una ruta full-page usan <c>to</c>.</para>
 */

interface QuickCreateAction {
  label: string;
  /** Permiso requerido. Si no existe en <c>permisos</c>, la acción
   * se omite. */
  permission: string;
  /** Acción al click: o navega a una ruta (<c>to</c>) o invoca
   * un handler (<c>onClick</c>) — típicamente abre un Sheet/modal. */
  to?: string;
  onClick?: () => void;
}

interface QuickCreateGroup {
  label: string;
  icon: LucideIcon;
  actions: readonly QuickCreateAction[];
}

export function QuickCreateMenu() {
  const permisos = useAuthStore((s) => s.permisos);
  const nuevaRequisicion = useNuevaRequisicion();
  const nuevaOrdenCompra = useNuevaOrdenCompra();
  const nuevoPedido = useNuevoPedido();
  const nuevoAnticipo = useNuevoAnticipo();
  const nuevoRepp = useNuevoRepp();
  const nuevaCartaPorte = useNuevaCartaPorte();
  const nuevaLineaCredito = useNuevaLineaCredito();
  const registrarGestion = useRegistrarGestion();
  const nuevaPropuesta = useNuevaPropuesta();

  // Catálogo de acciones, definido dentro del componente para poder
  // capturar handlers de hooks. Cuando otros módulos del back-office
  // se implementen, registrar acá (convención §6.4 patrones-compras).
  const grupos: readonly QuickCreateGroup[] = [
    {
      label: 'Ventas',
      icon: CircleDollarSign,
      actions: [
        {
          label: 'Factura',
          permission: PermisosCanonicos.FacturacionFacturasEmitir,
          // FAC-UX-PR2: ya no es Sheet — navega a la página del form
          // de emisión con pestañas.
          to: '/facturacion/facturas/nueva',
        },
        {
          label: 'Anticipo',
          permission: PermisosCanonicos.FacturacionAnticiposEmitir,
          onClick: () => nuevoAnticipo.abrir(),
        },
        {
          label: 'Complemento de pago (REPP)',
          permission: PermisosCanonicos.FacturacionReppEmitir,
          onClick: () => nuevoRepp.abrir(),
        },
        {
          label: 'Carta Porte',
          permission: PermisosCanonicos.FacturacionCartaPorteEmitir,
          onClick: () => nuevaCartaPorte.abrir(),
        },
        {
          label: 'Pedido manual',
          permission: PermisosCanonicos.FacturacionPedidosCapturar,
          onClick: () => nuevoPedido.abrir(),
        },
      ],
    },
    {
      label: 'Compras',
      icon: ShoppingCart,
      actions: [
        {
          label: 'Requisición',
          permission: PermisosCanonicos.ComprasRequisicionesCrear,
          onClick: () => nuevaRequisicion.abrir(),
        },
        {
          label: 'Orden de compra',
          permission: PermisosCanonicos.ComprasOrdenesCrear,
          onClick: () => nuevaOrdenCompra.abrir(),
        },
      ],
    },
    {
      label: 'Banca',
      icon: CreditCard,
      actions: [],
    },
    {
      label: 'Cuentas por cobrar',
      icon: Receipt,
      actions: [
        {
          label: 'Línea de crédito',
          permission: PermisosCanonicos.CuentasPorCobrarLineasCreditoGestionar,
          onClick: () => nuevaLineaCredito.abrir(),
        },
        {
          label: 'Gestión de cobranza',
          permission: PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar,
          onClick: () => registrarGestion.abrir(),
        },
        {
          label: 'Propuesta de aplicación',
          permission: PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
          onClick: () => nuevaPropuesta.abrir(),
        },
      ],
    },
    {
      label: 'Cuentas por pagar',
      icon: FileText,
      actions: [],
    },
    {
      label: 'Activos',
      icon: Building2,
      actions: [],
    },
    {
      label: 'Contabilidad',
      icon: BookOpen,
      actions: [],
    },
  ];

  // Filtra acciones por permiso y luego grupos sin acciones.
  const visibles = grupos
    .map((g) => ({
      ...g,
      actions: g.actions.filter((a) => permisos.includes(a.permission)),
    }))
    .filter((g) => g.actions.length > 0);

  const sinAcciones = visibles.length === 0;

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button size="icon" className="size-9" aria-label="Crear nuevo">
          <Plus className="h-4 w-4" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="end"
        className="w-[min(36rem,calc(100vw-2rem))] p-4"
      >
        {sinAcciones ? (
          <p className="text-sm text-muted-foreground">
            No tienes permisos para crear documentos en este momento. Si
            crees que debería ser distinto, contacta a tu administrador.
          </p>
        ) : (
          <div className="grid grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
            {visibles.map((g) => (
              <GrupoQuickCreate key={g.label} grupo={g} />
            ))}
          </div>
        )}
      </PopoverContent>
    </Popover>
  );
}

function GrupoQuickCreate({ grupo }: { grupo: QuickCreateGroup }) {
  const Icon = grupo.icon;
  return (
    <section className="space-y-1.5">
      <h3 className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        <Icon className="h-3.5 w-3.5" aria-hidden="true" />
        {grupo.label}
      </h3>
      <ul className="space-y-0.5">
        {grupo.actions.map((a) => (
          <li key={a.label}>
            <ActionItem action={a} />
          </li>
        ))}
      </ul>
    </section>
  );
}

function ActionItem({ action }: { action: QuickCreateAction }) {
  const className = cn(
    'flex w-full items-center gap-1.5 rounded px-2 py-1 text-left text-sm',
    'text-foreground hover:bg-accent hover:text-accent-foreground',
    'focus:bg-accent focus:text-accent-foreground focus:outline-none',
  );
  const content = (
    <>
      <Plus
        className="h-3.5 w-3.5 text-muted-foreground"
        aria-hidden="true"
      />
      {action.label}
    </>
  );

  if (action.onClick) {
    return (
      <button type="button" onClick={action.onClick} className={className}>
        {content}
      </button>
    );
  }
  if (action.to) {
    return (
      <Link to={action.to} className={className}>
        {content}
      </Link>
    );
  }
  return null;
}
