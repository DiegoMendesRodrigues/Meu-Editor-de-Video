# Manual del usuario — Meu Editor de Vídeo

Este manual explica cómo instalar, usar y configurar Meu Editor de Vídeo. Está escrito para quien nunca ha editado vídeo. Los nombres de menús y botones son los mismos que aparecen en pantalla.

Los textos en **negrita** son nombres de menús, botones y opciones. El símbolo › separa los niveles de un menú: **Archivo › Exportar…** significa abrir el menú Archivo y elegir Exportar.

## Primeros pasos

Meu Editor de Vídeo funciona en Windows 11 de 64 bits. Para instalarlo, use uno de los dos archivos de instalación:

- **MeuEditorDeVideo-…-Setup.exe**: instalador completo. Recomendado para la mayoría de las personas.
- **MeuEditorDeVideo-…-x64.msi**: paquete de Windows Installer, para quien prefiere la instalación estándar de Windows.

Ejecute el archivo, siga las pantallas y, al final, abra el programa desde el menú Inicio. No hace falta instalar nada más por separado.

> Si Windows muestra el aviso "Windows protegió su PC", haga clic en "Más información" y luego en "Ejecutar de todas formas". Este aviso aparece en programas sin certificado de firma digital.

### Requisitos del equipo

Estos son los requisitos previstos en la especificación del producto:

- **Mínimo:** Windows 11 de 64 bits, procesador Intel Core i5 o AMD Ryzen 5, 16 GB de RAM, tarjeta gráfica compatible con Direct3D y SSD de al menos 512 GB.
- **Recomendado:** Windows 11 de 64 bits, procesador Intel Core i7 o AMD Ryzen 7, 32 GB de RAM o más, tarjeta gráfica NVIDIA RTX o AMD Radeon con 8 GB de memoria de vídeo o más y SSD NVMe de 1 TB o más.

No es obligatorio tener una tarjeta gráfica dedicada, pero hace que la vista previa, la exportación y las funciones de inteligencia artificial sean mucho más rápidas.

### Al abrir el programa

La primera vez (o si falta alguna preferencia o está vacía en el registro de Windows), aparece la ventana **Bienvenido a Meu Editor de Vídeos**. Rellene **Nombre completo**, **Idioma del sistema**, **Tema** (Claro, Oscuro o Seguir el sistema operativo) y **Buscar nuevas actualizaciones cada mes** (Sí o No) y haga clic en **Guardar las preferencias**. Todos los campos son obligatorios y la ventana solo se cierra después de guardar. Si elige otro idioma, el programa se reinicia en él. Después puede cambiar las preferencias en **Herramientas › Configuración… › General**.

El programa se abre directamente en la pantalla de edición, con un proyecto nuevo y vacío llamado "Sin título". Si existe una copia de un proyecto que no se cerró correctamente, el programa pregunta si desea recuperarla (vea la sección Guardar, guardado automático y recuperación).

Los cinco últimos proyectos abiertos aparecen en el menú **Archivo**, entre separadores. Haga clic en uno para abrirlo. Si el archivo se movió o se eliminó, el programa avisa y lo quita de la lista.

### Crear un proyecto

1. Elija **Archivo › Nuevo proyecto…**.
2. Escriba un nombre en "Nombre del proyecto".
3. Elija la carpeta donde se guardará el proyecto en "Carpeta del proyecto" (use **Examinar…** si lo desea).
4. Elija una "Plantilla de proyecto". Las plantillas ya traen la resolución, la relación de aspecto y la frecuencia de fotogramas indicadas. Hay plantillas para vídeo horizontal (Full HD, 4K y cine), vertical y cuadrado, y plantillas para plataformas como YouTube, Instagram, TikTok, WhatsApp y Facebook.
5. Si lo prefiere, ajuste manualmente la resolución, la relación de aspecto, la frecuencia de fotogramas (FPS) y el perfil de color.
6. En "Almacenamiento de los medios", elija una opción:
   - **Vincular medios (los archivos se quedan donde están)**: el proyecto guarda solo la ruta de los archivos. Es lo más ligero, pero los archivos deben seguir en el mismo lugar.
   - **Empaquetar medios en el proyecto (.med independiente)**: los medios se copian dentro del proyecto. Úselo si quiere llevar el proyecto a otro equipo.
7. Haga clic en **Crear proyecto**.

### Abrir un proyecto existente

Hay tres maneras:

- Elija **Archivo › Abrir…** y seleccione el archivo .med.
- Haga clic en uno de los proyectos recientes del menú **Archivo**.
- Haga doble clic en el archivo .med en el Explorador de archivos. El programa abre el proyecto automáticamente.

## La pantalla de edición

Después de crear o abrir un proyecto, aparece la pantalla de edición. Está dividida en áreas:

- **Barra superior** (arriba de la ventana): a la izquierda, los menús **Archivo**, **Editar**, **Ver**, **Proyecto**, **Herramientas** y **Ayuda** y los botones **Guardar**, **Deshacer** y **Rehacer**; en el centro, el nombre del proyecto; a la derecha, **Tema**, **Cola de renderizado**, **Exportar** y los botones para minimizar, maximizar y cerrar la ventana. Arrastre la barra para mover la ventana y haga doble clic en ella para maximizar o restaurar.
- **Biblioteca de medios** (a la izquierda): arriba están las pestañas Biblioteca de medios, Vídeos, Imágenes, Audios, Elementos gráficos, Textos, Transiciones, Efectos, Color e IA. Pase el ratón sobre una pestaña para ver el nombre completo. Los medios importados aparecen en una cuadrícula, con miniatura y duración.
- **Vista previa** (en el centro, arriba): muestra el resultado del vídeo. Arriba están la resolución de la vista previa, el botón Proxy y el botón **Maximizar el monitor**, que hace que la vista previa cubra la biblioteca y la línea de tiempo (el panel Propiedades sigue visible; haga clic de nuevo para restaurar); abajo, la barra de posición (arrástrela para ir a cualquier punto), los botones de reproducir, pausar y avanzar fotograma a fotograma, el volumen, la captura de fotograma (guarda el fotograma actual en PNG o JPEG, a la resolución del proyecto; también en **Archivo › Exportar fotograma actual…**) y la pantalla completa.
- **Propiedades** (a la derecha): muestra la información y la configuración del elemento seleccionado. Está dividida en pestañas: **Proyecto** (resolución, frecuencia de fotogramas, duración, cantidad de medios y clips, archivo), **Medio**, **Vídeo**, **Texto**, **Composición**, **IA**, **Audio**, **Efectos**, **Color** e **Historial**. En la esquina inferior derecha, los accesos **Manual del usuario** y **Asistente virtual** abren la ayuda con un clic.
- **Línea de tiempo** (abajo): donde monta el vídeo. Cada fila horizontal es una pista. Hay una barra de herramientas justo encima.

> Cuando el nombre del proyecto aparece con la marca "● sin guardar", hay cambios que todavía no se han guardado. Use **Archivo › Guardar** o Ctrl+S.

### Menús

- **Archivo**: nuevo proyecto, **nuevo a partir de plantilla**, abrir, importar medios, importar secuencia de imágenes, guardar, guardar como, crear copia de seguridad, exportar, cola de renderizado, exportar fotograma actual (PNG o JPEG) y los cinco proyectos recientes.
- **Editar**: deshacer y rehacer; seleccionar todo; dividir; **unir los cortes** (deshace una división: haga clic en la regla sobre el punto del corte; solo está disponible cuando los dos lados son la continuación del mismo medio, sin ajustes distintos); cortar, copiar, pegar en el cursor y duplicar; eliminar, eliminar y cerrar el hueco (solo en la pista del clip) y **eliminar y unir** (cierra el hueco en todas las pistas, manteniendo la sincronía; las pistas bloqueadas no se mueven y la operación se rechaza si un clip de otra pista ocupa parte del tramo eliminado).
- **Ver**:
  - **Modo del diseño**: **Predeterminado**; **Línea de tiempo** (la línea de tiempo ocupa todo el ancho); **Vídeo** (el monitor ocupa la mitad derecha, a toda la altura).
  - **Calidad de reproducción**: Automática, Completa, 1/2 o 1/4.
  - **Nivel de zoom del vídeo**: Ajustar (cabe en el monitor) o del 10% al 400%; por encima del tamaño del monitor aparecen barras de desplazamiento.
  - **Reproducir/Pausar**, **Detener** (vuelve al punto donde empezó la reproducción) e **Intervalo de reproducción en bucle** (repite la selección de la línea de tiempo o, sin selección, el proyecto entero).
  - **Regla**: reglas en píxeles del vídeo arriba y a la izquierda de la imagen.
- **Proyecto**: empaquetar medios en el proyecto, **reencuadrar automáticamente**, **guardar como plantilla**, **capa de ajuste**, **clip compuesto**, **pantalla dividida**, **multicámara** y localizar medios ausentes (vea [Capas de ajuste, clips compuestos, pantalla dividida y multicámara](#capas-de-ajuste-clips-compuestos-pantalla-dividida-y-multicámara)).
  - **Guardar como plantilla…** guarda una copia del proyecto guardado en sus plantillas. **Archivo › Nuevo a partir de plantilla…** elige una plantilla (suya o de un paquete), pregunta dónde guardar el proyecto nuevo y abre la copia; la plantilla queda intacta.
  - **Reencuadrar automáticamente…** cambia el formato del proyecto (9:16 para Reels, TikTok y Shorts; 1:1; 4:5; 16:9) y recoloca cada clip de vídeo e imagen para cubrir el nuevo encuadre. En **Seguir**, elija **Cara**, **Cuerpo entero** o **Centro del encuadre**. Para seguir a la persona, el programa usa el modelo de eliminación de fondo y crea fotogramas clave de posición, que puede ajustar después en la pestaña Vídeo. Los textos quedan como están. El progreso aparece en la pestaña **IA**, y todo se deshace con un solo Ctrl+Z.
- **Herramientas**: mezclador de audio, editor de subtítulos, importar y exportar subtítulos, **texto a voz**, **analizar medios**, **grabar pantalla, webcam y micrófono**, **paquetes de contenido**, modelos de IA y configuración.
  - **Grabar pantalla, webcam y micrófono…** graba el escritorio, una webcam en una esquina de la pantalla y el sonido de un micrófono, todo a la vez, en un archivo MP4. Con más de un monitor, elija en **Pantalla a grabar** qué pantalla grabar (o todas). La webcam ocupa una décima parte del ancho de la pantalla grabada. Al hacer clic en **Grabar**, el programa se minimiza y aparece una cuenta atrás 5, 4, 3, 2, 1 en el centro de la pantalla antes de empezar a grabar. Para detener, pulse **F10** (funciona con cualquier programa en primer plano); el programa vuelve a aparecer. La grabación va a la carpeta **Grabaciones** (junto al proyecto o en Vídeos › Meu Editor de Vídeo), entra en la biblioteca (categoría Grabaciones) y, si la opción está marcada, en la línea de tiempo en el cursor. Sin la pantalla, graba solo la webcam.
  - **Paquetes de contenido…** instala, exporta y elimina paquetes de pegatinas, títulos y plantillas de proyecto (archivos .medpack). Un paquete es un ZIP con las carpetas `figurinhas` (PNG, GIF, WebP, JPEG o SVG), `titulos` (.medtitle) y `modelos` (.med) y, opcionalmente, un `pacote.json` con el nombre (por ejemplo `{"nome": "Fiesta de verano"}`). Los elementos que usted guarda quedan en el paquete **Mis elementos**; use **Exportar…** para compartirlo.
  - **Texto a voz…** convierte un texto en narración con las voces instaladas en Windows, sin internet. Escriba el texto, elija la **Voz** y la **Velocidad** y haga clic en **Generar e insertar**: el audio va a la biblioteca (categoría Narración) y se coloca en el cursor, en la primera pista de audio libre o en una pista nueva llamada Narración. Para tener más voces, instálelas en **Configuración de Windows › Hora e idioma › Voz**.
  - **Analizar medios** pone etiquetas a todos los medios de la biblioteca: tipo, orientación (horizontal, vertical, cuadrado), resolución (HD, Full HD, 4K), duración (corto, largo) y sonido. Con los modelos instalados, también **voz** o **sin voz**, el número de **escenas** y **persona** o **sin personas**. Las etiquetas aparecen al pasar el ratón sobre el medio, y el cuadro **Buscar…** de la biblioteca también busca en ellas.
- **Ayuda**: **Manual del usuario** (F1), **Asistente virtual** (Mayús+F1), **Atajos de teclado…**, **Información del sistema…**, **Abrir carpeta de registros**, **Licencias de terceros**, **Acerca de Meu Editor de Vídeo…**, **Buscar actualizaciones** (consulta por internet si hay una versión nueva: muestra "¡Tienes la versión más reciente!" o la ventana **Nueva versión disponible**, con la versión, los idiomas, los sistemas compatibles, el autor y los enlaces de descarga en Microsoft Store y en el sitio AplicativosIA.Online; al hacer clic en **Cerrar esta ventana**, se guarda la fecha de la comprobación). En **Acerca de Meu Editor de Vídeo…**, el icono de LinkedIn abre el perfil del autor en el navegador.

El **Manual del usuario** abre este manual dentro del programa, sin internet. La lista de la izquierda lleva a cada tema y Ctrl+F busca una palabra. La ventana puede quedar abierta mientras edita.

**Atajos de teclado…** muestra los atajos en uso, incluidos los que usted cambió. **Información del sistema…** muestra la versión, Windows, el procesador, la memoria, la tarjeta gráfica, los modelos de IA y las carpetas del programa; el botón **Copiar** lo lleva todo al portapapeles, útil al pedir ayuda.

## Importar medios

Importar es traer al proyecto los archivos que va a usar, como vídeos, fotos, música y grabaciones. Los archivos originales no se modifican.

### Formatos admitidos

- **Vídeo**: MP4, M4V, MOV, MKV, WebM, AVI, WMV, FLV, MPG y MPEG, M2TS, MTS, TS, 3GP, OGV, MXF y VOB.
- **Audio**: MP3, WAV, AAC, M4A, FLAC, OGG, OGA, Opus, WMA, AIFF, AIF, AC3 y AMR.
- **Imagen**: JPG y JPEG, PNG, BMP, TIF y TIFF, WebP, TGA, JFIF y SVG.
- **GIF animado**: GIF.
- **Secuencia de imágenes**: una serie de fotos numeradas, como foto_0001.png, foto_0002.png, etc.

Los archivos de otros formatos se rechazan con el mensaje "Formato de archivo no admitido.".

### Cómo importar

1. Haga clic en **Importar medios** en el panel de la biblioteca, o use **Archivo › Importar medios…** (atajo Ctrl+I).
2. Elija uno o varios archivos y confirme.
3. Siga el progreso. El mensaje "Importando 3 de 10" indica cuántos archivos se han leído.

También puede arrastrar archivos o carpetas desde el Explorador de archivos y soltarlos sobre la biblioteca de medios. Cuando el archivo está sobre el área correcta, aparece la indicación "Suelte para importar".

Para una secuencia de imágenes, use **Archivo › Importar secuencia de imágenes…**. El programa reconoce la numeración automáticamente.

Cada medio importado recibe una miniatura. Al seleccionar un medio, la pestaña **Medio** de las propiedades muestra la duración, la resolución, la frecuencia de fotogramas y otros datos del archivo.

### Organizar la biblioteca

- Use el cuadro **Buscar…** para encontrar un medio por nombre, categoría o por las etiquetas creadas en **Herramientas › Analizar medios** (por ejemplo "voz", "persona" o "vertical").
- Haga clic derecho en un medio para **Cambiar nombre…**, **Definir categoría…**, **Quitar categoría**, **Añadir a la línea de tiempo** o **Quitar del proyecto**.
- Quitar un medio del proyecto no borra el archivo del equipo.

### Medios ausentes

Si un archivo se movió, cambió de nombre o se eliminó, el proyecto muestra el aviso "No se encontraron algunos medios del proyecto" al abrirse. El aviso también aparece en la barra de estado como "Medios ausentes", con la cantidad de medios afectados. Para resolverlo:

1. Haga clic en **Localizar…** para indicar el archivo, o en **Buscar en carpeta…** para que el programa busque en las carpetas y subcarpetas que elija.
2. Si no quiere localizarlos ahora, haga clic en **Continuar sin los medios**. Los tramos quedan marcados como ausentes y pueden localizarse después.

Puede abrir esta ventana en cualquier momento en **Proyecto › Localizar medios ausentes…**. En un proyecto descargado de internet, el programa no busca medios ni LUT que estén en otro equipo de la red, porque solo con consultar ese equipo ya le enviaría sus datos de acceso de Windows: esos medios aparecen como ausentes y esas LUT quedan desactivadas. Si confía en el origen del proyecto, localice los medios en esta ventana y active las LUT en el panel **Propiedades**.

Para evitar este problema al llevar el proyecto a otro equipo, use **Proyecto › Empaquetar medios en el proyecto…**. Copia los medios dentro del archivo del proyecto.

### Proxies (versiones ligeras para editar)

Un proxy es una copia ligera de un vídeo grande, en 540p, usada solo para que la vista previa sea más fluida. La exportación siempre usa el archivo original.

- Para crear un proxy, haga clic derecho en el vídeo de la biblioteca y elija **Generar proxy de edición (540p)**.
- Para borrar un proxy, elija **Eliminar proxy**.
- En el monitor, la opción **Usar proxies en la vista previa** activa o desactiva su uso.
- En **Herramientas › Configuración…**, puede pedir que los proxies se generen automáticamente para vídeos de más de 1080p.

## Editar en la línea de tiempo

La línea de tiempo es donde se monta el vídeo de izquierda a derecha. El **cursor** (una línea vertical) muestra el instante que se ve en la vista previa. Los trozos de medios colocados en las pistas se llaman clips.

### Colocar medios en la línea de tiempo

- Arrastre un medio de la biblioteca a una pista.
- O seleccione el medio en la biblioteca y haga clic en **Añadir a la línea de tiempo**, en la barra de herramientas.

### Mover, recortar y dividir

- **Mover**: arrastre el clip a otro lugar de la pista o a otra pista del mismo tipo.
- **Recortar el inicio o el final**: arrastre el borde del clip para acortar o alargar el tramo usado.
- **Dividir**: coloque el cursor en el punto deseado y haga clic en **Dividir** en la barra de herramientas, o pulse S. El clip se corta en dos en ese punto.
- **Unir los cortes** (menú Editar): vuelve a unir las dos partes de un clip dividido. Coloque el cursor sobre el punto del corte; solo funciona cuando las dos partes son la continuación del mismo medio, sin ajustes distintos.
- **Eliminar**: seleccione el clip y pulse Supr. También puede usar el botón **Eliminar selección**.
- **Eliminar y cerrar el hueco**: borra el clip y desplaza los clips siguientes para llenar el vacío. Use Mayús+Supr.
- **Copiar, cortar, pegar y duplicar**: use Ctrl+C, Ctrl+X, Ctrl+V y Ctrl+D. Al pegar, el clip va a la posición del cursor.

### Imán y edición en cascada

- **Imán de alineación** (tecla N): cuando está activado, el clip "se pega" a los bordes de otros clips y al cursor, lo que facilita encajar las partes sin dejar huecos. El botón está en la barra de herramientas.
- **Edición en cascada (ripple)**: cuando está activada, mover o recortar un clip empuja los clips siguientes, sin dejar huecos.

### Pistas

Las pistas son las filas horizontales de la línea de tiempo. Para añadir una pista, haga clic en el botón **Añadir pista** y elija el tipo:

- **Pista de vídeo**
- **Pista de audio**
- **Pista de imagen**
- **Pista de texto**
- **Pista de subtítulos**
- **Pista de efectos**

Cada pista tiene tres botones en la cabecera de la izquierda:

- **Ocultar**: oculta la pista en la vista previa.
- **Silenciar**: silencia la pista de audio.
- **Bloquear**: impide cambios en la pista, para no tocar lo que ya está listo.

Haga clic derecho en el nombre de la pista para **Cambiar nombre de la pista…** o **Quitar pista**.

### Agrupar y vincular

- **Agrupar** (Ctrl+G): une varios clips para que se muevan juntos. Use **Desagrupar** (Ctrl+Mayús+G) para separarlos.
- **Vincular audio y vídeo**: hace que el audio de un vídeo siga al vídeo. Use **Desvincular audio y vídeo** para separarlos.

### Velocidad del clip

Haga clic derecho en el clip y elija **Velocidad…** para hacerlo más rápido o más lento. En la pestaña **Vídeo** de las propiedades, la lista de velocidades va de 0,25× (cámara lenta) a 8× (acelerado).

### Activar y desactivar clips

Haga clic derecho en el clip y elija **Activar/desactivar clip**. Un clip desactivado sigue en la línea de tiempo (atenuado y con el símbolo ⊘), pero no se ve ni se oye en la reproducción ni en la exportación.

### Capas de ajuste, clips compuestos, pantalla dividida y multicámara

- **Capa de ajuste** (**Proyecto › Añadir capa de ajuste**): crea en el cursor un clip de 5 segundos en una pista de efectos. Los efectos y la corrección de color colocados en él se aplican a **todas las pistas de abajo** durante el tiempo del clip. Seleccione la capa y añada efectos en las pestañas **Efectos** y **Color**. La opacidad de la capa dosifica el efecto y las máscaras limitan el área. Estire o mueva la capa como cualquier clip.
- **Clip compuesto** (**Proyecto › Crear clip compuesto…** o menú contextual): junta los clips seleccionados (con sus audios vinculados) en un único clip, que se mueve, se recorta y recibe efectos como uno solo. Las pistas y posiciones originales se guardan dentro de él. **Deshacer clip compuesto** devuelve los clips a la línea de tiempo, en pistas nuevas, solo con el tramo que mostraba el clip compuesto. El sonido del clip compuesto solo suena a velocidad normal.
- **Pantalla dividida** (**Proyecto › Pantalla dividida**): seleccione de 2 a 4 clips de vídeo o imagen, cada uno en una pista distinta, y elija un diseño: **2 lado a lado**, **2 apilados**, **3 columnas**, **3 franjas**, **1 grande y 2 pequeños** o **4 en cuadrícula**. Cada clip (en el orden de las pistas, de arriba abajo) se amplía para cubrir su parte del encuadre y se recorta el exceso. Ajuste después la posición y el recorte en la pestaña Vídeo.
- **Multicámara** (**Proyecto › Multicámara**): ponga cada cámara en una pista de vídeo, sincronizadas (por ejemplo, con **Sincronizar por el audio**). Durante la reproducción o parado, elija **Cámara 1** a **Cámara 4** (Alt+1 a Alt+4, con el foco en la línea de tiempo) para mostrar esa cámara **a partir del cursor**: el programa corta todas las cámaras en el cursor y desactiva las demás. La cámara 1 es la pista de vídeo más alta con clip en el cursor. El audio no cambia.

### Color del clip

Para identificar clips de forma visual, seleccione uno o varios clips, haga clic derecho y elija **Color del clip**. El color se guarda en el proyecto y puede deshacerse con Ctrl+Z. Elija **Automático (por tipo)** para volver al color predeterminado (vídeo, imagen, audio o texto).

Los proyectos con colores de clip usan el formato 1.3. Las versiones anteriores del programa abren estos proyectos con un aviso e ignoran los colores.

### Marcadores

Los marcadores son recordatorios en la regla de tiempo, útiles para anotar puntos importantes, como el inicio de una frase.

- Coloque el cursor y haga clic en **Añadir marcador** o pulse M.
- Haga clic derecho en el marcador para **Cambiar nombre del marcador…** o **Quitar marcador**.

### Zoom de la línea de tiempo

Use los botones de zoom de la barra de herramientas, o los atajos:

- Acercar: Ctrl + tecla más (o + del teclado numérico)
- Alejar: Ctrl + tecla menos (o - del teclado numérico)
- Ajustar a la ventana: Mayús+Z

### Deshacer y rehacer

- **Deshacer** (Ctrl+Z) y **Rehacer** (Ctrl+Y o Ctrl+Mayús+Z) retroceden y avanzan en las últimas acciones.
- La pestaña **Historial** de las propiedades muestra la lista de acciones realizadas. Haga clic en una para volver el proyecto a ese punto.

### Navegar por el vídeo

- Espacio: reproduce o pausa.
- Flechas izquierda y derecha: retroceden o avanzan un fotograma.
- Mayús con las flechas: retroceden o avanzan un segundo.
- Arriba y abajo: van al corte anterior y al siguiente.
- Inicio y Fin: van al principio y al final.

## Efectos visuales, texto y transiciones

Para aplicar un efecto, seleccione el clip en la línea de tiempo y use las pestañas de las propiedades. También puede usar las secciones de la biblioteca, como **Efectos**, **Color** y **Transiciones**: haga doble clic en un elemento para aplicarlo en el cursor o al clip seleccionado, o use el botón **Aplicar al clip seleccionado**.

### Pestaña Vídeo

Esta pestaña reúne la configuración de movimiento y tiempo del clip:

- **Transformación**: posición X e Y, escala, rotación y opacidad. Junto a cada valor hay un botón ◇ que crea un fotograma clave.
- **Fotogramas clave**: un fotograma clave guarda un valor en un instante concreto. Cuando hay fotogramas clave, el programa cambia el valor gradualmente entre ellos. Use **Fotograma clave anterior** y **Siguiente fotograma clave** para moverse entre ellos, e **Interpolación** para elegir la curva: Suave (entrada y salida), Lineal, Acelerando, Desacelerando o Mantener (salto).
- **3D (perspectiva)**: giro X, giro Y y profundidad, para dar sensación de profundidad al clip. El botón **Girar tarjeta 3D** hace que el clip gire como una tarjeta. No es un modelo 3D completo.
- **Animaciones listas**: efectos de movimiento listos para aplicar, como entrar por la izquierda, aparecer con zoom, Ken Burns (zoom lento), pulsar, girar, balancear, rebotar y aparecer y desaparecer.
- **Recorte**: recorta los bordes del vídeo por la izquierda, arriba, la derecha y abajo. Los controles solo ocultan los bordes; el tamaño del proyecto sigue igual. Para que el vídeo tenga realmente el tamaño recortado, haga clic en **Aplicar recorte al vídeo**: el proyecto pasa a tener el tamaño del área recortada, en píxeles del vídeo original (una grabación de 1920×1080 sin el 25% de la izquierda queda en 1440×1080), el área recortada llena el encuadre y la exportación sale con esas dimensiones. Los textos y los demás clips acompañan. El clip no puede estar girado ni tener fotogramas clave de posición o escala. Ctrl+Z lo deshace.
- **Mezcla**: modo de fusión con otras capas, como Multiplicar, Trama, Superponer, Luz suave, Sumar, Oscurecer, Aclarar y Diferencia.
- **Fundidos**: **Entrada gradual** y **Salida gradual**, de 0 a 10 segundos, para que el clip aparezca y desaparezca suavemente.
- **Velocidad**: velocidades predefinidas u otra velocidad. Por debajo de 1×, la opción **Cámara lenta suavizada** mezcla fotogramas vecinos para reducir los saltos. **Reproducir al revés** muestra el clip del final al principio, con el audio vinculado también invertido (también en el menú contextual del clip; el clip recibe el símbolo ◀ en la línea de tiempo). No se aplica a fotogramas congelados. En vídeos largos, use proxies para que la reproducción al revés sea fluida.
  - **Cámara lenta por interpolación (flujo óptico)**: por debajo de 1×, crea los fotogramas intermedios siguiendo el movimiento, lo que es más fluido que la simple mezcla. No necesita modelo, pero hace más lentas la reproducción y la exportación.
  - **Curva**: curva de velocidad (speed ramping). Elija **Montaje**, **Héroe**, **Bala**, **Salto**, **Acelerar** o **Desacelerar** para que el clip acelere y desacelere dentro de sí mismo, sin cambiar la duración ni el tramo del vídeo usado. **Constante (sin curva)** vuelve a lo normal. Con curva, el audio del clip queda silenciado. Recortar o dividir el clip también vuelve a la velocidad constante.
  - **Desenfoque de movimiento**: estela de movimiento cuando la posición, la escala o la rotación están animadas con fotogramas clave. Al 100%, la estela cubre el intervalo de un fotograma entero.
- **Congelar fotograma**, **Repetir tramo**, **Rellenar encuadre**, **Girar 90°** y **Voltear** en horizontal y en vertical.
- **Transición de entrada**: aplica una transición entre este clip y el clip anterior de la misma pista. Para usarla, los dos clips deben estar juntos. Usted elige la transición y la **Duración de la transición**.

- **Seguimiento de cámara**: elija **Cabeza** o **Cuerpo**, y una cámara virtual sigue esa parte de la persona. Ajuste:
  - **Corte automático (zoom)**: acerca la imagen al objetivo.
  - **Seguir el movimiento**: cuánto sigue la cámara al objetivo (al 0%, se queda en el centro).
  - **Agitar**: temblor de cámara alrededor del objetivo.
  - **Fondo desenfocado**: desenfoca todo lo que no es la persona.

  El seguimiento usa el mismo modelo de recorte de persona que la pestaña **IA** (instálelo en **Modelos de IA** si aún no lo tiene). Sin el modelo o sin persona en el encuadre, la cámara se queda en el centro y el fondo no se desenfoca. El efecto también aparece en la lista de efectos aplicados de la pestaña **Efectos**.
- **Retocar**: retoque del rostro de la persona, fotograma a fotograma. Haga clic en **Belleza**, **Maquillaje** o **Difuminar rostros** para añadir el efecto al clip; los ajustes aparecen justo debajo.
  - **Difuminar rostros**: encuentra el rostro en cada fotograma y lo oculta con **Desenfoque**, **Pixelar** o **Franja negra**. Ajuste la **Intensidad** y el **Tamaño del área**. Funciona con un rostro por fotograma.
  - **Belleza**: **Piel · suavizar** y **Piel · aclarar**, **Líneas de expresión** y **Bolsas bajo los ojos** (suavizan esas zonas), y ajustes de forma: **Rostro · afinar**, **Ojos · agrandar**, **Nariz · afinar**, **Boca · tamaño** y **Cejas · levantar** (los valores negativos hacen lo contrario).
  - **Maquillaje**: elija un **Look** listo (Natural, Glamour, Noche o Rosado) o **Ninguno**, y ajuste cada elemento: **Pestañas**, **Sombra de ojos**, **Delineador**, **Cejas**, **Pintalabios**, **Colorete**, **Contorno**, **Iluminador**, **Color de ojos** y **Pecas**. La sombra, las cejas, el pintalabios, el colorete y el iris tienen color propio. Un elemento con valor superior al 0% usa su valor y su color; con 0%, vale lo que define el look.

  El retoque usa el modelo **Face Mesh V2**, que localiza 478 puntos del rostro. Instálelo en **Herramientas › Modelos de IA › Descargar modelos…** (unos 5 MB). Si el modelo de recorte de persona está instalado, ayuda a encontrar la cabeza en el encuadre. Sin el modelo Face Mesh o sin rostro visible, el fotograma queda como está. El retoque funciona mejor con el rostro de frente y ocupando buena parte de la imagen.

Las transiciones disponibles son: Corte seco, Disolver, Fundido a negro, Zoom, Deslizar (a la izquierda, a la derecha, arriba y abajo), Empujar (a la izquierda y a la derecha), Cortinilla (a la izquierda, a la derecha, arriba y abajo), Círculo, Rombo, Reloj y Persianas.

### Efectos

La pestaña **Efectos** tiene dos partes. Arriba, una cuadrícula con una miniatura de cada efecto, que muestra el resultado en un paisaje de ejemplo: haga clic en una para añadir el efecto al clip. Abajo, la lista **Efectos aplicados**, en el orden en que se procesan. Cada efecto de la lista tiene controles deslizantes para los ajustes y los botones **Subir**, **Bajar** y **Quitar efecto**. La flecha a la izquierda del nombre muestra u oculta los ajustes. Haga doble clic en un control deslizante para volver al valor predeterminado. En los ajustes de color, una franja de color bajo el control muestra el efecto del valor (por ejemplo, de azul a naranja en la temperatura).

Efectos visuales disponibles:

- **Desenfoque**, **Nitidez**, **Viñeta**, **Blanco y negro**, **Sepia**, **Negativo**, **Pixelar**, **Granulado**, **Posterizar** y **Brillo suave**.
- **Croma**: elimina un fondo verde, azul o de otro color elegido. Tiene tolerancia, suavidad de la transición, suavizado de bordes, eliminación del reflejo de color y la opción de dejar el fondo transparente o de color sólido.
- **Refinar bordes**: contrae o expande el borde de la transparencia, suaviza y limpia el color de los bordes. Úselo después del croma o de una máscara.
- **Reencuadrar 360°**: convierte un vídeo 360° (equirrectangular, con ancho igual al doble de la altura) en una vista normal. Ajuste la **Dirección**, la **Inclinación**, el **Giro** y el **Campo de visión** y elija la **Proporción de la vista** (16:9, 9:16, 1:1, 4:3 o 4:5). Cree fotogramas clave en estos valores para que la cámara virtual se mueva por el vídeo.
- **Corrección de lente**: corrige la curvatura de las líneas rectas causada por lentes gran angular y cámaras de acción (valores negativos) o por teleobjetivos (positivos). **Zoom para ocultar los bordes** amplía la imagen para quitar las esquinas vacías.
- **Reducción de ruido de vídeo**: suaviza el grano de grabaciones con poca luz. **Conservar detalles** mantiene más textura.
- **Visualizador de audio**: barras (normales o en espejo) o una onda que bailan con el sonido de una canción. Al añadirlo, elija la canción de la biblioteca; debe estar en la línea de tiempo. Ajuste el número de barras, la altura, el color y la opacidad. Úselo en un clip de vídeo o imagen (por ejemplo, un fondo).

En las pestañas **Efectos** y **Color**, la opción **Comparar antes/después en el monitor** muestra, a la izquierda de una línea en el monitor, el vídeo sin efectos y sin corrección de color, para comparar. Arrastre el tirador de la línea a la derecha o a la izquierda para cambiar la división.

Las tarjetas de las opciones ya aplicadas al clip tienen el borde resaltado. Un doble clic en una tarjeta aplicada la retira: quita el efecto o, en un ajuste predefinido de color, los ajustes que creó (siempre que no los haya cambiado).

### Color

La pestaña **Color** empieza por los **Ajustes predefinidos**: Blanco y negro, Noir, Cine, Vívido, Cálido, Frío, Desvaído, Retro y Claro y luminoso. Haga clic en una miniatura para aplicar el estilo al clip. El ajuste entra en la lista de correcciones aplicadas como ajustes normales, que puede cambiar o quitar; **Deshacer** (Ctrl+Z) lo revierte todo de una vez.

Debajo, en **Herramientas de color** (y en la sección **Color** de la biblioteca), están las herramientas de corrección de color:

- **Ajustes de color**: exposición, brillo, contraste, saturación, temperatura, matiz (verde o magenta), tono, sombras y luces.
- **Balance de blancos**: indique el color que debería ser gris neutro y el programa corrige la dominante.
- **Curvas**: curvas de tonos del vídeo completo y por canal (rojo, verde y azul).
- **Ruedas de color**: ajuste separado de sombras, tonos medios y luces.
- **LUT 3D (.cube)**: aplica una tabla de colores en formato .cube, muy usada para dar estilo al vídeo. Elija la intensidad con el control deslizante.
- **HSL**: ajusta el tono, la saturación y la luminancia de cada gama de color (rojo, naranja, amarillo, verde, cian, azul, morado y magenta) sin tocar las demás, por ejemplo solo el azul del cielo. Los grises, el negro y el blanco no cambian.
- **HDR**: aspecto HDR, que recupera sombras y luces y refuerza los detalles. Tiene intensidad, sombras, luces, detalles e intensidad del color.
- **Igualar color**: iguala el color y el contraste de un clip con los de otro. Seleccione el clip y use **Igualar color con el clip anterior** en la pestaña **IA**: el programa compara tres fotogramas de cada clip y crea el efecto, con ganancia y desplazamiento por canal. Ajuste la **Intensidad** para un término medio.

### Máscaras, seguimiento y estabilización

Estas funciones están en la pestaña **Composición**. Sirven para vídeos, imágenes, textos y elementos gráficos.

- **Máscaras**: cree una forma con **+ Rectángulo**, **+ Elipse** o **+ Libre** (puntos, con Ctrl+clic en el monitor). La máscara recorta lo que se ve del clip. Puede sumar, restar o intersecar máscaras, invertir la máscara y suavizar el contorno.
- **Máscara de color (solo efectos)**: marcada, la máscara no recorta el clip; limita la corrección de color y los efectos a su área (por ejemplo, aclarar solo el rostro o desenfocar solo una matrícula). El borde suavizado de la máscara hace la transición.
- **Forma clave**: con el botón **◇ Forma clave**, la máscara cambia de forma a lo largo del tiempo.
- **Seguimiento de movimiento**: marque un objeto en el monitor, haga clic en **Seguir** y el programa sigue ese objeto. Otro clip puede entonces **Seguir** el movimiento, con la opción **Seguir**. Use **Soltar** para desactivarlo.
- **Estabilización**: **Analizar y estabilizar** mide el movimiento de la cámara y compensa los temblores. Ajuste la **Suavidad** y la **Ampliación** de la imagen. Use **Quitar estabilización** para deshacer.

### Elementos gráficos y formas

En la sección **Elementos gráficos** de la biblioteca, haga doble clic en una forma para insertarla en el cursor. Las formas disponibles son: Rectángulo, Rectángulo redondeado, Círculo, Línea, Flecha, Estrella, Hexágono, Bocadillo y Corazón. Puede ajustar el relleno y el color del contorno en la pestaña **Composición**.

También puede importar un gráfico vectorial con el botón **Importar SVG…**.

En **Pegatinas** aparecen las pegatinas de los paquetes instalados. El programa trae el paquete **Básico**, con 40 pegatinas de uso común (flechas, círculo de resalte, correcto e incorrecto, bocadillos, corazón, estrella, emojis, botones Suscríbete y Me gusta, sellos como Nuevo y Oferta, entre otras). No puede eliminarse, pero sí exportarse. Haga clic en una para insertarla en el cursor durante 3 segundos, al 40% del tamaño (ajústelo en la pestaña Vídeo), en una pista de imagen libre (o en una pista nueva llamada Pegatinas). La imagen entra también en la biblioteca, en la categoría Pegatinas.

### Textos y subtítulos

### Insertar textos

En la sección **Textos** de la biblioteca, haga doble clic en una plantilla para insertarla en el cursor:

- **Texto simple**: texto en negrita con sombra suave.
- **Título**: título grande en el centro, con entrada en zoom.
- **Rótulo (lower third)**: nombre y cargo en la esquina inferior izquierda, con caja y barra de resalte.
- **Créditos finales**: lista de créditos que sube por la pantalla durante el clip.
- **Subtítulo**: subtítulo en la pista de subtítulos, con caja oscura en la parte inferior del encuadre.

Seleccione el texto en la línea de tiempo para editarlo. La pestaña **Texto** de las propiedades tiene:

- **Contenido**: el texto. Pulse Intro para saltar de línea.
- **Fuente**: fuentes de Windows o fuentes añadidas al programa con **+ Fuente** (archivos .ttf u .otf).
- **Negrita**, **Cursiva** y **Subrayado**.
- **Colores** del texto, del contorno, de la caja de fondo y de la sombra. Use el formato #RRGGBB o #AARRGGBB para definir el color.
- **Alineación** y **Punto de anclaje**.
- **Animación de entrada** y **Animación de salida**: por ejemplo, fundido, deslizamiento o zoom.

La posición, la escala, la rotación y la opacidad del texto están en la pestaña **Vídeo**.

Para reutilizar un texto ya hecho (estilo, animaciones y duración), selecciónelo y use **Guardar texto seleccionado como título…** en la sección Textos (o en el menú contextual del clip). Aparece en **Títulos guardados**; elíjalo y haga clic en **Insertar título guardado** para colocarlo en el cursor. Los títulos de paquetes instalados aparecen en la misma lista.

### Subtítulos

Los subtítulos son textos con horario, organizados en la pista de subtítulos. Para trabajar con ellos:

- **Herramientas › Importar subtítulos (SRT/VTT)…**: trae un archivo de subtítulos ya hecho.
- **Herramientas › Exportar subtítulos (SRT/VTT)…**: guarda los subtítulos del proyecto como archivo SRT o VTT.
- **Herramientas › Editor de subtítulos**: abre una ventana para ver y editar todos los subtítulos. Puede añadir, quitar y editar el texto y los horarios, ir a un subtítulo concreto y desplazarlos todos a la vez, en segundos.
- **Generar subtítulos**: crea subtítulos automáticamente a partir de la voz. Vea la sección Inteligencia artificial.

Al exportar el vídeo, puede elegir entre guardar un archivo de subtítulos junto al vídeo o dibujar los subtítulos en el propio vídeo (subtítulos incrustados).

## Audio

### Mezclador de audio

Abra el mezclador en **Herramientas › Mezclador de audio** o con el botón de la barra de herramientas. Muestra una columna para cada pista de audio, con:

- **Volumen** (fader) y **panorámica** (izquierda y derecha). Haga doble clic en el fader o en la panorámica para volver al valor predeterminado.
- **M** (silencio): silencia la pista.
- **S** (solo): deja sonando solo las pistas en solo.
- Medidores de nivel, que se mueven durante la reproducción.

### Pestaña Audio del clip

Seleccione un clip de audio, o un vídeo con sonido, y use la pestaña **Audio**:

- **Volumen**: 100% es el volumen original. Con el botón ◇ crea un fotograma clave de volumen. Así puede bajar la música durante una frase, por ejemplo.
- **Entrada gradual** y **Salida gradual**: fundidos de 0 a 10 segundos, para que el sonido empiece y termine suavemente.
- **Normalizar sonoridad**: ajusta el volumen a un objetivo de sonoridad (estándar EBU R128), sin superar un pico seguro. Elija el **Objetivo de sonoridad** antes de aplicar.
- **Quitar silencios**: corta los tramos silenciosos del clip. Ajuste el **Umbral**, por debajo del cual el sonido se considera silencio, y la **Duración mínima** del silencio.
- **Ritmos**: con la música seleccionada, **Marcar ritmos** crea un marcador en cada golpe y **Cortar vídeos en los ritmos** divide los clips de vídeo e imagen que suenan con ella en cada golpe (para montajes al ritmo). Ajuste la **Sensibilidad**: más alta encuentra más golpes.
- **Atenuar bajo la voz**: con la música seleccionada, haga clic en **Atenuar bajo la voz** y el programa baja su volumen en los tramos donde hay voz en las otras pistas (narración, entrevista). Elija la **Reducción** (por defecto −12 dB) y la **Transición** (duración de la bajada y la subida). El resultado son fotogramas clave de volumen, que puede ajustar; **Quitar atenuación** borra esta animación. Con el modelo de detección de voz instalado, la detección es más precisa; sin él, el programa usa el nivel del sonido.
- **Sincronizar por el audio**: si grabó el mismo momento con dos cámaras o con una grabadora de sonido, seleccione los dos clips y haga clic en **Sincronizar**. El programa alinea los dos sonidos. El clip de la pista más alta queda quieto y el otro se mueve.
- **Mantener el tono con la velocidad**: cuando el clip se acelera o se ralentiza, la voz mantiene un tono natural.

### Efectos de audio

En la misma pestaña, en **Añadir efecto de audio**, están los efectos:

- **Ecualizador**: graves, medios y agudos, con corte de graves.
- **Compresor**: acerca los tramos fuertes a los débiles, con umbral, relación, ataque, liberación y ganancia de compensación.
- **Reducción de ruido**: reduce siseos y ruidos constantes, como un ventilador o el aire acondicionado. Tiene intensidad y sensibilidad.
- **Eco**: repeticiones del sonido con retardo.
- **Reverberación**: simula el sonido de una sala.
- **Teléfono / radio**: sonido de teléfono o radio antigua.
- **Distorsión**: saturación del sonido.
- **Cambiar la voz**: voces listas (**Grave**, **Aguda**, **Ardilla**, **Monstruo** y **Robot**) o, en **Personalizado**, el **Tono** en semitonos y la intensidad del **Robot**. La velocidad no cambia.

Los efectos se aplican en el orden en que aparecen en la lista **Efectos de audio aplicados**.

## Inteligencia artificial

Las funciones de inteligencia artificial del programa se ejecutan solo en su equipo. Ningún vídeo, audio, imagen o texto se envía a internet.

Las funciones están en la pestaña **IA** de las propiedades, cuando selecciona un clip en la línea de tiempo. La sección **IA** de la biblioteca resume las funciones y tiene el botón **Gestionar modelos de IA…**, que abre el gestor de modelos.

### Modelos necesarios

Cada función usa un modelo de IA. El modelo de **Quitar fondo** viene con el programa. Los demás deben instalarse, una sola vez, con la herramienta **Descargar modelos de IA**. Cuando el modelo de una función no está instalado, el botón queda desactivado y aparece el mensaje "No disponible — instale el modelo en Herramientas › Modelos de IA.".

| Función | Modelo | Requiere instalación | Se ejecuta en |
|---|---|---|---|
| Quitar fondo | MODNet | No, viene con el programa | CPU o GPU |
| Calcular máscaras en segundo plano | MODNet | No, viene con el programa | CPU o GPU |
| Seguir persona | MODNet | No, viene con el programa | CPU o GPU |
| Generar subtítulos | Whisper large-v3 turbo | Sí, unos 550 MB | Solo CPU |
| Aislar voz | GTCRN | Sí, unos 0,5 MB | Solo CPU |
| Reducir ruido con IA | GTCRN | Sí, unos 0,5 MB | Solo CPU |
| Quitar tramos sin voz | Silero VAD 5 | Sí, unos 2,3 MB | Solo CPU |
| Detectar escenas | TransNetV2 | Sí, unos 31 MB | CPU o GPU |
| Mejorar imagen | Swin2SR ligero 2× | Sí, unos 8 MB | CPU o GPU |
| Retocar (belleza y maquillaje) y difuminar rostros | Face Mesh V2 | Sí, unos 5 MB | Solo CPU |
| Asistente virtual (respuestas en lenguaje natural) | Phi-4-mini-instruct | Sí, unos 3,2 GB | CPU o GPU |

La detección de escenas funciona incluso sin el modelo TransNetV2, comparando las imágenes del vídeo. En ese caso, la precisión es menor.

El reconocimiento de voz (subtítulos) usa Silero VAD para encontrar los tramos con voz. Al instalar Whisper, Silero VAD se instala junto, automáticamente.

> Los modelos de la tabla ocupan espacio en disco y también memoria al usarse. Whisper, por ejemplo, necesita unos 1,5 GB de memoria libre.

### Quitar fondo

Separa a la persona del fondo y deja el fondo transparente, o lo sustituye por otra cosa. Úselo en la pestaña **IA**, con **Quitar fondo con IA**.

- **Nuevo fondo**: elija **Transparente**, **Color sólido**, **Desenfoque** o **Imagen o vídeo de la biblioteca**. En este último caso, elija el archivo en **Medio del fondo**.
- **Mantener**: deje a la persona (primer plano) o el fondo, para quitar a la persona.
- **Ajustes**: el umbral de la máscara, la contracción o expansión del borde, el suavizado del borde y el suavizado entre fotogramas están en la pestaña **Efectos**, en **Efectos aplicados**. El suavizado entre fotogramas reduce el temblor del borde de un fotograma a otro.

El modelo se entrenó principalmente para retratos. En escenas con objetos parecidos a personas, puede conservar partes del fondo.

Use **Calcular máscaras en segundo plano** para procesar antes el clip entero. Así la reproducción y la exportación son más rápidas. El resultado se guarda en la caché.

Use **Seguir persona con IA** para crear un seguimiento de la persona. Después, en la pestaña **Composición**, puede vincularle textos o elementos gráficos.

### Generar subtítulos

Crea subtítulos automáticamente a partir de la voz del clip.

1. Seleccione el clip y vaya a la pestaña **IA**.
2. En **Idioma de la voz**, elija Portugués, Inglés, Español o el idioma automático.
3. Haga clic en **Generar subtítulos con IA**.
4. Los subtítulos aparecen en la pista de subtítulos. Revíselos en el **Editor de subtítulos**.

El reconocimiento se ejecuta en el procesador y tarda unos 4 minutos para un vídeo de 10 minutos. Siga el progreso en la barra de progreso de la pestaña.

### Aislar voz y reducir ruido

- **Aislar voz con IA**: separa la voz del ambiente. El programa crea dos pistas nuevas, una con la voz y otra con el ambiente.
- **Reducir ruido con IA**: reduce el ruido de fondo, con la **Intensidad de la reducción de ruido** elegida. Útil para grabaciones con siseo o zumbido.

Estas operaciones se hacen con el audio del clip tal como suena en la línea de tiempo. La opción **Silenciar el audio original** evita oír el sonido antiguo junto con el nuevo.

### Quitar tramos sin voz

Encuentra los tramos sin voz y los quita del clip. Use **Pausa mínima** para definir la duración mínima de pausa que se cortará y luego haga clic en **Quitar tramos sin voz con IA**. Esta función necesita el modelo Silero VAD instalado; sin él, el botón no está disponible. Para cortar silencios sin IA, use la pestaña **Audio** (**Quitar silencios**).

### Detectar escenas

Busca los cambios de escena del vídeo y crea un marcador en cada uno.

1. Seleccione el clip y vaya a la pestaña **IA**.
2. Ajuste la **Sensibilidad** de las escenas. Cuanto mayor, más escenas se encuentran.
3. Marque **Dividir el clip en cada escena** si quiere que el clip se corte en cada escena.
4. Haga clic en **Detectar escenas**.

### Mejorar imagen

Amplía una foto 2× reconstruyendo los detalles. Seleccione la imagen, vaya a la pestaña **IA** y haga clic en **Mejorar imagen con IA**. El clip pasa a usar la nueva imagen. Para volver a la original, use **Deshacer**.

### Asistente virtual

Resuelve dudas sobre el programa y sobre edición de vídeo según este manual, en el idioma del programa y sin internet. Ábralo en **Ayuda › Asistente virtual** (Mayús+F1): el panel aparece a la derecha del editor. Escriba la pregunta y pulse Intro (Mayús+Intro añade una línea); sin conversación, el panel sugiere algunas preguntas.

- Con el modelo de lenguaje **Phi-4-mini** instalado, el asistente escribe una respuesta breve usando solo los fragmentos del manual que encontró y muestra la **Fuente en el manual**: haga clic en ella para abrir el manual en esa sección. El botón de detener interrumpe la respuesta.
- Sin el modelo, el asistente muestra las secciones del manual que tratan el tema, con un fragmento de cada una y el botón **Abrir en el manual**. El aviso en la parte superior del panel instala el modelo (unos 3,2 GB, descargados una sola vez de los orígenes oficiales) sin salir del editor; **Ahora no** oculta el aviso.
- El modelo usa la tarjeta gráfica cuando el **Dispositivo de IA** es **GPU (DirectML)** (en **Configuración › Rendimiento**) o, si no, el procesador. La primera pregunta tarda unos segundos más porque se carga el modelo; al cerrar el panel, sale de la memoria.
- **Nueva conversación** borra la conversación. Las respuestas pueden contener errores: compruébelo en la sección indicada.

### Cancelar un procesamiento

Mientras una función de IA se ejecuta, aparece una barra de progreso. Haga clic en **Cancelar procesamiento de IA** para interrumpirla.

## Exportar el vídeo

Exportar es generar el archivo final del vídeo. Haga clic en **Exportar** en la esquina superior derecha de la ventana, o use **Archivo › Exportar…** (atajo Ctrl+E).

### Plantillas de exportación

En la lista de la izquierda de la ventana de exportación, elija una plantilla. Cada plantilla ya trae la configuración adecuada para una plataforma:

- YouTube
- YouTube Shorts
- Instagram Feed
- Instagram Reels
- TikTok
- WhatsApp Status
- WhatsApp compartir
- Facebook Feed
- Facebook Reels
- Presentaciones
- Ordenadores y portátiles
- Exportación personalizada

Ajuste los parámetros y, si quiere, haga clic en **Guardar como nueva…** para guardar su configuración. Las plantillas propias aparecen con el sello "Mi plantilla" y pueden editarse, duplicarse o eliminarse. Las plantillas predeterminadas no pueden borrarse, pero puede usar **Restaurar predeterminada** para volver a la configuración original de una plantilla, o **Restaurar todas**.

### Parámetros

- **Formato del archivo**: MP4, MOV (QuickTime), MKV, WebM, AVI, MPEG-2 (.mpg) y GIF animado. No todos los formatos aceptan todos los códecs. Si la combinación no es posible, la ventana avisa.
- **Codificador**: **Automático (tarjeta gráfica si está disponible)**, **Tarjeta gráfica** o **Software (más compatible)**. El modo Software es el más seguro para archivos que deben abrirse en cualquier dispositivo.
- **Códec de vídeo**: H.264 (AVC), H.265 (HEVC), AV1, VP9, Apple ProRes, FFV1, QuickTime Animation, Motion JPEG, MPEG-2 o GIF. Para la mayoría de los casos, elija H.264.
- **Perfil**: Predeterminado, High, Main o Baseline del códec. Mantenga Predeterminado si no lo sabe.
- **Control de tasa**: **Tasa variable (VBR)**, **Tasa constante (CBR)** o **Calidad constante**. Con calidad constante, use valores entre 18 y 23; los números menores significan mejor calidad.
- **Tasa de bits de vídeo (kbps)**: cuanto mayor, más calidad y más tamaño de archivo. Para internet, use el valor sugerido por la plantilla.
- **Resolución**: marque **Usar la resolución del proyecto** o indique el ancho y el alto en píxeles.
- **Frecuencia de fotogramas**: **Del proyecto**, o una de las frecuencias estándar (23,976, 24, 25, 29,97, 30, 50, 59,94 y 60 FPS).
- **Códec de audio**, **Frecuencia de muestreo** (44,1 kHz, 48 kHz o 96 kHz), **Canales** (Mono, Estéreo o 5.1) y **Tasa de bits de audio (kbps)**.
- **Exportar transparencia (canal alfa)**: mantiene el fondo transparente. Úselo con ProRes 4444, QuickTime Animation, FFV1, VP9 o GIF.

### Intervalo y división del archivo

- **Intervalo**: elija **Secuencia completa** para exportar todo el proyecto, o **Intervalo personalizado** para exportar solo un tramo. Defina el **Inicio** y el **Fin**, o use **Inicio en el cursor**.
- **Dividir en archivos**: divide la exportación en partes de hasta cierta duración en segundos, o con un límite de tamaño en MB. Útil para plataformas con límite de tamaño.

### Subtítulos en la exportación

En **Subtítulos**, elija **Ninguno**, **SubRip (.srt)** o **WebVTT (.vtt)** para guardar un archivo de subtítulos junto al vídeo. Marque **Dibujar los subtítulos en el vídeo (subtítulos incrustados)** para que queden grabados en la imagen.

### Destino e inicio

1. Elija la **Carpeta de destino** y el **Nombre del archivo**.
2. Marque **Renderizar en segundo plano** si quiere seguir editando mientras se genera el vídeo.
3. Haga clic en **Exportar** para empezar.

### Cola de renderizado

Cada exportación entra en la cola de renderizado. Abra la cola con el botón **Cola de renderizado** o en **Archivo › Cola de renderizado…**. Para cada elemento, la cola muestra el estado, el progreso, la velocidad y el tiempo restante:

- **En cola**: esperando su turno.
- **Renderizando**: en curso.
- **Completada**: lista. Use **Abrir archivo exportado** o **Mostrar en la carpeta**.
- **Interrumpida**: puede continuarse con **Reanudar**. Al reanudar, el programa aprovecha los bloques ya generados.
- **Error**: el mensaje explica el motivo.
- **Cancelada**: interrumpida por el usuario.

Use **Cancelar exportación** para detener un elemento, **Quitar de la cola** para sacarlo de la lista y **Limpiar completadas** para vaciar los elementos terminados.

## Guardar, guardado automático y recuperación

### Guardar el proyecto

- **Archivo › Guardar** (Ctrl+S): guarda los cambios en el archivo del proyecto.
- **Archivo › Guardar como…** (Ctrl+Mayús+S): guarda una copia del proyecto con otro nombre o en otra carpeta.
- **Archivo › Crear copia de seguridad**: crea una copia de seguridad del proyecto en el momento actual. El programa guarda hasta 10 copias de cada proyecto, en la carpeta de copias de seguridad de la aplicación.

El archivo del proyecto tiene la extensión .med. El programa no modifica los archivos de medios originales.

### Guardado automático

De forma predeterminada, el programa guarda una copia de recuperación cada 5 minutos. Puede desactivar esta opción, o cambiar el intervalo, en **Herramientas › Configuración…**, pestaña **General**.

La copia de recuperación no sustituye al archivo del proyecto. Solo se ofrece cuando el programa se abre después de un cierre inesperado, como un corte de luz o un bloqueo.

### Recuperar un proyecto

Si el programa se cerró sin guardar, al abrirlo de nuevo aparece la pregunta "Recuperar proyecto":

- **Sí**: abre el proyecto con los últimos cambios.
- **No**: abre un proyecto vacío; la copia sigue guardada y se ofrece de nuevo la próxima vez.

> Se recomienda guardar manualmente con Ctrl+S a menudo, incluso con el guardado automático activado.

## Configuración y rendimiento

Abra la configuración en **Herramientas › Configuración…** (atajo Ctrl+,). La configuración se divide en cuatro pestañas. No se guarda nada hasta que haga clic en **Guardar**.

### Pestaña General

- **Usuario › Nombre completo**: su nombre.
- **Idioma del sistema › Idioma**: Alemán, Español, Francés, Inglés, Portugués (Brasil) o Portugués (Portugal). En la primera ejecución, el programa usa el idioma de Windows si es uno de estos; si no, el inglés. Al guardar, el programa se reinicia en el nuevo idioma: antes pregunta por los cambios sin guardar y vuelve a abrir un proyecto guardado. El manual también se abre en el idioma elegido.
- **Tema**: **Oscuro**, **Claro** o **Igual que Windows**. También puede cambiarlo con el botón **Tema** de la barra superior.
- **Buscar actualizaciones › Comprobar mensualmente**: **Sí** (predeterminado) o **No**. La fecha de la última comprobación aparece al lado. Con **Sí**, al abrirse el programa comprueba por sí solo si hay una versión nueva cuando han pasado más de 30 días desde la última comprobación. La consulta solo envía el idioma de la interfaz; sin internet, no aparece nada.
- **Abrir el último proyecto al iniciar**.
- **Carpetas predeterminadas**: la carpeta de los proyectos y la de las exportaciones.
- **Guardado automático**: activar o desactivar la copia de recuperación y definir el intervalo.

El nombre, el idioma, el tema y la preferencia de actualizaciones se guardan en el Registro de Windows, en `HKEY_CURRENT_USER\Software\Meu Editor de Vídeo`.

### Pestaña Rendimiento

- **Hardware detectado**: muestra el procesador, la memoria, las tarjetas gráficas, los discos y si la tarjeta gráfica puede decodificar y codificar vídeo. El programa también indica las limitaciones del equipo y una recomendación de uso.
- **Vista previa**: la **Calidad predeterminada de la vista previa** puede ser **Automática**, **Completa**, **1/2 de la resolución** o **1/4 de la resolución**. En Automática, el programa elige según el equipo y el proyecto, y reduce la resolución durante la reproducción si el equipo no da abasto. La **Memoria para fotogramas de la vista previa** define cuánto espacio usar para guardar fotogramas listos.
- **Proxies**: **Usar proxies en la vista previa cuando existan** y **Generar proxies automáticamente para vídeos de más de 1080p**.
- **Aceleración**: **Usar la tarjeta gráfica para decodificar y codificar vídeo** y **Renderizar la cola de exportación en segundo plano**.
- **Inteligencia artificial**: el **Dispositivo de IA** puede ser **GPU (DirectML), con retorno a la CPU**, o **Solo CPU**. Con la opción de GPU, si la tarjeta o el controlador fallan, el modelo se carga en el procesador automáticamente. El botón **Gestionar modelos…** abre la ventana de modelos de IA.

### Pestaña Caché

La caché guarda miniaturas, formas de onda, proxies y máscaras de IA, para que no haya que recalcularlos. En esta pestaña ve cuánto espacio usa cada área, define el **Límite de la caché** en GB (4 GB por defecto) y puede usar **Aplicar el límite ahora** o **Vaciar caché**.

Cuando se supera el límite, se borran los elementos usados hace más tiempo. Los proyectos y los medios originales nunca se borran al vaciar la caché.

### Pestaña Atajos

Esta pestaña muestra todas las acciones del programa, agrupadas por categoría. Para cambiar un atajo:

1. Haga clic en el campo del atajo.
2. Pulse la nueva combinación de teclas.
3. Haga clic en **Guardar**.

Si la combinación ya la usa otra acción, el programa avisa y no deja guardar. Use **Predeterminado** en una acción para volver al atajo original, o **Restaurar todos los valores predeterminados** para deshacer todos los cambios. Los menús siempre muestran el atajo en uso.

## Atajos de teclado

Estos son los atajos predeterminados. Puede cambiar cualquiera en **Herramientas › Configuración… › Atajos**.

| Acción | Atajo |
|---|---|
| Nuevo proyecto | Ctrl+N |
| Abrir proyecto | Ctrl+O |
| Guardar | Ctrl+S |
| Guardar como | Ctrl+Mayús+S |
| Importar medios | Ctrl+I |
| Exportar | Ctrl+E |
| Deshacer | Ctrl+Z |
| Rehacer | Ctrl+Y o Ctrl+Mayús+Z |
| Configuración | Ctrl+, |
| Manual del usuario | F1 |
| Asistente virtual | Mayús+F1 |
| Reproducir / pausar | Espacio |
| Pantalla completa | F11 (Esc o F11 para salir) |
| Fotograma anterior | ← |
| Fotograma siguiente | → |
| Retroceder 1 segundo | Mayús+← |
| Avanzar 1 segundo | Mayús+→ |
| Ir al inicio | Inicio |
| Ir al final | Fin |
| Corte anterior | ↑ |
| Corte siguiente | ↓ |
| Dividir clip en el cursor | S o Ctrl+K |
| Eliminar | Supr o Retroceso |
| Eliminar y cerrar el hueco | Mayús+Supr |
| Copiar | Ctrl+C |
| Cortar | Ctrl+X |
| Pegar | Ctrl+V |
| Duplicar | Ctrl+D |
| Seleccionar todo | Ctrl+A |
| Agrupar | Ctrl+G |
| Desagrupar | Ctrl+Mayús+G |
| Añadir marcador | M |
| Activar/desactivar imán | N |
| Acercar la línea de tiempo | Ctrl++ (tecla más) |
| Alejar la línea de tiempo | Ctrl+- (tecla menos) |
| Ajustar a la ventana | Mayús+Z |
| Multicámara: cámara 1 a 4 | Alt+1 a Alt+4 |

## Privacidad

Meu Editor de Vídeo está hecho para funcionar en su equipo:

- Sus vídeos, fotos, audios, textos y proyectos se procesan solo en su equipo.
- Las funciones de inteligencia artificial también se ejecutan en su equipo. Ningún contenido se envía a internet.
- El programa no recopila datos de uso ni envía telemetría.
- El programa no se actualiza solo por internet.

Hay una única excepción. Cuando hace clic en **Instalar** en la herramienta **Descargar modelos de IA**, el programa descarga los archivos de los modelos de IA de los sitios oficiales de sus autores, que son huggingface.co y github.com. Solo se descargan los modelos elegidos y no se envía ningún dato de su proyecto. Una vez instalados, los modelos funcionan sin internet. También puede instalar paquetes de modelos ya descargados, con **Instalar paquete…**, sin usar internet.

Para ver las licencias y los componentes de terceros usados, abra **Ayuda › Acerca de Meu Editor de Vídeo…**. El texto completo de las licencias está en **Ayuda › Licencias de terceros**.

## Solución de problemas

### La función de IA está "No disponible"

El modelo de la función aún no se ha instalado. Vaya a **Herramientas › Modelos de IA…**, haga clic en **Descargar modelos…**, elija los modelos y haga clic en **Instalar**. La herramienta instala primero las dependencias, si las hay. Para la función Quitar fondo no hace falta instalar nada.

### Se interrumpió la instalación de un modelo

Abra **Descargar modelos…** de nuevo y haga clic en **Instalar**. La descarga continúa donde se quedó. Si el archivo descargado está incompleto o alterado, se descarta y se descarga otra vez.

### La vista previa se traba o va lenta

- Arriba del monitor, en **Resolución de la vista previa**, elija **1/2** o **1/4**.
- Active el uso de proxies, creando proxies de los vídeos grandes con **Generar proxy de edición (540p)**.
- Cierre otros programas pesados mientras edita.
- En **Configuración › Rendimiento**, compruebe que la aceleración por tarjeta gráfica esté activada.

La exportación siempre usa la resolución original, por eso un vídeo puede trabarse en la vista previa y aun así exportarse correctamente.

### Un medio aparece como ausente

El archivo se movió, cambió de nombre o se eliminó. Use **Proyecto › Localizar medios ausentes…** para indicar la nueva ruta. Para evitarlo, use **Empaquetar medios en el proyecto** antes de mover el proyecto.

### "Formato de archivo no admitido"

El programa no lee ese formato. Convierta el archivo a un formato común, como MP4 (vídeo) o MP3 y WAV (audio), con otro programa de conversión, e impórtelo de nuevo.

### La exportación falló

- Lea el mensaje en la cola de renderizado. Indica el motivo.
- Si aparece el mensaje de códec incompatible con el formato, elija otro códec u otro formato.
- Si el error tiene que ver con la tarjeta gráfica, elija el codificador **Software (más compatible)** y exporte de nuevo.
- Si el archivo no se abre en otros dispositivos, use la plantilla **Ordenadores y portátiles** o el formato MP4 con H.264.

### Perdí cambios tras un bloqueo

Al abrir el programa, responda **Sí** a la pregunta "Recuperar proyecto". Si no aparece, busque la copia de seguridad del proyecto, que está en la carpeta de copias de seguridad de la aplicación, dentro de `%LOCALAPPDATA%\MeuEditorDeVideo\backups`.

### El programa no se abre o muestra errores

Los registros de funcionamiento (logs) están en la carpeta `%LOCALAPPDATA%\MeuEditorDeVideo\logs`. Para abrirla, use **Ayuda › Abrir carpeta de registros** (o pulse Windows + R, pegue esa ruta y pulse Intro). Al pedir ayuda, envíe el archivo de registro más reciente junto con el texto de **Ayuda › Información del sistema…**. Los registros no contienen el contenido de sus vídeos.

### Dudas sobre la versión instalada

Abra **Ayuda › Acerca de Meu Editor de Vídeo…** para ver la versión del programa, la licencia, la información de privacidad y la lista de componentes y modelos de IA usados.
