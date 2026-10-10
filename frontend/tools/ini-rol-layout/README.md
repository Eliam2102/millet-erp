# Prueba aislada de layout INI-ROL

Renderiza el `PanelInicio` real, sus tokens y componentes. Usa permisos en el store y datos **ficticios**, sin iniciar sesión ni llamar al ERP. No prueba integración ni aceptación visual del cliente.

Desde `frontend`, en un entorno que permita Chromium:

```sh
npx vite build --config tools/ini-rol-layout/vite.config.mjs --outDir /tmp/ini-rol-layout-dist
node tools/ini-rol-layout/verificar.cjs
```

Necesita Playwright disponible. Si está instalado en otra ubicación, indicar la carpeta del paquete con `INI_ROL_PLAYWRIGHT`. No se agregó una dependencia al proyecto. `INI_ROL_LAYOUT_DIST` permite cambiar la carpeta del build.

El navegador intercepta todas las solicitudes; sirve el build y fixtures en memoria, sin un servidor ni puertos abiertos. Recorre Compras, Almacén, Cobranza, Tesorería y Administrador a 1440×900, 1280×800 y 390×844. Falla ante scroll de página, desbordamiento horizontal o recorte de indicadores/visuales en escritorio. Genera `/tmp/ini-rol-layout.json` y una captura del Administrador a 1440×900.

El build aislado pasó. La ejecución está **Por confirmar**: el sandbox rechazó el arranque de Chromium (`MachPortRendezvousServer: Permission denied (1100)`). Comparar además con la propuesta aprobada y verificar el shell con una sesión real autorizada; la prueba aislada reproduce las dimensiones del rail y topbar, sin modificar la navegación.
