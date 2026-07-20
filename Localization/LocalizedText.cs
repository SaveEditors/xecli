using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XeCli.Localization;

internal static partial class LocalizedText
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-ES");

    private static readonly (Regex Pattern, string Replacement)[] RegexReplacements =
    {
        (new Regex(@"^Default console set to\s+", RegexOptions.CultureInvariant), "Consola predeterminada establecida en "),
        (new Regex(@"^Deleted directory\s+", RegexOptions.CultureInvariant), "Directorio eliminado "),
        (new Regex(@"^Deleted file\s+", RegexOptions.CultureInvariant), "Archivo eliminado "),
        (new Regex(@"^Created directory\s+", RegexOptions.CultureInvariant), "Directorio creado "),
        (new Regex(@"^Moved directory\s+", RegexOptions.CultureInvariant), "Directorio movido "),
        (new Regex(@"^Moved file\s+", RegexOptions.CultureInvariant), "Archivo movido "),
        (new Regex(@"^FTP target:\s+", RegexOptions.CultureInvariant), "Destino FTP: "),
        (new Regex(@"^Installer exited with code\s+", RegexOptions.CultureInvariant), "El instalador salió con el código "),
        (new Regex(@"^Attempting FTP reconnection\s+", RegexOptions.CultureInvariant), "Intentando reconexión FTP "),
        (new Regex(@"^Choose an install scope", RegexOptions.CultureInvariant), "Elige un alcance de instalación"),
        (new Regex(@"^Install directory:", RegexOptions.CultureInvariant), "Directorio de instalación:"),
        (new Regex(@"^Continue with installation\?", RegexOptions.CultureInvariant), "¿Continuar con la instalación?"),
        (new Regex(@"^Would you like to reinstall or update it\?", RegexOptions.CultureInvariant), "¿Quieres reinstalarlo o actualizarlo?"),
        (new Regex(@"^Choose a console number or press Enter to skip:", RegexOptions.CultureInvariant), "Elige un número de consola o pulsa Enter para omitir:"),
        (new Regex(@"^Launch the XeCLI installer now\?", RegexOptions.CultureInvariant), "¿Iniciar ahora el instalador de XeCLI?"),
        (new Regex(@"^Select a console$", RegexOptions.CultureInvariant), "Selecciona una consola"),
        (new Regex(@"^IP:$", RegexOptions.CultureInvariant), "IP:"),
        (new Regex(@"^Current user:", RegexOptions.CultureInvariant), "Usuario actual:"),
        (new Regex(@"^Hosted avatar library$", RegexOptions.CultureInvariant), "Biblioteca de avatar alojada"),
        (new Regex(@"^Local avatar collection$", RegexOptions.CultureInvariant), "Colección local de avatares"),
        (new Regex(@"(\d+)\s+consoles detected\.", RegexOptions.CultureInvariant), "$1 consolas detectadas."),
        (new Regex(@"^1 console detected\.", RegexOptions.CultureInvariant), "1 consola detectada.")
    };

    private static readonly (string Source, string Target)[] PhraseReplacements =
    {
        ("All users", "Todos los usuarios"),
        ("Current user", "Usuario actual"),
        ("Recommended", "Recomendado"),
        ("Administrator approval required", "Se requiere aprobación de administrador"),
        ("Install directory", "Directorio de instalación"),
        ("Command Access", "Acceso al comando"),
        ("Next Step", "Siguiente paso"),
        ("Machine PATH", "PATH del sistema"),
        ("User PATH", "PATH del usuario"),
        ("Install complete", "Instalación completada"),
        ("Uninstall complete", "Desinstalación completada"),
        ("Not connected", "No conectado"),
        ("PATH install complete", "Instalación en PATH completada"),
        ("PATH uninstall complete", "Desinstalación de PATH completada"),
        ("First-Run Setup", "Configuración inicial"),
        ("Searching for consoles on the local network...", "Buscando consolas en la red local..."),
        ("No consoles discovered. Enter IP manually:", "No se detectaron consolas. Introduce la IP manualmente:"),
        ("Yes", "Sí"),
        ("Not now", "Ahora no"),
        ("Never ask again", "No volver a preguntar"),
        ("Select one title on the left, then check the items to install.", "Selecciona un título a la izquierda y luego marca los elementos que quieras instalar."),
        ("Choose language / Elige idioma", "Choose language / Elige idioma"),
        ("English", "English"),
        ("Español", "Español"),
        ("NOTICE", "AVISO"),
        ("SUCCESS", "ÉXITO"),
        ("FAILED", "FALLO"),
        ("USAGE", "USO"),
        ("DESCRIPTION", "DESCRIPCIÓN"),
        ("COMMANDS", "COMANDOS"),
        ("OPTIONS", "OPCIONES"),
        ("NOTES", "NOTAS"),
        ("SUPPORTED", "COMPATIBLE"),
        ("ADDITIONAL COMMANDS", "COMANDOS ADICIONALES"),
        ("EXTRA COMMANDS", "COMANDOS EXTRA")
    };

    private static readonly Dictionary<string, string> ManualOverrides = new(StringComparer.Ordinal)
    {
        ["Choose language / Elige idioma"] = "Choose language / Elige idioma",
        ["Avatar Browser"] = "Explorador de avatares",
        ["XeCLI Avatar Browser"] = "Explorador de avatares de XeCLI",
        ["Close"] = "Cerrar",
        ["Clear"] = "Limpiar",
        ["Content ID"] = "ID de contenido",
        ["COMMANDOS:"] = "COMANDOS:",
        ["COMMANDS:"] = "COMANDOS:",
        ["DESCRIPCION"] = "DESCRIPCIÓN",
        ["DESCRIPCION:"] = "DESCRIPCIÓN:",
        ["DESCRIPTION:"] = "DESCRIPCIÓN:",
        ["Description:"] = "Descripción:",
        ["DESCRIPCION:"] = "DESCRIPCIÓN:",
        ["Game"] = "Juego",
        ["Game / title filter"] = "Filtro de juego / titulo",
        ["Install Selected"] = "Instalar seleccionados",
        ["Item"] = "Elemento",
        ["Item search"] = "Busqueda de elementos",
        ["Items"] = "Elementos",
        ["Layout"] = "Diseno",
        ["Lista de perfiles y usuarios registrados"] = "Lista perfiles y usuarios conectados",
        ["List profiles and signed-in users."] = "Lista perfiles y usuarios conectados.",
        ["Resolve the active title or look up a Title ID."] = "Resuelve el título activo o busca un ID de título.",
        ["Resuelva el título activo o busque una ID de título"] = "Resuelve el título activo o busca un ID de título",
        ["Select All"] = "Seleccionar todo",
        ["Select a title to view its avatar items."] = "Selecciona un titulo para ver sus elementos de avatar.",
        ["Select at least one avatar item first."] = "Selecciona al menos un elemento de avatar primero.",
        ["Show or set the default target."] = "Muestra o establece el objetivo predeterminado.",
        ["Size"] = "Tamano",
        ["Tag"] = "Etiqueta",
        ["Title ID"] = "ID del titulo",
        ["Titles"] = "Titulos",
        ["Avatar items"] = "Elementos de avatar",
        ["Haga ping a la consola actual"] = "Envía un ping a la consola actual",
        ["Ping the current console."] = "Hace ping a la consola actual.",
        ["Publisher"] = "Editor",
        ["Reinicie la consola (en frío por defecto)"] = "Reinicia la consola (en frío de forma predeterminada)",
        ["Reboot the console (cold by default)."] = "Reinicia la consola (en frío de forma predeterminada).",
        ["Apague la consola"] = "Apaga la consola",
        ["Power off the console."] = "Apaga la consola.",
        ["Inicie un XEX con argumentos opcionales"] = "Inicia un XEX con argumentos opcionales",
        ["Launch a XEX with optional arguments."] = "Inicia un XEX con argumentos opcionales.",
        ["Launch the XeCLI installer"] = "Inicia el instalador de XeCLI",
        ["Launch the XeCLI installer."] = "Inicia el instalador de XeCLI.",
        ["Launch the XeCLI installer now?"] = "¿Iniciar ahora el instalador de XeCLI?",
        ["Descubra consolas y establezca el objetivo predeterminado"] = "Detecta consolas y establece el objetivo predeterminado",
        ["Discover consoles and set the default target."] = "Detecta consolas y establece el objetivo predeterminado.",
        ["Establezca o seleccione el objetivo predeterminado"] = "Establece o selecciona el objetivo predeterminado",
        ["Set or select the default target."] = "Establece o selecciona el objetivo predeterminado.",
        ["Escanee la red en busca de consolas"] = "Escanea la red en busca de consolas",
        ["Scan the network for consoles."] = "Escanea la red en busca de consolas.",
        ["Capture una captura de pantalla en vivo"] = "Captura una imagen en vivo",
        ["Capture a live screenshot."] = "Captura una imagen en vivo.",
        ["Explore los íconos XNotify y administre alias preestablecidos"] = "Explora los iconos de XNotify y gestiona alias predefinidos",
        ["Download public homebrew packages to USB, a folder, or a detected console drive."] = "Descarga paquetes homebrew públicos a una unidad USB, una carpeta o una unidad de consola detectada.",
        ["Browse XNotify icons and manage preset aliases."] = "Explora los iconos de XNotify y gestiona alias predefinidos.",
        ["Ayudantes del controlador de gestión del sistema"] = "Herramientas del System Management Controller",
        ["System Management Controller helpers."] = "Herramientas del System Management Controller.",
        ["Ayudantes de velocidad del ventilador"] = "Herramientas de velocidad del ventilador",
        ["Fan speed helpers."] = "Herramientas de velocidad del ventilador.",
        ["Ayudantes LED de anillo de luz"] = "Herramientas de los LED del anillo de luz",
        ["Ring-of-light LED helpers."] = "Herramientas de los LED del anillo de luz.",
        ["Ayudantes de usuario registrados"] = "Herramientas para usuarios conectados",
        ["Signed-in user helpers."] = "Herramientas para usuarios conectados.",
        ["Ayudantes de bandeja de discos"] = "Herramientas de la bandeja del disco",
        ["Disc tray helpers."] = "Herramientas de la bandeja del disco.",
        ["Please enter Y or N."] = "Introduce S o N.",
        ["Launch XeLL Reloaded, or confirm that XeLL is already running."] = "Inicia XeLL Reloaded o confirma que XeLL ya está en ejecución.",
        ["Inspect the available XeLL HTTP services, endpoints, and detected CPU key."] = "Inspecciona los servicios HTTP de XeLL disponibles, los endpoints y la CPU key detectada.",
        ["XeLL keyvault export helpers."] = "Herramientas para exportar el keyvault de XeLL.",
        ["Export the keyvault, CPU key text, and a packaged verified backup set."] = "Exporta el keyvault, el texto de la CPU key y un paquete de copias verificadas.",
        ["Boot XeLL Reloaded, download the flash dump, verify repeated dumps, and package a safe backup."] = "Inicia XeLL Reloaded, descarga el volcado flash, verifica volcados repetidos y empaqueta una copia de seguridad segura.",
        ["Ayudantes emergentes de consola estilo entrenador"] = "Herramientas de ventanas emergentes estilo trainer",
        ["Trainer-style console popup helpers."] = "Herramientas de ventanas emergentes estilo trainer.",
        ["FTP commands (alternate access)."] = "Comandos FTP (acceso alternativo).",
        ["Show or set FTP target."] = "Muestra o establece el objetivo FTP.",
        ["List files via FTP."] = "Lista archivos por FTP.",
        ["Find files via FTP."] = "Busca archivos por FTP.",
        ["Download a file via FTP."] = "Descarga un archivo por FTP.",
        ["Upload a file via FTP."] = "Sube un archivo por FTP.",
        ["Print a file via FTP."] = "Imprime un archivo por FTP.",
        ["Delete a file via FTP."] = "Elimina un archivo por FTP.",
        ["Create a directory via FTP."] = "Crea un directorio por FTP.",
        ["Move or rename a file via FTP."] = "Mueve o cambia el nombre de un archivo por FTP.",
        ["Ayudantes de perfil y datos guardados sobre FTP"] = "Herramientas de perfiles y partidas guardadas por FTP",
        ["Profile and save-data helpers over FTP."] = "Herramientas de perfiles y partidas guardadas por FTP.",
        ["Installed content management over FTP."] = "Gestión de contenido instalado por FTP.",
        ["Biblioteca de elementos de avatar e instalación de ayudantes"] = "Biblioteca de elementos de avatar y herramientas de instalación",
        ["Avatar item library and install helpers."] = "Biblioteca de elementos de avatar y herramientas de instalación.",
        ["DashLaunch gestión de complementos"] = "Gestión de plugins de DashLaunch",
        ["DashLaunch plugin management."] = "Gestión de plugins de DashLaunch.",
        ["ISO to Games on Demand conversion."] = "Conversión de ISO a Games on Demand.",
        ["Browse avatar items in a Windows picker and install selected entries."] = "Explora elementos de avatar en un selector de Windows e instala las entradas seleccionadas.",
        ["Use the hosted avatar library instead of the local corpus."] = "Usa la biblioteca de avatares alojada en lugar del corpus local.",
        ["Avatar item library root. Defaults to config or Avatar-Item-Collection."] = "Raíz de la biblioteca de elementos de avatar. Usa la configuración o Avatar-Item-Collection de forma predeterminada.",
        ["Directory used to cache downloaded avatar packages."] = "Directorio usado para almacenar en caché los paquetes de avatar descargados.",
        ["Alias of `rgh modules` and `rgh xbdm modules`."] = "Alias de `rgh modules` y `rgh xbdm modules`.",
        ["Shortcut for `rgh xbdm modules`."] = "Acceso directo para `rgh xbdm modules`.",
        ["Shortcut for `rgh xbdm mem`."] = "Acceso directo para `rgh xbdm mem`.",
        ["Shortcut for `rgh xbdm xex`."] = "Acceso directo para `rgh xbdm xex`.",
        ["Shortcut for `rgh xbdm fs`."] = "Acceso directo para `rgh xbdm fs`.",
        ["Shortcut for `rgh xbdm threads`."] = "Acceso directo para `rgh xbdm threads`.",
        ["Shortcut for `rgh xbdm debug`."] = "Acceso directo para `rgh xbdm debug`.",
        ["Install all items for one title when paired with --all."] = "Instala todos los elementos de un título cuando se usa con --all.",
        ["Install one specific avatar item."] = "Instala un elemento de avatar específico.",
        ["Install all items for the selected title."] = "Instala todos los elementos del título seleccionado.",
        ["Label shown in local output when --xuid is provided."] = "Etiqueta mostrada en la salida local cuando se usa --xuid.",
        ["Initial item search text."] = "Texto inicial de búsqueda de elementos.",
        ["Initial game/title filter text."] = "Texto inicial del filtro de juego/título.",
        ["Initial publisher filter."] = "Filtro inicial del editor.",
        ["Initial derived tag filter."] = "Filtro inicial de la etiqueta derivada.",
        ["Maximum titles/items to preload (default: 100, 0 = no limit)."] = "Número máximo de títulos y elementos que se precargarán (predeterminado: 100, 0 = sin límite).",
        ["XeLL Reloaded helpers with guided auto-launch when needed."] = "Ayudas de XeLL Reloaded con inicio automático guiado cuando sea necesario.",
        ["XeLL-backed NAND dumping and verification."] = "Volcado y verificación de NAND con respaldo de XeLL.",
        ["Ghidra headless helpers."] = "Ayudantes sin interfaz de Ghidra.",
        ["IDA headless helpers."] = "Ayudantes sin interfaz de IDA.",
        ["Configure Ghidra paths."] = "Configura las rutas de Ghidra.",
        ["Configure las rutas Ghidra."] = "Configura las rutas de Ghidra.",
        ["Run headless analysis."] = "Ejecuta análisis sin interfaz.",
        ["Ejecute análisis sin cabeza."] = "Ejecuta análisis sin interfaz.",
        ["Decompile a module or XEX."] = "Descompila un módulo o un XEX.",
        ["Descompilar un módulo o XEX."] = "Descompila un módulo o un XEX.",
        ["Verify decompiler output for bad-instruction placeholders."] = "Verifica la salida del descompilador en busca de marcadores de instrucciones no válidas.",
        ["Verifique la salida del descompilador en busca de marcadores de posición de instrucciones incorrectas."] = "Verifica la salida del descompilador en busca de marcadores de instrucciones no válidas.",
        ["Configure IDA paths and backend defaults."] = "Configura las rutas de IDA y los valores predeterminados del backend.",
        ["Configure las rutas IDA y los valores predeterminados del backend."] = "Configura las rutas de IDA y los valores predeterminados del backend.",
        ["Inspect IDA, idaxex, and idalib readiness."] = "Inspecciona el estado de IDA, idaxex e idalib.",
        ["Inspeccione la preparación de IDA, idaxex y idalib."] = "Inspecciona el estado de IDA, idaxex e idalib.",
        ["Run headless analysis and save an IDA database."] = "Ejecuta análisis sin interfaz y guarda una base de datos de IDA.",
        ["Ejecute un análisis sin cabeza y guarde una base de datos IDA."] = "Ejecuta análisis sin interfaz y guarda una base de datos de IDA.",
        ["Decompile a module or XEX via IDA."] = "Descompila un módulo o un XEX mediante IDA.",
        ["Descompile un módulo o XEX mediante IDA."] = "Descompila un módulo o un XEX mediante IDA.",
        ["Verify IDA decompiler output for common failure placeholders."] = "Verifica la salida del descompilador de IDA en busca de marcadores de fallo comunes.",
        ["Verifique la salida del descompilador IDA para detectar marcadores de posición de fallas comunes."] = "Verifica la salida del descompilador de IDA en busca de marcadores de fallo comunes.",
        ["Decompile a XEX to C via Ghidra."] = "Descompila un XEX a C mediante Ghidra.",
        ["Descompile un XEX en C mediante Ghidra."] = "Descompila un XEX a C mediante Ghidra.",
        ["Decompile a XEX to C via IDA."] = "Descompila un XEX a C mediante IDA.",
        ["Descompile un XEX en C mediante IDA."] = "Descompila un XEX a C mediante IDA.",
        ["Inicie XeLL Reloaded o confirme que XeLL ya se esté ejecutando"] = "Inicia XeLL Reloaded o confirma que XeLL ya está en ejecución",
        ["Inspeccione los servicios HTTP XeLL disponibles, los puntos finales y la clave de CPU detectada"] = "Inspecciona los servicios HTTP de XeLL disponibles, los endpoints y la CPU key detectada",
        ["XeLL keyvault export helpers"] = "Herramientas para exportar el keyvault de XeLL",
        ["Inicie XeLL Reloaded, descargue el volcado flash, verifique los volcados repetidos y empaquete una copia de seguridad segura"] = "Inicia XeLL Reloaded, descarga el volcado flash, verifica volcados repetidos y empaqueta una copia de seguridad segura",
        ["Mostrar o establecer el objetivo FTP"] = "Muestra o establece el objetivo FTP",
        ["Listar archivos a través de FTP"] = "Lista archivos por FTP",
        ["Busque archivos a través de FTP"] = "Busca archivos por FTP",
        ["Descargue un archivo a través de FTP"] = "Descarga un archivo por FTP",
        ["Cargue un archivo a través de FTP"] = "Sube un archivo por FTP",
        ["Imprima un archivo a través de FTP"] = "Imprime un archivo por FTP",
        ["Eliminar un archivo a través de FTP"] = "Elimina un archivo por FTP",
        ["Cree un directorio a través de FTP"] = "Crea un directorio por FTP",
        ["Mueva o cambie el nombre de un archivo mediante FTP"] = "Mueve o cambia el nombre de un archivo por FTP",
        ["Show a compact console status snapshot."] = "Muestra un resumen compacto del estado de la consola.",
        ["Fatman is XeCLI's FATX image and storage manager"] = "Fatman es el gestor de imágenes y almacenamiento FATX de XeCLI",
        ["Fatman is XeCLI's FATX image and storage manager."] = "Fatman es el gestor de imágenes y almacenamiento FATX de XeCLI.",
        ["List host storage devices visible to Fatman."] = "Lista los dispositivos de almacenamiento del host visibles para Fatman.",
        ["List Windows physical disks that Fatman can open directly."] = "Lista los discos físicos de Windows que Fatman puede abrir directamente.",
        ["Detect partitions inside a raw Xbox 360 HDD image or physical disk."] = "Detecta particiones dentro de una imagen de disco duro Xbox 360 sin procesar o de un disco físico.",
        ["Scan an image or physical disk for plausible FATX/XTAF volume headers."] = "Escanea una imagen o un disco físico en busca de encabezados de volumen FATX/XTAF plausibles.",
        ["Inspect one detected FATX partition."] = "Inspecciona una partición FATX detectada.",
        ["List entries inside a FATX partition."] = "Lista las entradas dentro de una partición FATX.",
        ["Search a FATX partition for matching paths."] = "Busca rutas coincidentes dentro de una partición FATX.",
        ["Extract a single file from a FATX partition."] = "Extrae un solo archivo de una partición FATX.",
        ["Print a text file from a FATX partition."] = "Imprime un archivo de texto desde una partición FATX.",
        ["Create a directory inside a FATX image."] = "Crea un directorio dentro de una imagen FATX.",
        ["Write a host file into a FATX image."] = "Escribe un archivo del host dentro de una imagen FATX.",
        ["Move or rename a FATX entry."] = "Mueve o cambia el nombre de una entrada FATX.",
        ["Remove a FATX entry from the image."] = "Elimina una entrada FATX de la imagen.",
        ["Format one or more FATX partitions inside an image or physical disk."] = "Formatea una o más particiones FATX dentro de una imagen o de un disco físico.",
        ["Scan a FATX partition for orphaned and invalid chain-map state."] = "Analiza una partición FATX en busca de asignaciones huérfanas y estados inválidos del mapa de cadenas.",
        ["Repair safe FATX chain-map issues such as orphaned allocations."] = "Repara problemas seguros del mapa de cadenas FATX, como asignaciones huérfanas.",
        ["Extract a directory tree from a FATX partition."] = "Extrae un árbol de directorios desde una partición FATX.",
        ["Dump one or more raw FATX partitions to host files."] = "Vuelca una o más particiones FATX sin procesar a archivos del host.",
        ["Back up or restore low-level FATX and partition metadata regions."] = "Haz una copia de seguridad o restaura regiones de metadatos FATX y de partición de bajo nivel.",
        ["Inspect and repair local Xbox 360 content packages"] = "Inspecciona y repara paquetes de contenido local de Xbox 360",
        ["Inspect and repair local Xbox 360 content packages."] = "Inspecciona y repara paquetes de contenido local de Xbox 360.",
        ["Verify a CON signature when supported."] = "Verifica una firma CON cuando sea compatible.",
        ["Save package headers and refresh STFS hashes."] = "Guarda las cabeceras del paquete y actualiza los hashes STFS.",
        ["Re-sign a CON package and verify the result."] = "Vuelve a firmar un paquete CON y verifica el resultado.",
        ["Print the package's FATX magic filename."] = "Imprime el nombre de archivo mágico FATX del paquete.",
        ["Print the destination FATX content path."] = "Imprime la ruta de destino del contenido FATX.",
        ["Inspect and edit local Xbox 360 profile packages"] = "Inspecciona y edita paquetes de perfil local de Xbox 360",
        ["Inspect and edit local Xbox 360 profile packages."] = "Inspecciona y edita paquetes de perfil local de Xbox 360.",
        ["Show profile package contents and summary information."] = "Muestra el contenido del paquete de perfil y la información de resumen.",
        ["Extract files from a profile package."] = "Extrae archivos de un paquete de perfil.",
        ["Inspect and update account data inside a profile package."] = "Inspecciona y actualiza los datos de la cuenta dentro de un paquete de perfil.",
        ["Inspect and update profile account data inside a profile package."] = "Inspecciona y actualiza los datos de la cuenta dentro de un paquete de perfil.",
        ["Show decoded profile account information."] = "Muestra la información decodificada de la cuenta del perfil.",
        ["Extract the raw Account payload from a profile package."] = "Extrae la carga útil Account sin procesar de un paquete de perfil.",
        ["Update the profile account gamertag."] = "Actualiza el gamertag de la cuenta del perfil.",
        ["List and extract embedded dashboard and title GPD files."] = "Lista y extrae los archivos GPD incrustados del panel y de los títulos.",
        ["Extract one dashboard or title GPD from the profile package."] = "Extrae un GPD del panel o de un título desde el paquete de perfil.",
        ["Inspect title records inside a profile package."] = "Inspecciona los registros de títulos dentro de un paquete de perfil.",
        ["List title records from the dashboard GPD."] = "Lista los registros de títulos del GPD del panel.",
        ["Insert or refresh one dashboard title record."] = "Inserta o actualiza un registro de título del panel.",
        ["Inspect and edit achievement data inside a profile package."] = "Inspecciona y edita datos de logros dentro de un paquete de perfil.",
        ["List achievements for one title GPD."] = "Lista los logros de un GPD de título.",
        ["Unlock one profile achievement and update title totals."] = "Desbloquea un logro del perfil y actualiza los totales del título.",
        ["Lock one profile achievement and update title totals."] = "Bloquea un logro del perfil y actualiza los totales del título.",
        ["Inspect and edit profile settings stored in the dashboard GPD."] = "Inspecciona y edita la configuración del perfil almacenada en el GPD del panel.",
        ["Inspect and edit profile settings stored in the dashboard profile GPD."] = "Inspecciona y edita la configuración del perfil almacenada en el GPD del panel.",
        ["List profile settings."] = "Lista la configuración del perfil.",
        ["Read a single profile setting."] = "Lee una sola configuración del perfil.",
        ["Create or update a single profile setting."] = "Crea o actualiza una sola configuración del perfil.",
        ["Inspect and edit avatar color values stored in the dashboard profile setting blob."] = "Inspecciona y edita los valores de color del avatar almacenados en el blob de configuración del perfil del panel.",
        ["Inspect and edit avatar color values stored in the dashboard profile settings blob."] = "Inspecciona y edita los valores de color del avatar almacenados en el blob de configuración del perfil del panel.",
        ["Show avatar ARGB color values."] = "Muestra los valores de color ARGB del avatar.",
        ["Update one or more avatar ARGB color values."] = "Actualiza uno o más valores de color ARGB del avatar.",
        ["Inspect and extract records from local GPD/XDBF files"] = "Inspecciona y extrae registros de archivos GPD/XDBF locales",
        ["Inspect and extract records from local GPD/XDBF files."] = "Inspecciona y extrae registros de archivos GPD/XDBF locales.",
        ["List records in a GPD/XDBF file."] = "Lista los registros de un archivo GPD/XDBF.",
        ["Read a single record from a GPD/XDBF file."] = "Lee un solo registro de un archivo GPD/XDBF.",
        ["Extract records to a directory."] = "Extrae registros a un directorio.",
        ["Show records that are pending sync."] = "Muestra los registros pendientes de sincronización.",
        ["Ghidra headless helpers (Free, external install required)"] = "Herramientas sin interfaz de Ghidra (gratis, requiere instalación externa)",
        ["Ghidra headless helpers (Free, external install required)."] = "Herramientas sin interfaz de Ghidra (gratis, requiere instalación externa).",
        ["Download or install XEXLoaderWV into the configured Ghidra install."] = "Descarga o instala XEXLoaderWV en la instalación de Ghidra configurada.",
        ["IDA Pro headless helpers (IDA 9.3 supported, external install required)"] = "Herramientas sin interfaz de IDA Pro (IDA 9.3 compatible, requiere instalación externa)",
        ["IDA Pro headless helpers (IDA 9.3 supported, external install required)."] = "Herramientas sin interfaz de IDA Pro (IDA 9.3 compatible, requiere instalación externa).",
        ["Configure IDA install, python, and backend paths."] = "Configura las rutas de instalación de IDA, Python y el backend.",
        ["Verify the configured IDA environment."] = "Verifica el entorno configurado de IDA.",
        ["Download or install the supported loader set into IDA."] = "Descarga o instala en IDA el conjunto de cargadores compatible.",
        ["Use a local idaxex archive instead of downloading one."] = "Usa un archivo local de idaxex en lugar de descargar uno.",
        ["Import a XEX into an IDA database headlessly."] = "Importa un XEX a una base de datos de IDA sin interfaz.",
        ["Decompile a XEX or IDA database to C."] = "Descompila un XEX o una base de datos de IDA a C.",
        ["Verify IDA decompiler output for obvious failures."] = "Verifica la salida del descompilador de IDA en busca de fallos evidentes.",
        ["Current user: unavailable"] = "Usuario actual: no disponible",
        ["Hosted avatar library"] = "Biblioteca de avatares alojada",
        ["Local avatar collection"] = "Colección local de avatares",
        ["All Tags"] = "Todas las etiquetas",
        ["Selected"] = "Seleccionados",
        ["visible item(s)"] = "elemento(s) visibles",
        ["items"] = "elementos",
        ["titles"] = "titulos",
        ["local corpus"] = "corpus local"
    };

    public static CultureInfo Culture { get; private set; } = EnglishCulture;

    public static string CurrentLanguageCode => ReferenceEquals(Culture, SpanishCulture) ? "es" : "en";

    public static bool IsSpanish => CurrentLanguageCode == "es";

    public static void Initialize(string? code)
    {
        Culture = NormalizeLanguageCode(code) == "es" ? SpanishCulture : EnglishCulture;
    }

    public static string NormalizeLanguageCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "en";
        }

        string normalized = value.Trim().Replace('_', '-').ToLowerInvariant();
        if (normalized is "es" or "es-es" or "es-mx" or "spanish" or "espanol" or "español")
        {
            return "es";
        }

        return "en";
    }

    public static string GetDefaultLanguageCode() =>
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";

    public static string[] ExtractLanguageArgument(string[] args, out string? language)
    {
        language = null;
        if (args.Length == 0)
        {
            return args;
        }

        List<string> output = new(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--lang", StringComparison.OrdinalIgnoreCase) || arg.Equals("--language", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    language = args[i + 1];
                    i++;
                }

                continue;
            }

            const string LangPrefix = "--lang=";
            const string LanguagePrefix = "--language=";
            if (arg.StartsWith(LangPrefix, StringComparison.OrdinalIgnoreCase))
            {
                language = arg[LangPrefix.Length..];
                continue;
            }

            if (arg.StartsWith(LanguagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                language = arg[LanguagePrefix.Length..];
                continue;
            }

            output.Add(arg);
        }

        return output.ToArray();
    }

    public static string? TryReadLanguageFromJsonConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("UiLanguage", out JsonElement languageElement) || languageElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return languageElement.GetString();
        }
        catch
        {
            return null;
        }
    }

    public static string Translate(string? text)
    {
        if (!IsSpanish || string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        if (text.Length == 0)
        {
            return string.Empty;
        }

        int prefixLength = 0;
        while (prefixLength < text.Length && char.IsWhiteSpace(text[prefixLength]))
        {
            prefixLength++;
        }

        int suffixLength = 0;
        while (suffixLength < text.Length - prefixLength && char.IsWhiteSpace(text[text.Length - suffixLength - 1]))
        {
            suffixLength++;
        }

        string prefix = prefixLength == 0 ? string.Empty : text[..prefixLength];
        string suffix = suffixLength == 0 ? string.Empty : text[(text.Length - suffixLength)..];
        string core = text.Substring(prefixLength, text.Length - prefixLength - suffixLength);
        if (core.Length == 0)
        {
            return text;
        }

        if (LooksLikeJson(core))
        {
            return text;
        }

        if (TryTranslateExact(core, out string? translated))
        {
            return prefix + translated + suffix;
        }

        string transformed = core;
        foreach ((Regex pattern, string replacement) in RegexReplacements)
        {
            transformed = pattern.Replace(transformed, replacement);
        }

        foreach ((string source, string target) in PhraseReplacements.OrderByDescending(static pair => pair.Source.Length))
        {
            transformed = transformed.Replace(source, target, StringComparison.Ordinal);
        }

        return prefix + transformed + suffix;
    }

    private static bool TryTranslateExact(string text, out string translated)
    {
        if (TryResolveLookup(text, out translated))
        {
            return true;
        }

        string normalized = CollapseWhitespace(text);
        if (!string.Equals(normalized, text, StringComparison.Ordinal) && TryResolveLookup(normalized, out translated))
        {
            return true;
        }

        if (TryTranslateByTerminalPunctuation(text, out translated))
        {
            return true;
        }

        if (!string.Equals(normalized, text, StringComparison.Ordinal) && TryTranslateByTerminalPunctuation(normalized, out translated))
        {
            return true;
        }

        translated = string.Empty;
        return false;
    }

    private static bool TryResolveLookup(string text, out string translated)
    {
        if (ManualOverrides.TryGetValue(text, out string? manualTranslation) && manualTranslation is not null)
        {
            translated = manualTranslation;
            return true;
        }

        if (ExactTranslations.TryGetValue(text, out string? exactTranslation) && exactTranslation is not null)
        {
            translated = exactTranslation;
            return true;
        }

        translated = string.Empty;
        return false;
    }

    private static bool TryTranslateByTerminalPunctuation(string text, out string translated)
    {
        const string TerminalPunctuation = ".:!?;";
        char[] punctuationChars = TerminalPunctuation.ToCharArray();
        string trimmed = text.TrimEnd(punctuationChars);
        if (trimmed.Length > 0 && trimmed.Length != text.Length)
        {
            string punctuation = text[trimmed.Length..];
            if (TryResolveLookup(trimmed, out string translatedWithoutPunctuation))
            {
                translated = translatedWithoutPunctuation + punctuation;
                return true;
            }
        }
        else
        {
            foreach (char punctuation in punctuationChars)
            {
                if (TryResolveLookup(text + punctuation, out string translatedWithPunctuation))
                {
                    translated = translatedWithPunctuation.TrimEnd(punctuation);
                    return true;
                }
            }
        }

        translated = string.Empty;
        return false;
    }

    private static string CollapseWhitespace(string value) =>
        Regex.Replace(value, @"\s+", " ");

    private static bool LooksLikeJson(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed is "{" or "}" or "[" or "]" or "}," or "]," or "true" or "false" or "null")
        {
            return true;
        }

        if (trimmed[0] == '{' || trimmed[0] == '[')
        {
            return true;
        }

        return trimmed[0] == '"' && trimmed.Contains("\":", StringComparison.Ordinal);
    }

    public static string TranslateMarkup(string? markup)
    {
        if (!IsSpanish || string.IsNullOrEmpty(markup))
        {
            return markup ?? string.Empty;
        }

        StringBuilder builder = new(markup.Length);
        int index = 0;
        while (index < markup.Length)
        {
            int tagStart = markup.IndexOf('[', index);
            if (tagStart < 0)
            {
                builder.Append(Translate(markup[index..]));
                break;
            }

            if (tagStart > index)
            {
                builder.Append(Translate(markup[index..tagStart]));
            }

            int tagEnd = markup.IndexOf(']', tagStart);
            if (tagEnd < 0)
            {
                builder.Append(Translate(markup[tagStart..]));
                break;
            }

            builder.Append(markup, tagStart, tagEnd - tagStart + 1);
            index = tagEnd + 1;
        }

        return builder.ToString();
    }

    public static bool IsAffirmative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "y" or "yes" or "s" or "si" or "sí";
    }

    public static bool IsNegative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "n" or "no";
    }
}
