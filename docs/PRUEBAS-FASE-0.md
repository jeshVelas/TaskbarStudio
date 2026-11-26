# Verificación de la Fase 0

## Decisión de publicación · 26 de noviembre de 2026

El dock B queda aprobado provisionalmente para la Fase 1. La Fase 0 sigue abierta por los criterios de GPU, recuperación e interacción detallados en este informe. Para la publicación se repitieron la recompilación Release x64 y la suite automatizada de ABI y lógica. Los tres ejecutables dieron `Machine=0x8664`, la compilación terminó con cero advertencias y errores, y pasaron los 13 casos. Las mediciones de escritorio corresponden a las sesiones documentadas a continuación.

## Verificación de cierre · 25–26 de noviembre de 2026

| Comprobación | Resultado en Windows 11 25H2, 26200.9974 |
|---|---|
| Recompilación Release x64 | SDK 10.0.404, `dotnet build TaskbarStudio.slnx -c Release -p:PlatformTarget=x64 -t:Rebuild`. Tres proyectos recompilados, 0 advertencias y 0 errores. Los tres ejecutables dieron `Machine=0x8664` (AMD64). |
| Automatización de ABI y lógica | `dotnet run --project tests/Phase0.Tests -c Release --no-build` completó 13/13. Los casos de interrupción y recuperación en esta suite usan un Shell simulado. |
| B, serie automatizada tras la permanencia | `Measure-Dock.ps1 -Cycles 50 -ForcedCloses 5 -CyclesOnly`: 45 cierres normales, cinco forzados y cero procesos residuales. Cuatro botones visibles y habilitados a 120 DPI. Primer lanzamiento de la serie 399 ms y P95 de los 49 posteriores **361 ms** (objetivo ≤ 750 ms). Cada ciclo conservó la barra y el área. Por proceso se observaron 355–358 handles, 25 objetos GDI y 31–32 USER. |
| A en el escritorio de Explorer | Se ejecutó `Test-NativeTrial.ps1 -ExerciseChange` en la posición original, a la izquierda. La ventana indicó ocultación desactivada y bloqueó ambos botones de escritura. La sonda independiente obtuvo, antes y después del cierre normal, área `(60,0,1920,1080)`, barra `(0,0,60,1080)` y `flags=0x0`. El archivo `pending.v1` estuvo ausente en ambas comprobaciones. |

Los 40 ciclos reales de escritura y restauración con la barra abajo pertenecen a la consolidación anterior. La ejecución nocturna verificó la protección de la posición original sin mover la barra.

### B · GPU del escritorio y del compositor

Tras medir el escritorio sin B, se abrió una instancia Release con `--load-test` (20 accesos) y se esperaron 60 s. En ambos tramos se consultó `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine`, agrupando por adaptador y motor y distinguiendo los PID de DWM y del dock. Se comparó también la geometría y la máscara de la barra en cada muestra.

| Tramo | Duración | Muestras GPU | Intervalos con entrada | Cambios de ventana en primer plano | Cambios de barra o área |
|---|---:|---:|---:|---:|---:|
| Referencia sin B | 601,0 s | 379 | 7 | 2 | 0 |
| B con 20 accesos | 601,3 s | 380 | 3 | 1 | 0 |

El mayor incremento medio por motor fue **0,0627 pp para el escritorio**. Para DWM, el mayor incremento positivo fue 0,0000 pp, con los incrementos negativos truncados a cero. El proveedor no expuso ningún motor con el PID de B durante la carga, por lo que su consumo directo queda sin dato. La actividad en ambos tramos excluye este par del recuento controlado.

Para la serie controlada se exigió escritorio desbloqueado, un monitor, alimentación de red estable, puntero fuera del dock, botón de cierre operativo al terminar y la misma área y barra. Se vigilaron la última entrada y la ventana en primer plano durante cada tramo. Cada carga empezó tras 60 s de estabilización con 20 accesos. Un cambio observado interrumpía el tramo.

| Par controlado | Referencia (s / muestras) | Carga (s / muestras) | Mayor incremento del escritorio (pp) | Mayor incremento de DWM (pp) |
|---|---:|---:|---:|---:|
| 1 | 600,1 / 370 | 601,1 / 400 | 0,0000 | 0,0000 |
| 2 | 600,3 / 377 | 601,5 / 364 | 0,1928 | 0,0000 |

En ambos pares, el escritorio cumplió el límite de ≤ 0,500 pp. El contador siguió sin exponer motores con el PID del dock. Se descartó un intento con 600,6 s de referencia y el puntero sobre la ventana al medir la carga. En el tercer par se obtuvieron 600,9 s de referencia (364 muestras), pero hubo entrada a los 23 s de carga. Otros dos intentos detectaron entrada al primer segundo y a los 24 s de referencia, el último después de esperar un minuto de inactividad. La lectura final de la barra coincidió con la inicial. **Quedaron completos dos de los tres pares controlados**. Faltan un par en una sesión reservada y la atribución mediante traza de la pasada anterior de **0,545 pp**.

### B · Estabilidad prolongada

La misma instancia con 20 accesos permaneció abierta desde las 21:37:28 hasta las 23:37:50, hora local (**7.221,2 s** de observación). Durante la carga se comprobó la supervivencia del proceso y se tomó una lectura de la barra por muestra. Después, las **218 comprobaciones** encontraron el proceso respondiendo, la ventana visible para Windows y el botón Cerrar habilitado. El intervalo máximo entre estas últimas comprobaciones fue de 31,0 s. La sonda observó cero cambios de barra o área, Cerrar terminó el proceso con código 0 y la lectura final coincidió con la inicial. Queda el recorrido manual de uso interactivo, incluida la vuelta desde pantalla completa.

### Revisión del repositorio

Se revisaron `git status`, el diff del árbol y el del índice. Para incluir los archivos sin seguimiento se compararon también contra el archivo vacío con `git diff --no-index`. De los 17 archivos candidatos, solo `README.md` tenía seguimiento y el índice estaba vacío. Todos eran texto. La revisión de nombres y contenido no encontró credenciales, correos personales, rutas de perfiles de usuario, binarios ni temporales destinados al repositorio. Los directorios `bin/` y `obj/` de los tres proyectos figuraban como ignorados. Los cambios de esta verificación se limitaron a `README.md` y este informe.

## Consolidación · 25 de noviembre de 2026

Windows 11 25H2 **26200.9974**, un monitor de 1920 × 1080, 120 DPI (125 %) medidos en la ventana del dock. SDK .NET x64 10.0.404, Desktop Runtime 10.0.15. Se usaron los binarios Release dependientes del framework en la sesión interactiva. La sonda en el escritorio aislado del ejecutor devolvió barra indisponible. Las pruebas con ventanas y contadores corresponden al escritorio de Explorer.

| Prueba de esta ejecución | Resultado |
|---|---|
| `dotnet build TaskbarStudio.slnx -c Release -p:PlatformTarget=x64` | Tres proyectos, 0 errores, 0 advertencias. Cabecera PE `Machine=0x8664` (AMD64) en los tres ejecutables. |
| `dotnet run --project tests/Phase0.Tests -c Release --no-build` | 13/13 aprobadas. Incluyen el ABI x64 de producción, la detección del borde inferior y la conservación del registro cuando el estado queda enmascarado. |
| `tests/Test-NativeTrial.ps1` | Consulta y cierre correctos. El formulario y la sonda coincidieron en ocultación desactivada. |
| `tests/Test-NativeTrial.ps1 -ExerciseChange` antes de proteger los bordes alternativos | **Falló la activación a la izquierda.** Devolución de SET `0x1`, bit solicitado `1`, bit leído inmediatamente `0`. Tras esperar hasta 5 s por el estado solicitado, la sonda independiente seguía leyendo `flags=0x0`. El script devolvió error después de verificar la recuperación. |
| Limpieza de A tras esa discrepancia | La acción Restaurar comprobó que ya estaba en el estado original. La sonda confirmó `autohide=False`, el mismo rectángulo y ausencia de `pending.v1`. Esta ejecución no ejercitó una escritura inversa efectiva. |
| A final con la barra a la izquierda | La consulta coincidió con la sonda. Los dos botones de escritura estaban deshabilitados, se mostró el motivo, no se creó registro y el cierre fue normal. También se verificó al terminar toda la sesión. |
| A final con la barra abajo | **40/40 ciclos reales aprobados**. Veinte desde activada y veinte desde desactivada. Cada ciclo abrió A, solicitó el valor opuesto, comprobó la lectura del Shell y el registro, restauró el estado inicial, comprobó la ausencia del registro y cerró. |
| Recuperación del escritorio de prueba | Se verificó primero la preferencia inferior original, activada. Configuración dejó de estar disponible al intentar la última reposición. Se reabrió la página y se verificaron la selección Izquierda, el área `(60,0,1920,1080)`, la barra `(0,0,60,1080)`, `flags=0x0` y la ausencia de registro. |

### Qué explica el resultado de A

El encabezado `um/shellapi.h` del SDK de Windows 10.0.26100.0 declara un mensaje `DWORD`, un puntero de entrada/salida a `APPBARDATA` y un resultado `UINT_PTR`. El adaptador usa `uint`, `ref AppBarData` y `nuint`. El SDK aplica el empaquetado de un byte solo fuera de Win64. En x64 el diseño secuencial ocupa **48 bytes**. Los desplazamientos de `cbSize`, `hWnd`, `uCallbackMessage`, `uEdge`, `rc` y `lParam` son 0, 8, 16, 20, 24 y 40. Los cuatro valores de `RECT` son enteros de 32 bits con signo. `HWND` y `LPARAM` tienen el tamaño de un puntero. [Estructura](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-appbardata) · [Función](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage)

| Mensaje | Interpretación usada |
|---|---|
| `ABM_GETSTATE = 0x04` | Con `cbSize` recibe una máscara. Cero es válido. `ABS_AUTOHIDE = 0x01`. Desde Windows 7 se omite `ABS_ALWAYSONTOP = 0x02`. [Contrato de lectura](https://learn.microsoft.com/en-us/windows/win32/shell/abm-getstate) |
| `ABM_GETTASKBARPOS = 0x05` | Devuelve verdadero si se obtuvo el rectángulo de la barra primaria. A consulta la disponibilidad antes y después de leer la máscara. La sonda usa el rectángulo en coordenadas de pantalla para identificar la posición. [Contrato de posición](https://learn.microsoft.com/en-us/windows/win32/shell/abm-gettaskbarpos) |
| `ABM_SETSTATE = 0x0A` | A proporciona `cbSize=48`, el HWND de su formulario y los bits solicitados en `lParam`. Microsoft especifica que el retorno siempre es TRUE. La aceptación de la prueba exige observar el nuevo bit mediante GET. [Contrato de escritura](https://learn.microsoft.com/en-us/windows/win32/shell/abm-setstate) |

El Shell devolvió `rc=(0,0,60,1080)` y el área de trabajo primaria fue `(60,0,1920,1080)`. En Configuración → Personalización → Barra de tareas, UI Automation leyó «Esta configuración solo está disponible cuando la barra de tareas está en la parte inferior» en la ayuda de la casilla de ocultación. UI Automation expuso la casilla como habilitada. La posición izquierda concuerda con la limitación que Microsoft describió al introducir las posiciones alternativas. [Nota de Microsoft sobre las posiciones alternativas](https://blogs.windows.com/windows-insider/2026/05/15/improving-windows-quality-making-taskbar-and-start-more-personal/)

Se cambió temporalmente la posición mediante Configuración en esa misma sesión. Antes de una nueva escritura de A, el borde inferior ya devolvía `rc=(0,1020,1920,1080)` y `flags=0x1`. El primer ensayo esperaba iniciar con el bit a cero y se detuvo en esa precondición. Se comprobó la ausencia de registro y se repitió con el estado inferior observado. A desactivó la ocultación, confirmó el bit a cero, restauró la ocultación y confirmó el bit a uno mediante una segunda sonda. Al volver a la izquierda reaparecieron el rectángulo y la máscara originales, con `autohide=False`. Esto aísla el efecto del borde en este equipo. La prueba no identifica cuándo ni quién fijó la preferencia que reaparece abajo.

Para evitar una escritura cuyo efecto quede enmascarado, en la compilación 26200 A comprueba la geometría de la barra primaria alrededor de cada lectura usada por la transacción. Si la barra está en otro borde, la ventana muestra el estado efectivo y explica la limitación. Una restauración pendiente se conserva al cerrar. Conservar sigue siendo una decisión explícita del usuario. En otras compilaciones se mantiene la verificación por lectura posterior.

### B · Arranque, aislamiento y recursos

Se ejecutó `tests/Measure-Dock.ps1` con sus valores por defecto en dos series consecutivas. En cada serie, los primeros cinco procesos se terminaron a la fuerza y los otros 45 mediante Cerrar. Cada PID terminó antes de la siguiente apertura. El primer dock de cada serie mostró los cuatro botones habilitados dentro de la ventana a 120 DPI. En los 100 ciclos coincidieron el área primaria, el rectángulo de la barra y la máscara del Shell al abrir y al cerrar. La ocultación estaba desactivada.

| Serie nueva | Primer arranque (ms) | P95 de los 49 siguientes (ms) | Handles por proceso | Objetos GDI | Objetos USER | Procesos de dock al final |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 630 | 399 | 331–357 | 25 | 29–32 | 0 |
| 2 | 379 | 403 | 355–357 | 25 | 31–32 | 0 |

Ambas series cumplieron el objetivo de P95 ≤ 750 ms. En la segunda, la primera y la última instancia tuvieron 356 handles, 25 objetos GDI y 32 USER. Se midieron además los objetos de **toda la sesión** antes de abrir el primer dock y tras los cierres 1, 25 y 50. GDI dio 3932 en las cuatro lecturas. USER dio 1752, 1758, 1758 y 1759. No se observó crecimiento proporcional al número de aperturas. Esos totales incluyen Explorer, UI Automation y las demás aplicaciones de la sesión. [Contadores de recursos gráficos de Windows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguiresources)

Con A cerrado y la barra abajo en ocultación activa se ejecutó `tests/Measure-Dock.ps1 -Cycles 6 -ForcedCloses 1 -CyclesOnly`. Los seis procesos terminaron, cinco mediante Cerrar. Todos presentaron 356 handles, 25 objetos GDI y 32 USER. La sonda conservó el área completa de 1920 × 1080, el rectángulo inferior y `flags=0x1`. Se comprobaron así los dos estados de ocultación en una barra real.

### B · Consumo en reposo con 20 accesos

Cada repetición nueva midió una referencia sin dock, estabilizó el dock durante 60 s y midió la carga sin interacción. La CPU se normaliza por los 16 procesadores lógicos. La memoria es privada comprometida. La GPU se expresa como el mayor aumento medio por motor de **todo el escritorio**, con mínimo de cero.

| Pasada | Referencia (s / muestras GPU) | Carga (s / muestras GPU) | CPU media | Memoria privada máxima | Aumento GPU (pp) |
|---|---:|---:|---:|---:|---:|
| Previa, según el registro original | 600 nominales / sin recuento | 600,6 / sin recuento | 0,001 % | 15,50 MiB | 0,142 |
| Repetición 1, inicio 13:52 local | 600,7 / 374 | 601,2 / 366 | 0,008 % | 18,34 MiB | 0,000 |
| Repetición 2, inicio 14:13 local | 600,4 / 395 | 601,4 / 372 | 0,001 % | 14,97 MiB | **0,545** |

Las tres pasadas cumplieron CPU ≤ 0,5 % y memoria privada ≤ 60 MiB. Cumplieron el objetivo GPU ≤ 0,5 pp dos de tres. La última lo superó en 0,045 pp. El script terminó con código 0 al concluir las mediciones y la limpieza. El criterio cuantitativo de GPU **falló** en esa pasada. El contador incluye toda la sesión y la muestra carece de atribución por proceso. Queda contrastar el exceso con una traza del dock y del compositor en un escritorio controlado.

## Entorno y resultados del ensayo anterior

Ejecución del 25 de noviembre de 2026 en Windows x64, compilación 26200. SDK portátil 10.0.404 y Windows Desktop Runtime 10.0.15. El host global solo tenía Microsoft.NETCore.App 3.1.32. Se usó el SDK oficial en una carpeta temporal. Compilación Release dependiente del framework, sin depurador.

Equipo Cyborg 15 A13VE con Intel Core i7-13620H, 16 procesadores lógicos, 15,71 GiB de RAM física disponible para Windows, pantalla de 1920 × 1080 y plan de energía Equilibrado. Gráficos Intel UHD con controlador 32.0.101.5972 y NVIDIA GeForce RTX 4050 Laptop con controlador 32.0.16.1656. La escala física de la sesión queda por confirmar en la matriz manual.

| Comprobación ejecutada | Resultado observado |
|---|---|
| Compilación de la solución | Tres proyectos, cero errores y cero advertencias. |
| Ejecutable de pruebas de lógica | Diez casos aprobados. Los dos primeros realizan 20 ciclos cada uno desde estados iniciales opuestos sobre un Shell simulado. |
| Posición calculada | Rectángulos de trabajo desplazados y escalas lógicas de 100, 125, 150 y 200 % dentro de la prueba de lógica. |
| Primera serie con ventanas reales de B | 50 aperturas, 45 cierres normales y cinco forzados. Coincidencia del área primaria consultada en cada ciclo. Primer botón Cerrar disponible a los 2494 ms. P95 de los 50 tiempos, incluido el primero, de 760 ms. |
| Segunda serie con ventanas reales de B | Otras 50 aperturas, cinco cierres forzados. La sonda independiente confirmó la misma área primaria y la misma ocultación antes, durante y después. Primer arranque de la serie 419 ms. P95 de los 49 posteriores 594 ms hasta observar habilitados Bloc de notas y Cerrar. Cumple el objetivo de 750 ms de esta serie. |
| Reposo real de B con 20 accesos | Una pasada de 600,6 s tras 60 s de estabilización. CPU media del proceso 0,001 %, memoria privada máxima muestreada 15,50 MiB. Mayor incremento medio de motor GPU sobre la referencia de diez minutos: 0,142 puntos porcentuales. Los tres valores cumplieron su umbral en esta pasada. |
| Consulta y cierre reales de A | La ventana y una consulta independiente al Shell coincidieron en ocultación desactivada. El proceso cerró normalmente. |
| Activación real de A | **Falló en este escritorio.** La solicitud ABM_SETSTATE devolvió un valor distinto de cero. La lectura inmediata y una lectura independiente cinco segundos después seguían indicando desactivada. También se probó dirigir la solicitud al identificador de la barra primaria, con el mismo resultado. Se descartó esa variante. |
| Recuperación real de A tras el fallo | Al reabrir la primera prueba se ofreció resolver el registro con el valor original. La comprobación final confirmó ocultación desactivada y ausencia de transacción pendiente. Queda por demostrar una restauración real desde el estado opuesto tras una escritura efectiva. |

Los fallos simulados comprenden escritura interrumpida, lectura posterior discrepante, recuperación en otra instancia, preferencia manual cambiada y registro ilegible. Los dos ejecutables de escritorio tienen puntos de entrada independientes. La prueba del ABI usa la estructura de producción. Los casos de lógica ejercitan la recuperación mediante un Shell simulado.

## Reproducción de las mediciones

Para reproducir el control de A, anotar la posición, elegir **Abajo** desde Configuración y consultar de nuevo la ocultación antes de tocarla. En este equipo cambió con la posición. Para cada estado inicial elegido en Configuración, ejecutar `1..20 | ForEach-Object { .\tests\Test-NativeTrial.ps1 -ExerciseChange }` y comprobar las 20 confirmaciones de cambio y restauración. Al terminar, restablecer primero la preferencia inferior y después la posición inicial. Verificar la geometría, la máscara y la ausencia de registro. En un borde alternativo de 26200, el script informa que se verificó el bloqueo de escritura.

1. Usar una cuenta estándar, una sesión interactiva y la compilación Release. Anotar compilación de Windows, versiones del SDK y Desktop Runtime, CPU, RAM física, GPU y controlador, monitores, resolución, escalado y plan de energía. Repetir en otra compilación compatible antes de afirmar compatibilidad general.
2. Para B, dejar el puntero fuera del dock, mantener la misma topología de pantalla y dejar el escritorio quieto. Ejecutar `tests/Measure-Dock.ps1` desde PowerShell. Realiza 50 aperturas, cinco cierres forzados y 45 mediante el botón Cerrar. Usa las consultas de solo lectura de `tests/DesktopProbe.cs` para comparar el área de trabajo primaria y la ocultación antes, durante y después. Espera la terminación de cada proceso. Si otro software cambia el área durante la prueba, repetir con el escritorio estable.
3. El cronómetro incluye creación del proceso y búsqueda por Windows UI Automation. El script comprueba que están habilitados el primer lanzador y Cerrar. Es una cota superior al tiempo del primer botón utilizable. El primer arranque de una pasada no equivale al primero después de reiniciar Windows. Para validar en frío, medir en cinco reinicios distintos y exigir ≤ 2 s en cada uno. Para arranques posteriores, evaluar p95 ≤ 750 ms con al menos 30 muestras posteriores al primer arranque.
4. El script mide una referencia del escritorio sin dock, abre el dock con `--load-test` (20 accesos con los mismos tres diseños), deja estabilizar 60 s y mide 600 s. Calcula la CPU del proceso como tiempo de procesador dividido por duración y procesadores lógicos. Registra el máximo muestreado de memoria privada comprometida. Objetivos por pasada: media de CPU ≤ 0,5 % y memoria ≤ 60 MiB.
5. Para GPU suma por motor los contadores de todos los procesos, incluido el compositor, promedia las muestras de cada tramo y toma el mayor incremento sobre su referencia. Objetivo ≤ 0,5 puntos porcentuales con el mismo escritorio. Los contadores WMI tienen granularidad limitada y pueden incluir actividad ajena. Si faltan, registrar «sin datos». Si la medida es marginal o el escritorio cambia, contrastar con una traza de GPU y compositor. Repetir el par referencia/carga tres veces sobre un equipo físico controlado.
6. Para una comprobación rápida se pueden indicar, por ejemplo, `-Cycles 6 -ForcedCloses 1 -Warmup 3 -Seconds 10`. La opción `-CyclesOnly` ejecuta las aperturas y cierres y omite la medición de reposo. Para registrar el primer arranque tras cada reinicio usar `-Cycles 1 -ForcedCloses 0 -CyclesOnly`. Registrar las duraciones efectivas.

## Registro manual pendiente

| Ensayo | Procedimiento y criterio de aceptación |
|---|---|
| Ocultación real | Observar la respuesta al puntero. Los 20 ciclos por estado ya se ejecutaron en 26200.9974. Repetir en otra compilación y comparar con Configuración. |
| Interrupciones de A | Probar cierre normal, terminación tras aplicar y relanzamiento. Cambiar también la preferencia desde Configuración mientras A está cerrada. Comprobar que el relanzamiento y el cierre de la ventana recuperada respetan esa preferencia. Probar por separado Restaurar y Conservar. |
| Interacción con el dock | La sonda ya verificó el aislamiento desde los dos estados de ocultación. Queda abrir los tres destinos con ratón y teclado y comprobar foco, alto contraste y Escape. |
| Escala y pantallas | Se comprobó una ventana real al 125 % y el cálculo de posición al 100, 125, 150 y 200 %. Solo había un monitor. Queda probar el traslado entre monitores al 100 % y 150 %, el cambio de principal y la desconexión, verificando que los botones sigan accesibles. |
| Explorer | En una sesión de prueba, reiniciar Explorer cinco veces con B abierto. Comprobar posición, navegación y lanzamiento al recuperarse. Con A abierto, consultar durante la indisponibilidad y confirmar el estado cuando regrese el Shell. |
| Suspensión y reanudación | `powercfg /a` confirmó espera moderna S0 en este equipo. Ejecutar cinco ciclos con alguien disponible para reanudar y desbloquear. Tras cada ciclo, comprobar que B responde, se puede cerrar y la barra sigue operable. |
| Uso prolongado y pantalla completa | Completar dos horas de uso y comprobar la vuelta desde una aplicación a pantalla completa. Registrar bloqueos, cierres inesperados, accesibilidad del dock y operabilidad de la barra. |
| Arranque en frío | Medir el primer lanzamiento de B tras cada uno de cinco reinicios de Windows, con inicio de sesión y el script indicado arriba. Cada medición debe ser ≤ 2 s. |
| Otra plataforma | Repetir A y B en una segunda compilación compatible de Windows 11 x64 y registrar versión, hardware y resultados. |
| Traza de GPU | Contrastar el exceso de 0,045 pp con los perfiles GPU y DesktopComposition en un escritorio reservado. WPR ofreció ambos perfiles en este equipo, pero al intentar iniciar la captura devolvió `0xc5585011` por falta de permiso para perfilar el sistema. Se requiere una sesión con ese permiso para obtener la traza. |

## Límites para interpretar la viabilidad

La ocultación es una preferencia compartida. La API pública permite leer un indicador y solicitar una escritura, pero no ofrece una transacción atómica con Configuración ni identifica el autor de un cambio. Si dos programas escriben simultáneamente, la lectura de verificación puede discrepar. A conserva la información de recuperación ante ese fallo y pide una decisión al reiniciar.

La transición y restauración de A quedaron verificadas en el borde inferior de 26200.9974. La comparación en otra compilación y la comprobación visual al acercar el puntero siguen pendientes.

Respaldan la aprobación provisional de B el aislamiento de la barra, el P95 de los arranques posteriores, el consumo medido de CPU y memoria y las dos horas de permanencia. La aceptación final de B sigue sujeta a GPU y a los ensayos de la matriz manual. A conserva el carácter de prototipo hasta validar la recuperación real y otra compilación.

En la sesión nocturna se registró actividad de escritorio y no se confirmó una ventana para interrumpir Explorer. Quedaron sin ejecutar los cinco reinicios del Shell, las cinco suspensiones con reanudación y los cinco primeros lanzamientos tras reiniciar Windows. Los dos últimos requieren coordinación para reanudar o iniciar sesión. El equipo de un solo monitor también limita las pruebas físicas con pantallas y DPI distintos.

La carga de 20 accesos repite tres iconos pequeños. Un dock con imágenes grandes o animaciones requerirá mediciones propias. Antes de ampliar el alcance, localizar en una traza cualquier incumplimiento reproducible de arranque o consumo y resolver primero los fallos de recuperación o compatibilidad.
