import { createFileRoute } from '@tanstack/react-router';
import { useAuth } from '@/lib/auth/useAuth';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Building2, CheckCircle2, ShieldAlert, User, Mail, Fingerprint } from 'lucide-react';

/**
 * Pantalla principal (Home) dentro del shell autenticado.
 * Muestra el estado de la sesión activa del usuario y las empresas accesibles.
 * Si el usuario no tiene empresas asignadas (ej. auto-aprovisionado reciente),
 * muestra una alerta institucional con instrucciones para el administrador.
 */
export const Route = createFileRoute('/_app/')({
  component: HomeComponent,
});

function HomeComponent() {
  const { user, currentEmpresa, empresas } = useAuth();
  const tieneEmpresas = empresas.length > 0;

  return (
    <div className="space-y-6 max-w-3xl">
      {/* Saludo y contexto de empresa */}
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-slate-100">
          Bienvenido, {user?.nombre?.split(' ')[0] ?? 'Usuario'}
        </h1>
        <p className="text-sm text-muted-foreground mt-1">
          {currentEmpresa
            ? `Trabajando en ${currentEmpresa.razonSocial} (${currentEmpresa.rfc})`
            : 'Sin empresa activa seleccionada'}
        </p>
      </div>

      {/* Alerta si el usuario no tiene empresas asignadas (auto-aprovisionamiento sin roles) */}
      {!tieneEmpresas && (
        <Alert className="border-amber-200 bg-amber-50/80 dark:border-amber-900/50 dark:bg-amber-950/20 text-amber-900 dark:text-amber-200">
          <ShieldAlert className="h-4 w-4 text-amber-600 dark:text-amber-400" />
          <AlertTitle className="font-semibold text-amber-800 dark:text-amber-300">
            Cuenta activa sin asignaciones de empresa o rol
          </AlertTitle>
          <AlertDescription className="text-xs text-amber-700 dark:text-amber-300/90 mt-1 leading-relaxed">
            Has iniciado sesión exitosamente mediante Microsoft Entra ID. Sin embargo, tu usuario aún no tiene empresas o roles asignados dentro del ERP. Por favor, solicita a tu administrador que configure tus permisos desde el módulo de <strong>Administración &gt; Usuarios</strong>.
          </AlertDescription>
        </Alert>
      )}

      {/* Tarjeta de Sesión Activa */}
      <Card className="border-slate-200 dark:border-slate-800 shadow-xs">
        <CardHeader className="pb-4">
          <div className="flex items-center justify-between">
            <CardTitle className="text-base font-semibold text-slate-900 dark:text-slate-100 flex items-center gap-2">
              <User className="h-4 w-4 text-primary" />
              Sesión activa
            </CardTitle>
            <Badge variant="outline" className="text-xs gap-1 border-emerald-500/30 text-emerald-700 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/30">
              <CheckCircle2 className="h-3 w-3" />
              Autenticado
            </Badge>
          </div>
          <CardDescription className="text-xs">
            Credenciales y contexto de la sesión actual validados por el sistema.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3 pt-0 text-sm">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-2 border-t border-slate-100 dark:border-slate-800">
            <div className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <User className="h-3.5 w-3.5" />
                Nombre completo
              </span>
              <p className="font-medium text-slate-800 dark:text-slate-200">
                {user?.nombre ?? '—'}
              </p>
            </div>

            <div className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <Mail className="h-3.5 w-3.5" />
                Correo corporativo
              </span>
              <p className="font-medium text-slate-800 dark:text-slate-200">
                {user?.email ?? '—'}
              </p>
            </div>

            <div className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <Fingerprint className="h-3.5 w-3.5" />
                User ID (Interno)
              </span>
              <p className="font-mono text-xs text-slate-600 dark:text-slate-400 break-all">
                {user?.id ?? '—'}
              </p>
            </div>

            <div className="space-y-1">
              <span className="text-xs font-medium text-muted-foreground flex items-center gap-1.5">
                <Building2 className="h-3.5 w-3.5" />
                Empresas asignadas
              </span>
              <div>
                {tieneEmpresas ? (
                  <div className="flex items-center gap-2">
                    <Badge variant="secondary" className="font-medium text-xs">
                      {empresas.length} {empresas.length === 1 ? 'empresa accesible' : 'empresas accesibles'}
                    </Badge>
                    {currentEmpresa && (
                      <span className="text-xs text-muted-foreground truncate">
                        (Activa: {currentEmpresa.razonSocial})
                      </span>
                    )}
                  </div>
                ) : (
                  <Badge variant="outline" className="text-xs border-amber-300 text-amber-700 dark:text-amber-400 dark:border-amber-800">
                    0 asignadas
                  </Badge>
                )}
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
