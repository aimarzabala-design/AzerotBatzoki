# Revisión del instalador AzerothCore — 02/11/2026

Destino confirmado: **Windows 10 Pro x64**, compilación 19041 o posterior, PowerShell 5.1. El host de revisión es Windows 11 y no sustituye las pruebas en el destino.

Se ha reconstruido el EXE a partir de las fuentes corregidas. Las comprobaciones aisladas pasan. **No se ha ejecutado una instalación completa ni se ha comprobado una sesión de juego.**

## Launcher de escritorio

El launcher está implementado en C# WinForms. No empaqueta ni sirve el cliente comercial WoW. El usuario selecciona una instalación local existente; `JUGAR` configura la realmlist `192.168.201.213`.

El servidor HTTPS del launcher se limita a salud y registro de cuentas AzerothCore; ya no publica manifiestos ni archivos de cliente. La activación desde el host muestra una huella TLS para compartir por otro canal. El alta requiere WSL `WoWServer` y worldserver activos, y solo admite clientes de la red privada.

## Correcciones

- Permisos: carpeta de scripts con escritura para Administradores/SYSTEM; cuenta instaladora con lectura. Solo WSL y los registros ordinarios permiten escritura a esa cuenta. Se rechazan enlaces/junctions en las rutas críticas revisadas.
- Autoarranque: la tarea del servidor usa permisos limitados. La tarea elevada de red únicamente lee una IPv4 validada y configura los puertos 3724/8085. No ejecuta código de WSL; usa netsh desde la carpeta del sistema obtenida mediante API. Sus registros están en una carpeta protegida.
- Credenciales: permisos mediante SID, válidos en Windows en español; si no se pueden proteger, se detiene la instalación. El bootstrap que contiene la contraseña se protege y elimina al terminar su ejecución. No se genera otra contraseña silenciosamente para una distribución existente.
- Lanzador: directorio temporal único, limpieza al terminar, PowerShell por ruta absoluta y propagación de argumentos/códigos de salida. Se elimina PSModulePath heredado para evitar cargar módulos de PowerShell 7 en Windows PowerShell 5.1.
- UAC: se conserva el código de salida del proceso elevado.
- PowerShell 5.1: el progreso de Docker se une a stdout dentro de Bash para que stderr normal no aborte el autoarranque. Se comprueban los códigos de salida de WSL/netsh.
- Copias: un volcado fallido no deja un archivo SQL final con apariencia de copia válida; se escribe a un temporal y se publica al completar. Incluye permisos privados y nombres únicos.
- WSL: firma Microsoft comprobada antes de instalar el MSI del kernel; se respeta su solicitud de reinicio. Se rechaza ARM64 porque la imagen y el EXE son AMD64.
- Se conserva Unicode en .wslconfig y se obtiene la ruta Linux del bootstrap de la unidad de sistema. Se elimina un fetch que no actualizaba el checkout y podía sugerir una actualización inexistente.

## Evidencia

- Los SHA-256 de EXE y PowerShell originales coincidían con el manifiesto incluido.
- Dos subagentes revisaron seguridad/Windows y compatibilidad con el proyecto oficial; otro corrigió y probó el lanzador. El principal integró los cambios y ejecutó las comprobaciones finales.
- `go test -v main.go main_test.go`: argumentos con espacios, ruta de PowerShell, limpieza, directorios distintos, códigos 0/23 y entorno de módulos. PowerShell se simula con un proceso auxiliar de prueba.
- `go vet main.go main_test.go`: correcto.
- Compilación Windows/AMD64, CGO desactivado, Go 1.27.3 portátil oficial con SHA-256 verificado. No se ha instalado Go globalmente.
- `Instalar_WoW_Server.exe -CheckOnly`: el EXE ejecuta el PowerShell integrado y comunica el mismo SHA-256 que la fuente entregada, sin instalar ni solicitar elevación.
- `Test-Installer.ps1` con Windows PowerShell 5.1: sintaxis PowerShell, scripts de autoarranque/red, ACL en memoria, IPv4 y rechazo de metacaracteres, sintaxis Bash y comportamiento de backups con Docker simulado. No crea tareas ni cambia firewall/WSL.
- La prueba de backup falló contra la fuente original porque quedaba un SQL incompleto y pasó después de corregirlo. La prueba del entorno PSModulePath también falló antes de su corrección.
- No se ha validado `docker compose config` con Compose real ni se ha importado/iniciado una distribución. La consulta del estado WSL de este entorno devolvió acceso denegado.

## Compatibilidad externa y límites

Los servicios, variables, puertos y nombre de contenedor usados coinciden con el [compose oficial revisado](https://github.com/azerothcore/acore-docker/blob/master/docker-compose.yml). Los comandos de cuenta coinciden con la [documentación oficial](https://www.azerothcore.org/wiki/creating-accounts). El flujo de componentes y kernel sigue la [instalación manual de WSL de Microsoft](https://learn.microsoft.com/en-us/windows/wsl/install-manual). La imagen Ubuntu se valida contra su [lista oficial SHA256SUMS](https://cloud-images.ubuntu.com/wsl/jammy/current/SHA256SUMS).

Se mantienen las imágenes `master` y MySQL `8.0` del diseño original: son etiquetas móviles. Por tanto, una instalación futura puede descargar versiones distintas de las revisadas. Las reinstalaciones conservan el checkout existente y sus datos; no fuerzan actualizaciones ni borran volúmenes. No se garantiza compatibilidad con instalaciones previas modificadas manualmente.

Con 4 GB de RAM físicos y WSL limitado a 2 GB, el funcionamiento sigue sin demostrarse. La configuración de memoria afecta globalmente a WSL2 y sus cambios pueden requerir reiniciar Windows; no se han detenido otras distribuciones en este equipo.

El EXE no tiene firma digital. El manifiesto SHA-256 sirve para detectar diferencias respecto a este paquete; no acredita una firma de editor.

## Comprobación pendiente en Windows 10 Pro

1. Guardar cualquier servidor/datos previos. Extraer el ZIP y ejecutar el EXE desde la misma cuenta administradora que gestionará la distribución y sus tareas.
2. Si pide reiniciar, hacerlo y volver a ejecutar el EXE. Verificar final de instalación y revisar los registros.
3. En `Estado_WoW_Server.bat`, comprobar MySQL, authserver y worldserver. Las tareas de importación/datos pueden terminar con código 0 y quedar detenidas: es normal.
4. Crear una cuenta y conectar un cliente compatible 3.3.5a, build 12340, con el realmlist configurado. Comprobar entrada efectiva al mundo.
5. Cerrar sesión/reiniciar y comprobar autoarranque y conexión LAN. Revisar `logs\inicio-automatico.log` y `network-logs\red-lan.log`.
6. Crear una copia y validar su restauración en una instancia separada. Confirmar que DB/SOAP no se publican en LAN y que la regla de juego solo permite la subred privada.

Estos pasos pendientes son necesarios para afirmar que el instalador funciona de extremo a extremo en el equipo de destino.
