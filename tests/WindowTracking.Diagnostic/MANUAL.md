# Comprobación interactiva de WindowTracking

Desde la raíz del repositorio, compila Release x64 y ejecuta la herramienta:

```powershell
dotnet build TaskbarStudio.slnx -c Release -p:PlatformTarget=x64
dotnet run --project tests/WindowTracking.Diagnostic -c Release -- 180
```

La duración es configurable entre 10 y 600 segundos. También puedes terminar con Ctrl+C. El diagnóstico muestra identificadores, PID y nombre de proceso, sin títulos. No captura contenido, no guarda la salida en archivos y no envía acciones a las ventanas.

Mientras el diagnóstico permanece abierto:

1. Abre Bloc de notas, Calculadora y Explorador de archivos desde Inicio.
2. Comprueba que aparezca una línea `+` para cada ventana elegible.
3. Alterna entre las tres ventanas y comprueba los cambios `focus on` y `focus off`.
4. Minimiza y restaura Bloc de notas. Comprueba `minimized` y `restored`.
5. Cambia el título de Bloc de notas, por ejemplo creando un documento y guardándolo con otro nombre. Comprueba `title`.
6. Cierra cada aplicación y comprueba su línea `-`.
7. Abre también FloatingDock si no está abierto. No debe aparecer como ventana elegible. Una aplicación de terceros cuyo título contenga la palabra FloatingDock debe seguir siendo visible.
8. Termina el diagnóstico con Ctrl+C. Debe imprimir `Hooks released`.

La prueba completa requiere interacción con el escritorio. No se considera aprobada hasta realizar estos pasos en la sesión interactiva de Windows.
