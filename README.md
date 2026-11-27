# Taskbar Studio

## Fase 1 · Barra flotante (iteración 4)

Ejecute `src/FloatingDock/bin/Release/net10.0-windows/FloatingDock.exe`. La ventana ya no muestra el marco tradicional. Windows 11 recibe la preferencia DWM de esquinas redondeadas y renderiza los bordes de controles redondeados en el área de trabajo. El asa de puntos permite arrastrar la barra; **Alt + flechas** la mueve con el teclado. **Esc**, el botón **Cerrar** y el menú contextual ofrecen salidas accesibles. El menú también permite centrarla de nuevo. La barra no es superior a las aplicaciones y no reserva área de trabajo.

El ancho se calcula a partir de los accesos visibles y de sus etiquetas. Las pruebas breves a 120 DPI midieron 461 px para tres accesos, 348 px para dos, 235 px para uno y 181 px para un icono sin etiqueta. El área de cálculo usa `Screen.WorkingArea`, que excluye la barra y las barras acopladas. El movimiento mantiene toda la ventana dentro del área disponible.

`IBarItem` define identidad, etiqueta, visibilidad por perfil, creación de icono y activación. `LauncherItem` mantiene los tres destinos actuales y su lógica de lanzamiento. La ventana consulta el elemento para representar y activar el botón. Ese contrato permite que futuras fuentes describan ventanas abiertas, sin incluir en esta iteración consultas al Shell, agrupación, cambio de ventanas o Inicio.

La barra flotante normal puede quedar cubierta y no sustituye la barra de Windows en aplicaciones maximizadas o de pantalla completa. La posición del prototipo se centra en el monitor seleccionado, se limita a su área de trabajo y se ajusta ante cambios de pantalla. La gestión real de ventanas, el seguimiento de foco mediante WinEvent, el comportamiento de appbar, la interacción con Inicio y la sincronización por monitor quedan para iteraciones posteriores. Una sustitución funcional deberá respetar el ciclo de registro y baja de appbar y diseñar la recuperación y el cambio de monitor.

La prueba gráfica confirmó DWM `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND` con HRESULT 0. Una captura con fondo de color verificó la esquina redondeada. Una muestra de reposo de tres segundos a 120 DPI registró 0 ms de CPU a la precisión del contador, 12,51 MiB de memoria privada, 26 objetos GDI y 20 USER. La captura inspeccionada quedó en el directorio temporal del sistema.

Release x64 compiló los cinco proyectos con cero advertencias y errores. Pasaron los 13 tests de Fase 0 y los 24 grupos de Fase 1. Las pruebas gráficas verificaron iconos, perfiles, anchos de uno a tres accesos, etiquetas, teclado, asa, menús de cierre, movimiento, límites del área de trabajo y que la barra original permaneciera igual. Bloc de notas, Calculadora y Explorador abrieron mediante Entrar. Se verificaron posiciones escaladas de 96 a 288 DPI por cálculo geométrico; el escritorio de prueba estaba a 120 DPI.

## Fase 1 · Iconos por perfil (iteración 3)
## Fase 1 · Iconos por perfil (iteración 3)

Con el dock enfocado, pulse **Ctrl+,**, seleccione un perfil y elija Bloc de notas, Calculadora o Explorador en **Icono de la aplicación**. Pulse **Seleccionar…** para abrir un PNG o ICO de una unidad local. La vista previa muestra el icono que recibirá ese acceso. **Aplicar** actualiza el dock abierto. **Restaurar icono** quita la personalización de ese acceso. **Cancelar**, Escape y el cierre del selector descartan las importaciones y los demás cambios del borrador.

Para probar la independencia, cree un perfil Gaming a partir del predeterminado, cambie solo el icono de Calculadora y aplique. Alterne entre los perfiles. Cierre el dock, mueva la imagen original y vuelva a abrirlo. En Gaming debe seguir viendo el icono elegido. Puede probar **Restaurar icono → Cancelar** antes de repetir la restauración con **Aplicar**.

**Almacenamiento.** El catálogo v1 incorpora el campo opcional `icons` con las claves `notepad`, `calculator` y `explorer`. Los perfiles anteriores usan los iconos que les correspondían. El borrador guarda los píxeles importados en memoria. Al aplicar, se publican los recursos por su SHA-256 en `%LOCALAPPDATA%\TaskbarStudio\Icons\icon-<hash>.bgra` y después se reemplaza el catálogo bajo el mismo bloqueo de escritura. Cada recurso contiene 128 × 128 píxeles BGRA con transparencia (64 KiB). Los perfiles pueden compartir el archivo y conservar referencias independientes. El último perfil que deja de usarlo permite su limpieza. Tras una interrupción entre la publicación y el reemplazo, el siguiente **Aplicar** recoge los recursos sueltos.

**Iconos de tema.** La prioridad es personalización del perfil, recurso del tema y dibujo integrado. Para un tema local, un valor como `"icons": { "calculator": "ocean-calc" }` referencia `%LOCALAPPDATA%\TaskbarStudio\Themes\icon-ocean-calc.png`. Se aplican las reglas de identificadores de temas. Si el recurso falta, está dañado o la referencia es inválida, el dock prueba el siguiente nivel. Los destinos ejecutables siguen en C#.

**Formatos y coste.** El importador admite archivos de hasta 2 MiB. Comprueba la firma, las dimensiones, la estructura y la decodificación. Para PNG estáticos el máximo es 1024 × 1024. Para ICO se admite un directorio de hasta 64 imágenes y se lee la de mayor área, con PNG interno o DIB clásico sin compresión. La copia se ajusta a un cuadrado transparente conservando la proporción. El selector y el dock usan cada uno una caché de hasta 12 recursos (768 KiB de píxeles). La decodificación y la escritura ocurren al interactuar y pueden producir una pausa breve. El presupuesto de importaciones pendientes es de 8 MiB por edición.

**Verificación de la Iteración 3.** Los cinco proyectos recompilaron en Release x64 con cero advertencias y errores, antes y después. Inicialmente pasaron 13 pruebas de Fase 0, 17 grupos de Fase 1 y dos pruebas gráficas. Al cierre pasaron 13, 23 y tres, respectivamente. La prueba gráfica nueva usó un PNG generado para el ensayo, comprobó el color de la vista previa y del botón, canceló una importación y cambió de perfil. Tras eliminar el PNG de origen, abrió el dock en un proceso de pruebas nuevo y verificó el icono guardado. Por último restauró el dibujo integrado. La prueba proporcionó la ruta directamente al controlador de importación del selector. El recorrido del diálogo nativo de archivos queda para la comprobación manual.

## Fase 1 · Temas y perfiles del dock (iteración 2)

El primer inicio usa el perfil **Predeterminado**, con Minimal Dark y los accesos a Bloc de notas, Calculadora y Explorador. Para administrar los perfiles, pulse **Ctrl+,** con el dock enfocado o elija **Configurar perfiles y tema…** en el menú contextual.

1. **Crear…** copia los ajustes del perfil seleccionado. Escriba un nombre, elija un tema y marque los accesos que quiere mostrar. Puede ocultar los nombres de los botones. Cada acceso conserva su nombre accesible y su descripción al pasar el puntero.
2. **Aplicar** guarda todos los cambios y activa el perfil seleccionado en el dock abierto. Al volver a iniciarlo, se restaura ese perfil. **Cancelar**, Escape o cerrar el selector descartan la sesión de edición completa.
3. **Renombrar…** conserva el identificador del perfil. **Eliminar…** pide confirmación y selecciona el predeterminado. La eliminación se guarda al aplicar. El perfil de identificador `default` está protegido incluso si se le cambia el nombre.

`Theme` mantiene la paleta y las dimensiones a 96 DPI. Cada `Profile` referencia su tema por ID y guarda su nombre, la visibilidad de las etiquetas y la selección de accesos integrados. Los nombres de perfil admiten hasta 64 caracteres y al crear o renombrar se comprueba que el nombre esté libre. Los destinos de los accesos siguen definidos en C#.

Los perfiles y el ID activo se guardan juntos en `%LOCALAPPDATA%\TaskbarStudio\Profiles\profiles.json` (hasta 256 KiB). **Aplicar** escribe un temporal, lo vacía a disco y reemplaza el catálogo en el mismo directorio. Si otra instancia ha cambiado el catálogo, vuelva a abrir el selector para editar la versión guardada. En la primera apertura se leen los archivos `profile-<id>.json` de la Iteración 1. El primer **Aplicar** incorpora esos perfiles al catálogo y conserva los originales.

Si el perfil activo falta o es inválido, se selecciona el predeterminado. Si todo el catálogo es ilegible, el dock puede arrancar con los ajustes iniciales. El arranque y **Cancelar** conservan el archivo para su recuperación. Si falta un tema local, el dock muestra Minimal Dark.

Para añadir un tema local, copie el JSON de ejemplo en `%LOCALAPPDATA%\TaskbarStudio\Themes\theme-<id>.json` y cambie `id` y `name`. Por ejemplo, `theme-ocean.json` debe contener `"id": "ocean"`. El selector descubre archivos al abrirse. Los identificadores empiezan por una letra minúscula ASCII, admiten letras minúsculas, dígitos y guiones y tienen hasta 48 caracteres. Los temas integrados tienen prioridad. [JSON de Minimal Dark](src/Personalization/Themes/minimal-dark.json)

La versión 1 admite JSON UTF-8 de hasta 16 KiB, colores `#RRGGBB` y medidas enteras: icono de 16–48, ancho mínimo de botón de 56–144, relleno y separación de 0–16 y margen inferior de 0–32. Se rechazan campos desconocidos o repetidos, rutas en los identificadores y puntos de reanálisis en las ubicaciones de la aplicación. Los archivos inválidos se omiten del selector. En alto contraste se usa la paleta del sistema. Los tres destinos siguen definidos en C# y cada dibujo tiene una clave propia en `DockIcons`. El catálogo versionado y esas claves permiten incorporar paquetes e iconos en el futuro. El selector solo existe mientras está abierto.

Tras compilar Release x64, estas pruebas usan datos temporales. La última abre y cierra ventanas brevemente:

```powershell
dotnet run --project tests/Phase0.Tests -c Release --no-build
dotnet run --project tests/Phase1.Tests -c Release --no-build
dotnet run --project tests/Phase1.Tests -c Release --no-build -- --dock-smoke
```

En la Iteración 2 los cinco proyectos recompilaron en Release x64 con cero advertencias y errores. Pasaron los 13 casos de Fase 0, los 17 grupos de Fase 1 y las dos pruebas gráficas breves. Se verificaron la migración desde JSON de la Iteración 1, la recuperación de perfiles, la cancelación, el rechazo de ediciones simultáneas obsoletas y la conservación del catálogo anterior si falla el reemplazo.

En la Iteración 1, el 26 de noviembre de 2026, los cinco proyectos recompilaron en Release x64 con cero errores y advertencias. Pasaron los 13 casos de Fase 0, los 10 grupos de Fase 1 y la prueba funcional del selector con perfil temporal. Tres aperturas del ejecutable a 120 DPI conservaron la barra y el área de trabajo y terminaron sin procesos residuales. Con tres accesos, una muestra de 5,27 s en reposo registró 11,86 MiB de memoria privada máxima muestreada y 0,000 % de CPU media normalizada, a la precisión del contador. El consumo sostenido y la GPU de aquella versión quedaron sin medir.

Quedan para comprobación física la legibilidad con alto contraste, el traslado entre monitores de distinto DPI y el uso del selector con teclado y ratón en esas condiciones. Las cifras de consumo de Fase 0 corresponden a aquella compilación.

## Prototipos de Fase 0

Dos prototipos de escritorio independientes para Windows 11 x64. El primero permite probar y revertir la ocultación automática de la barra del usuario. El segundo muestra un dock flotante de tres accesos.

**El dock B queda aprobado provisionalmente para la Fase 1. La Fase 0 sigue abierta.** Antes de la publicación se recompilaron los tres proyectos en Release x64 con cero errores y advertencias. Pasaron las 13 pruebas automatizadas de ABI y lógica. [Resultados y criterios pendientes](docs/PRUEBAS-FASE-0.md)

**A · Ocultación nativa.** En Windows 11 25H2 (26200.9974) se habían observado 40 ciclos reales de cambio y restauración con la barra abajo, 20 por estado inicial. En la verificación de cierre, con la barra a la izquierda, A bloqueó la escritura. La sonda confirmó la misma posición, área de trabajo y ocultación al salir, sin registro de recuperación pendiente. Quedan la recuperación tras una interrupción real y la validación en otra compilación. [Ensayos de A](docs/PRUEBAS-FASE-0.md)

**B · Dock flotante.** En el cierre completó 50 ciclos con cero procesos residuales y un P95 de 361 ms en los 49 arranques posteriores al primero (objetivo ≤ 750 ms). Una misma instancia con 20 accesos permaneció abierta dos horas y cerró normalmente. Las series anteriores verificaron el aislamiento de la barra con la ocultación activada y desactivada, y cumplieron los límites de CPU y memoria.

Dos pares controlados de GPU dieron **0,0000 y 0,1928 pp** de incremento del escritorio, dentro del límite de **0,500 pp**. La entrada de usuario interrumpió el tercero. Quedan ese par, la traza para explicar el exceso previo de **0,545 pp**, los reinicios de Explorer, la suspensión, los arranques en frío y la matriz de interacción y pantallas. [Mediciones y criterios pendientes](docs/PRUEBAS-FASE-0.md)

## Compilar y ejecutar

En Windows x64 se necesita el SDK .NET 10 x64 para compilar. Para ejecutar los binarios dependientes del framework se necesita el **.NET 10 Desktop Runtime x64**, incluido en el SDK de Windows. El runtime básico por sí solo carece de WinForms. Los proyectos usan las bibliotecas del framework y no declaran paquetes NuGet. [Descargas oficiales de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)

Desde la raíz del repositorio, en PowerShell con cuenta estándar:

```powershell
dotnet build TaskbarStudio.slnx -c Release -p:PlatformTarget=x64
.\src\NativeTrial\bin\Release\net10.0-windows\NativeTrial.exe
.\src\FloatingDock\bin\Release\net10.0-windows\FloatingDock.exe
```

Si se usa un SDK portátil, establezca `$env:DOTNET_ROOT` al directorio del SDK y sustituya el comando `dotnet` por `& "$env:DOTNET_ROOT\dotnet.exe"` en esa sesión.

## Usar los prototipos

**A · Prueba nativa.** Consulte el estado, active o desactive la ocultación y observe el efecto en la barra. Cada escritura va seguida de una lectura del Shell. Puede restaurar el estado anterior, conservar la elección o cerrar la prueba activa para restaurarla. La ventana vuelve a consultar al recuperar el foco y mediante **Consultar estado**. La aplicación usa el contrato de mensajes del Shell con una ventana propia. [Consulta de estado](https://learn.microsoft.com/windows/win32/shell/abm-getstate) · [Cambio de estado](https://learn.microsoft.com/windows/win32/shell/abm-setstate)

Antes de solicitar un cambio, A guarda el estado previo en `%LOCALAPPDATA%\TaskbarStudio\NativeTrial\pending.v1`. Un archivo de sesión abierto en exclusiva impide que dos instancias se disputen esa recuperación. Tras una interrupción, volver a abrir A muestra la decisión pendiente. **Restaurar** verifica el estado y **Conservar** descarta el registro sin escribir en el Shell. Cerrar una sesión recuperada deja esa decisión disponible para después. Si Explorer está indisponible, vuelva a consultar cuando se haya recuperado.

**B · Dock.** Abra Bloc de notas, Calculadora o Explorador mediante clic, o recorra los botones con Tab y Mayús+Tab y active el botón enfocado con Espacio o Entrar. **Cerrar** y Escape terminan el proceso. La posición inicial se calcula en el área de trabajo y se recalcula al cambiar la pantalla. El dock usa una ventana normal, puede quedar cubierta por otras ventanas y no reserva espacio. Los iconos son dibujos geométricos propios. Los destinos deben estar disponibles en el equipo.

## Estructura y pruebas

| Ruta | Contenido |
|---|---|
| `TaskbarStudio.slnx`, `Directory.Build.props` | Solución, plataforma x64 y configuración común. |
| `src/NativeTrial/` | Interfaz, acceso al Shell y transacción local de recuperación. |
| `src/FloatingDock/` | Ventana, accesos, dibujos y cálculo de posición. |
| `tests/Phase0.Tests/` | Comprobación del ABI x64 y pruebas de lógica con Shell simulado y directorios temporales. |
| `tests/Test-NativeTrial.ps1`, `tests/Measure-Dock.ps1` | Pruebas de los ejecutables en un escritorio interactivo. |
| `docs/PRUEBAS-FASE-0.md` | Protocolo, mediciones realizadas y verificaciones pendientes. |
| `.gitignore` | Exclusión de binarios y archivos de compilación. |

```powershell
dotnet run --project tests/Phase0.Tests -c Release --no-build
.\tests\Test-NativeTrial.ps1
.\tests\Measure-Dock.ps1
```

El script de A consulta y cierra la ventana. En una sesión de prueba, añadir `-ExerciseChange` solicita cambiar temporalmente el ajuste real, verificarlo y restaurarlo. El script del dock realiza 50 ciclos de apertura y cierre, comprueba los cuatro botones a los DPI de la ventana y registra los handles y objetos GDI y USER de cada proceso. También compara el área de trabajo, la geometría y los indicadores de la barra primaria antes, durante y después. La pasada de reposo mide CPU, memoria privada y GPU frente a una referencia del escritorio. [Protocolo y resultados](docs/PRUEBAS-FASE-0.md)
