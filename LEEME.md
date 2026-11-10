# AzerothCore para Windows 10 Pro — edición revisada

Destino: Windows 10 Pro x64 (build 19041 o posterior) con PowerShell 5.1.
El EXE contiene el instalador completo. Los demás archivos permiten revisarlo y recompilarlo; no necesitas instalar Go para usar el EXE.

## Instalación

1. Extrae el ZIP en una carpeta local.
2. Guarda tus datos si ya existe un servidor. Usa la misma cuenta administradora para instalar y gestionar la distribución WSL.
3. Ejecuta Instalar_WoW_Server.exe y acepta UAC.
4. Si pide reiniciar, guarda tu trabajo, reinicia Windows y vuelve a ejecutar el mismo EXE.
5. Al finalizar, abre Estado_WoW_Server.bat en la carpeta WoWServer de tu unidad de sistema (normalmente C:\WoWServer). Espera a que authserver/worldserver estén activos.

Requiere virtualización habilitada, Internet y al menos 25 GB libres; preferibles 40 GB. Con 4 GB de RAM el margen es muy reducido: el funcionamiento en ese hardware queda pendiente de prueba. Cambia la configuración global de WSL2 a 2 GB de memoria, 2 procesadores y 4 GB de swap, guardando copia de .wslconfig. Puede requerir reiniciar para aplicarla a una VM ya iniciada.

## Comprobación sin instalar

Desde PowerShell, en la carpeta extraída:

    .\Instalar_WoW_Server.exe -CheckOnly

Debe mostrar CHECK_OK SHA256=... y terminar. Solo comprueba extracción, ejecución y sintaxis del PowerShell integrado; no prueba descargas, WSL, Docker ni el servidor.

## Gestión

En C:\WoWServer se crean Iniciar_WoW_Server.bat, Parar_WoW_Server.bat, Estado_WoW_Server.bat, Ver_Logs_WoW.bat, Crear_Cuenta_WoW.bat, Configurar_IP_WoW.bat y Crear_Copia_Seguridad.bat.

Para crear una cuenta, abre Crear_Cuenta_WoW.bat y escribe en la consola:

    account create USUARIO CONTRASENA CONTRASENA

Para salir de la consola sin apagar worldserver: Ctrl+P y después Ctrl+Q. No uses Ctrl+C.

Configura la dirección del reino con Configurar_IP_WoW.bat: 127.0.0.1 para este PC, o la IP local del servidor para LAN. El cliente debe ser compatible con WoW 3.3.5a build 12340 y tener su realmlist configurado. No se incluye ni descarga el cliente comercial.

El firewall solo permite los puertos del juego 3724/8085 desde la subred local, incluso si Windows clasifica la Wi-Fi como pública. No se configura el router ni el acceso desde Internet. MySQL y SOAP quedan en localhost. El autoarranque del servidor usa permisos limitados; una tarea separada configura la red con elevación.

Registros: C:\WoWServer\logs\instalacion.log, logs\inicio-automatico.log y network-logs\red-lan.log. Copias SQL: /opt/azerothcore/backups dentro de WSL.

## Launcher de escritorio

Ejecuta `LaunchWoW\AzerotBatzoki.exe`. La versión 1.2 incorpora una portada de Northrend, identidad con el icono propio, ajustes oscuros y un botón Jugar destacado en dorado. La distribución se adapta al tamaño de la ventana y a los cambios de resolución de WoW. Se puede redimensionar y maximizar. La actualización se bloquea si el cliente seleccionado está abierto. Para compilarlo, usa `LaunchWoW\Compilar.ps1`. El cliente debe ser WoW 3.3.5a build 12340 con `Wow.exe`, `Data\common.MPQ` y un locale `enUS`, `enGB` o `esES`. El botón de instalación descarga desde `192.168.201.209:8788`, comprueba SHA-256, configura el reino `192.168.201.213` y arranca el juego. No requiere `Data\interface.MPQ`.

La dirección configurada `192.168.201.213` es privada y solo es accesible desde la red local que contiene el portátil servidor; no permite jugar desde Internet. El equipo de distribución `192.168.201.209` debe tener el cliente fuente en `Documents\AzerotBatzoki-ClientDistribution`. Ejecuta `LaunchWoW\Enable-DownloadHostFirewall.ps1` una vez desde PowerShell como administrador y pulsa `ACTIVAR LAN` para servirlo por TCP 8788 a LocalSubnet; el cliente y el certificado privado quedan fuera de Git. El reino sigue en `192.168.201.213` (auth 3724, world 8085). El puerto 8787 del servicio de cuentas queda sin cambios. No publiques ni abras el host de descarga a Internet.

El launcher está implementado en C# WinForms y no requiere Go para ejecutarse. Para recompilarlo, ejecuta `LaunchWoW\Compilar.ps1`; para compilar el paquete completo, ejecuta `Compilar.ps1`.
Requiere .NET Framework 4.8, incluido en las versiones de Windows 10 indicadas arriba.

## Actualizar cliente y addons

En el PC de descargas, copia los addons compartidos a `Documents\AzerotBatzoki-ClientDistribution\Interface\AddOns\NombreDelAddon`. Termina de copiarlos antes de que los jugadores actualicen. El host detecta los cambios y recalcula el manifiesto en la siguiente petición; el cálculo de hashes puede tardar. Tras sustituir el ejecutable del host por esta versión, reinícialo una vez.

En cada PC, cierra WoW, selecciona el cliente existente y pulsa **ACTUALIZAR**. Se comparan tamaños y SHA-256 y se descargan únicamente los archivos ausentes o distintos. Cada archivo se verifica antes de reemplazarlo. Se conservan los ajustes y archivos locales que no aparecen en el manifiesto. Los addons compartidos con la misma ruta se actualizan a la versión del host; los addons retirados del host no se borran automáticamente del PC del jugador.

## Estado de la revisión

Se corrigieron permisos, tratamiento de credenciales, códigos de error, backups incompletos, entorno de PowerShell y arranque/red. El launcher comprueba la presencia de `Wow.exe` y un idioma compatible, no la de `Data\interface.MPQ`. Se reconstruyó el EXE y pasaron las pruebas aisladas. No se ha ejecutado una instalación completa en Windows 10 Pro ni se ha verificado el inicio de sesión desde otro PC de la LAN; no se garantiza todavía el funcionamiento de extremo a extremo. Lee INFORME_REVISION.md para ver evidencia y pruebas pendientes.

El EXE no está firmado y Windows puede mostrar editor desconocido. SHA256SUMS-local.txt permite comparar el contenido recibido. Las imágenes Docker mantienen etiquetas móviles del proyecto original.

## Fuentes y recompilación

Incluye las fuentes Go del instalador de servidor y las fuentes C# del launcher de escritorio. Para compilar el paquete completo se requieren Go, Git Bash y el compilador de .NET Framework; el launcher se ejecuta sin Go. El archivo Test-Installer.ps1 usa Docker simulado y no instala WSL ni modifica tareas/firewall.
