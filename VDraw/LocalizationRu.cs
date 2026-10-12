namespace VDraw;

/// <summary>
/// Русские соответствия английским строкам интерфейса.
///
/// Ключ повторяет исходный литерал ДОСЛОВНО, включая двойные пробелы перед
/// сочетанием клавиш и переносы \r\n. Расхождение в одном пробеле — это
/// молча не сработавший перевод, поэтому ключи брались из кода, а не
/// набирались заново.
///
/// Сочетания клавиш не переводятся: Ctrl и Shift на русской раскладке
/// подписаны так же, а буква в сочетании привязана к клавише, а не к слову.
/// </summary>
internal static class LocalizationRu
{
	public static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
	{
		#region Подсказки: документ

		["New document  (Ctrl+N)\r\nAsks for paper format, then replaces the current document."] =
			"Новый документ  (Ctrl+N)\r\nСпрашивает формат листа и заменяет текущий документ.",

		["Open document  (Ctrl+O)\r\nOpens a .vdr file. Unsaved changes are offered for saving first."] =
			"Открыть документ  (Ctrl+O)\r\nОткрывает файл .vdr. Несохранённые изменения сначала предлагается сохранить.",

		["Save  (Ctrl+S)\r\nSaves to the current file. A new document asks for a name."] =
			"Сохранить  (Ctrl+S)\r\nСохраняет в текущий файл. Для нового документа спрашивает имя.",

		["Save as...  (Ctrl+Shift+S)\r\nSaves under a new name and keeps working in the new file."] =
			"Сохранить как...  (Ctrl+Shift+S)\r\nСохраняет под новым именем и продолжает работу в новом файле.",

		["Import  (Ctrl+I)\r\nInserts shapes from a .vdr or pages from a .pdf into the current document, and adds .vdrs sets and Visio stencils to custom shapes."] =
			"Импорт  (Ctrl+I)\r\nВставляет фигуры из .vdr или страницы из .pdf в текущий документ, " +
			"а наборы .vdrs и клипарты Visio добавляет в кастомные фигуры.",

		["Import document  (Ctrl+I)\r\nInserts shapes from another .vdr into the current page. Paper settings are not imported."] =
			"Импорт документа  (Ctrl+I)\r\nВставляет фигуры из другого .vdr в текущую страницу. Параметры листа не импортируются.",

		["Export selection  (Ctrl+E)\r\nWrites the selected shapes to PNG, JPEG, BMP or SVG."] =
			"Экспорт выделенного  (Ctrl+E)\r\nСохраняет выделенные фигуры в PNG, JPEG, BMP или SVG.",

		["Print  (Ctrl+P)"] = "Печать  (Ctrl+P)",
		["Help  (F1)"] = "Справка  (F1)",
		["Untitled"] = "Без имени",

		#endregion

		#region Подсказки: правка

		["Copy  (Ctrl+C)\r\nCopies the selected shapes. In image mode copies the pixel selection."] =
			"Копировать  (Ctrl+C)\r\nКопирует выделенные фигуры. В режиме изображения — выделенные пиксели.",

		["Paste  (Ctrl+V)"] = "Вставить  (Ctrl+V)",
		["Undo  (Ctrl+Z)"] = "Отменить  (Ctrl+Z)",
		["Redo  (Ctrl+Y)"] = "Повторить  (Ctrl+Y)",
		["Delete selected  (Del)"] = "Удалить выделенное  (Del)",

		#endregion

		#region Подсказки: инструменты фигур

		["Select  (Esc)\r\nPick, move, resize and rotate shapes."] =
			"Выбор  (Esc)\r\nВыделение, перемещение, изменение размера и поворот фигур.",

		["Straight lines  (L)\r\nClick to add nodes, right-click or Enter to finish."] =
			"Ломаная  (L)\r\nЩелчок добавляет узел, правая кнопка или Enter завершает.",

		["Curve  (B)\r\nClick to add nodes, right-click or Enter to finish."] =
			"Кривая  (B)\r\nЩелчок добавляет узел, правая кнопка или Enter завершает.",

		["Rectangle  (R)\r\nArrow opens plain, rounded and chamfered variants."] =
			"Прямоугольник  (R)\r\nСтрелка открывает обычный, скруглённый и со срезанными углами.",

		["Circle  (C)\r\nArrow switches between by centre and radius, and by three points."] =
			"Окружность  (C)\r\nСтрелка переключает построение по центру и радиусу и по трём точкам.",

		["Ellipse  (E)\r\nArrow switches between by three points and by five points."] =
			"Эллипс  (E)\r\nСтрелка переключает построение по трём точкам и по пяти.",

		["Regular polygon  (P)\r\nFirst click sets the centre, second the radius."] =
			"Правильный многоугольник  (P)\r\nПервый щелчок задаёт центр, второй — радиус.",

		["Free polygon  (Shift+P)\r\nClick to add vertices, right-click or Enter to close."] =
			"Произвольный многоугольник  (Shift+P)\r\nЩелчок добавляет вершину, правая кнопка или Enter замыкает.",

		["Text  (T)"] = "Текст  (T)",

		["Image  (Ctrl+Shift+I)\r\nPlaces a raster image into a closed contour."] =
			"Изображение  (Ctrl+Shift+I)\r\nПомещает растровую картинку в замкнутый контур.",

		["Measure distance  (M)\r\nThree clicks: from, to, and the height of the value label. Ends snap to shape points."] =
			"Измерить расстояние  (M)\r\nТри щелчка: откуда, куда и высота подписи. Концы притягиваются к точкам фигур.",

		["Image editing mode  (Ctrl+M)\r\nSwitches the toolbar to brushes and pixel selection for the selected image."] =
			"Режим правки изображения  (Ctrl+M)\r\nПереключает панель на кисти и выделение пикселей выбранной картинки.",

		["Default outline colour\r\nApplies to newly created shapes."] =
			"Цвет обводки по умолчанию\r\nПрименяется к вновь создаваемым фигурам.",

		["Default fill colour\r\nApplies to newly created shapes."] =
			"Цвет заливки по умолчанию\r\nПрименяется к вновь создаваемым фигурам.",

		#endregion

		#region Подсказки: упорядочение

		["Group  (Ctrl+G)"] = "Сгруппировать  (Ctrl+G)",
		["Ungroup  (Ctrl+Shift+G)"] = "Разгруппировать  (Ctrl+Shift+G)",
		["Bring to front  (Ctrl+Shift+Up)"] = "На передний план  (Ctrl+Shift+Up)",
		["Send to back  (Ctrl+Shift+Down)"] = "На задний план  (Ctrl+Shift+Down)",
		["Flip horizontally  (Shift+H)"] = "Отразить по горизонтали  (Shift+H)",
		["Flip vertically  (Shift+V)"] = "Отразить по вертикали  (Shift+V)",
		["Rotate 90 clockwise  (Ctrl+Right)"] = "Повернуть на 90 по часовой  (Ctrl+Right)",
		["Rotate 90 counter-clockwise  (Ctrl+Left)"] = "Повернуть на 90 против часовой  (Ctrl+Left)",

		#endregion

		#region Подсказки: булевы операции

		["Union  (Ctrl+Alt+U)\r\nMerges the selected shapes into one."] =
			"Объединение  (Ctrl+Alt+U)\r\nСливает выделенные фигуры в одну.",

		["Subtract  (Ctrl+Alt+S)\r\nRemoves the upper shapes from the lower one."] =
			"Вычитание  (Ctrl+Alt+S)\r\nУдаляет верхние фигуры из нижней.",

		["Intersect  (Ctrl+Alt+I)\r\nKeeps only the overlapping area."] =
			"Пересечение  (Ctrl+Alt+I)\r\nОставляет только общую область.",

		["Exclude  (Ctrl+Alt+X)\r\nKeeps everything except the overlapping area."] =
			"Исключение  (Ctrl+Alt+X)\r\nОставляет всё, кроме общей области.",

		["Combine  (Ctrl+Alt+C)\r\nJoins the shapes into one curve keeping separate contours."] =
			"Соединение  (Ctrl+Alt+C)\r\nСобирает фигуры в одну кривую, сохраняя отдельные контуры.",

		#endregion

		#region Подсказки: узлы

		["Convert to curve  (Ctrl+Q)\r\nTurns a primitive into an editable curve. The step cannot be reversed except by Undo."] =
			"Преобразовать в кривую  (Ctrl+Q)\r\nДелает из примитива редактируемую кривую. Обратного действия нет, только отмена.",

		["Edit nodes  (N)\r\nShows the nodes and control handles of the selected curve."] =
			"Правка узлов  (N)\r\nПоказывает узлы и контрольные точки выбранной кривой.",

		["Cut with a straight line  (K)"] = "Разрезать прямой  (K)",
		["Cut with a curve  (Shift+K)"] = "Разрезать кривой  (Shift+K)",

		["Close the cut edges\r\nWhen on, the parts produced by a cut get a closed contour."] =
			"Замыкать края реза\r\nЧасти, полученные разрезом, получают замкнутый контур.",

		["Merge nodes  (Ctrl+J)\r\nJoins two selected ends into one node."] =
			"Объединить узлы  (Ctrl+J)\r\nСводит два выделенных конца в один узел.",

		["Split node  (Ctrl+Shift+J)\r\nBreaks the contour at the focused node."] =
			"Разделить узел  (Ctrl+Shift+J)\r\nРазрывает контур в текущем узле.",

		["Straight node  (1)\r\nDrops the control handles: both segments become straight."] =
			"Прямой узел  (1)\r\nУбирает контрольные точки: оба сегмента становятся прямыми.",

		["Smooth node  (2)\r\nControl handles stay collinear, the curve passes through smoothly."] =
			"Гладкий узел  (2)\r\nКонтрольные точки остаются на одной прямой, кривая проходит плавно.",

		["Cusp node  (3)\r\nControl handles move independently, a corner is allowed."] =
			"Острый узел  (3)\r\nКонтрольные точки двигаются независимо, допускается излом.",

		["Delete nodes  (Del in node mode)"] = "Удалить узлы  (Del в режиме узлов)",

		#endregion

		#region Подсказки: вид и страница

		["Rulers  (Ctrl+R)"] = "Линейки  (Ctrl+R)",

		["Guides  (Ctrl+;)\r\nDrag a guide out of a ruler."] =
			"Направляющие  (Ctrl+;)\r\nНаправляющая вытягивается из линейки.",

		["Snap to objects  (Ctrl+Shift+N)\r\nHold Ctrl while dragging to suspend it temporarily."] =
			"Привязка к объектам  (Ctrl+Shift+N)\r\nCtrl при перетаскивании временно отключает её.",

		["Dynamic guides\r\nShows alignment lines to nearby shapes while dragging."] =
			"Динамические направляющие\r\nПоказывают линии выравнивания к соседним фигурам при перетаскивании.",

		["Zoom\r\nPick a value or type your own. Ctrl+wheel zooms at the cursor."] =
			"Масштаб\r\nВыберите значение или введите своё. Ctrl+колесо масштабирует у курсора.",

		["Zoom to selection  (Ctrl+Shift+0)"] = "Масштаб по выделению  (Ctrl+Shift+0)",
		["Zoom to all shapes  (Ctrl+0)"] = "Масштаб по всем фигурам  (Ctrl+0)",
		["Fit page  (Ctrl+1)"] = "Вписать страницу  (Ctrl+1)",

		["Page properties\r\nFormat, size, units and background of the current page."] =
			"Свойства страницы\r\nФормат, размер, единицы и фон текущей страницы.",

		["Paper format\r\nChoose Custom to enter your own width and height."] =
			"Формат листа\r\nВыберите Custom, чтобы задать свои ширину и высоту.",

		["Lock aspect ratio\r\nWhen on, changing one side changes the other proportionally."] =
			"Сохранять пропорции\r\nИзменение одной стороны пропорционально меняет вторую.",

		["Portrait orientation"] = "Книжная ориентация",
		["Landscape orientation"] = "Альбомная ориентация",
		["Printable area borders"] = "Границы области печати",
		["Grid  (Ctrl+#)"] = "Сетка  (Ctrl+#)",
		["Page background colour"] = "Цвет фона страницы",

		#endregion

		#region Подсказки: правка изображения

		["Eraser  (E in image mode)"] = "Ластик  (E в режиме изображения)",

		["Brush  (B in image mode)\r\nSoft edge, strength and smoothing apply."] =
			"Кисть  (B в режиме изображения)\r\nДействуют мягкость края, сила и сглаживание.",

		["Rectangular selection  (R in image mode)"] = "Прямоугольное выделение  (R в режиме изображения)",

		["Lasso selection  (L in image mode)\r\nShift adds to the selection, Alt subtracts."] =
			"Выделение лассо  (L в режиме изображения)\r\nShift добавляет к выделению, Alt вычитает.",

		["Fill  (G in image mode)\r\nFills an area of similar colour. Tolerance sets how similar."] =
			"Заливка  (G в режиме изображения)\r\nЗаливает область близкого цвета. Допуск задаёт степень близости.",

		["Lighten\r\nBrightens pixels under the brush."] = "Осветление\r\nОсветляет пиксели под кистью.",
		["Darken\r\nDarkens pixels under the brush."] = "Затемнение\r\nЗатемняет пиксели под кистью.",
		["Contrast\r\nRaises contrast under the brush."] = "Контраст\r\nПовышает контраст под кистью.",

		["Black and white\r\nDesaturates pixels under the brush."] =
			"Чёрно-белое\r\nОбесцвечивает пиксели под кистью.",

		["Foreground colour\r\nDrawing colour for brush and fill."] =
			"Основной цвет\r\nЦвет рисования для кисти и заливки.",

		["Background colour\r\nWhat the eraser leaves behind."] =
			"Фоновый цвет\r\nТо, что остаётся после ластика.",

		#endregion

		#region Кнопка языка

		["Switch interface language\r\nCurrent: English. Click to switch to Russian."] =
			"Переключить язык интерфейса\r\nСейчас: русский. Нажмите, чтобы перейти на английский.",

		#endregion

		#region Меню

		["File"] = "Файл",
		["New"] = "Создать",
		["Open"] = "Открыть",
		["Save"] = "Сохранить",
		["Save as"] = "Сохранить как",
		["Export image"] = "Экспорт изображения",
		["Width (px)"] = "Ширина (px)",
		["Height (px)"] = "Высота (px)",
		["Keep aspect ratio"] = "Сохранять пропорции",
		["Transparent background"] = "Прозрачный фон",
		["Background..."] = "Фон...",
		["OK"] = "ОК",
		["Cancel"] = "Отмена",
		["Pixels: {0:N0}"] = "Пикселей: {0:N0}",
		["Proportions differ from the original."] = "Пропорции отличаются от исходных.",
		["Padding (px)"] = "Поля (px)",
		["Left"] = "Слева",
		["Top"] = "Сверху",
		["Right"] = "Справа",
		["Bottom"] = "Снизу",
		["Content: {0} x {1}"] = "Содержимое: {0} x {1}",
		["Padding leaves no room for content."] = "Поля не оставляют места для содержимого.",
		["Export animated GIF"] = "Экспорт анимации GIF",
		["Frames per second"] = "Кадров в секунду",
		["Exported range"] = "Экспортируемый диапазон",
		["First frame"] = "Первый кадр",
		["Last frame"] = "Последний кадр",
		["Play"] = "Пуск",
		["Stop"] = "Стоп",
		["Frame {0} / {1}"] = "Кадр {0} из {1}",
		["Frames: {0}"] = "Кадров: {0}",
		["Duration: {0:0.0} s"] = "Длительность: {0:0.0} с",
		["Use page space for export"] = "Использовать область страницы",
		["Captured area, page units"] = "Снимаемая область, единицы страницы",
		["Area: {0:0.##} x {1:0.##} page units"] = "Область: {0:0.##} x {1:0.##} ед. страницы",
		["Area: content bounds across all frames"] = "Область: габариты содержимого по всем кадрам",
		["GIF stores at most 256 colours per frame."] = "GIF хранит не более 256 цветов на кадр.",
		["Drag to pan, wheel to zoom, double-click to fit."] = "Перетаскивание — сдвиг, колесо — масштаб, двойной щелчок — вписать.",

		["Import"] = "Импорт",
		["Export"] = "Экспорт",
		["Print"] = "Печать",
		["Close"] = "Закрыть",
		["Exit"] = "Выход",

		["Document"] = "Документ",
		["Page"] = "Страница",
		["Objects"] = "Объекты",

		["Lines"] = "Ломаная",
		["Curve"] = "Кривая",
		["Rectangle"] = "Прямоугольник",
		["Rounded Rectangle"] = "Скруглённый прямоугольник",
		["Chamfered Rectangle"] = "Скошеный прямоугольник",
		["Actual dpi"] = "Реальное разрешение",
		["Invert angle"] = "Внешний угол",
		["Measure angle  (Shift+M)\r\nDefines two vectors, then places the value label. Click a straight segment to take it whole, or click two points."] =
			"Измерить угол  (Shift+M)\r\nЗадаёт два вектора, затем положение подписи. Щелчок по прямому сегменту берёт его целиком, либо задайте две точки.",
		["Circle 2-points"] = "Окружность по 2 точкам",
		["Circle 3-points"] = "Окружность по 3 точкам",
		["Ellipse 3-points"] = "Эллипс по 3 точкам",
		["Ellipse 5-points"] = "Эллипс по 5 точкам",
		["Polygon"] = "Многоугольник",
		["Measure distance"] = "Измерить расстояние",
		["Measure angle"] = "Измерить угол",
		["Convert to curve"] = "Преобразовать в кривую",

		#endregion

		#region Заголовки панели свойств

		["General"] = "Общие",
		["Transform"] = "Преобразование",
		["Line"] = "Линия",
		["Fill"] = "Заливка",
		["Shadow / Glow"] = "Тень / свечение",
		["Transparency"] = "Прозрачность",
		["Link"] = "Привязка",
		["Image"] = "Изображение",

		#endregion

		#region Подписи строк панели свойств

		["X"] = "X",
		["Y"] = "Y",
		["Width"] = "Ширина",
		["Height"] = "Высота",
		["Rotation"] = "Поворот",
		["Rotate"] = "Поворот",
		["Layer"] = "Слой",
		["Sides"] = "Сторон",
		["Enabled"] = "Включено",
		["Color"] = "Цвет",
		["Colour"] = "Цвет",
		["Thickness"] = "Толщина",
		["Name"] = "Имя",
		["Pattern"] = "Штрих",
		["Length"] = "Длина",
		["pattern|Size"] = "Размер",
		["Add pattern"] = "Добавить штрих",
		["Delete pattern"] = "Удалить штрих",
		["Start"] = "Начало",
		["End"] = "Конец",
		["Start size"] = "Размер начала",
		["End size"] = "Размер конца",
		["Type"] = "Тип",
		["Style"] = "Начертание",
		["Font"] = "Шрифт",
		["Size"] = "Кегль",
		["Alignment"] = "Выравнивание",
		["Char spacing"] = "Межбуквенный интервал",
		["Line spacing"] = "Межстрочный интервал",
		["Spacing"] = "Интервал",
		["Along path"] = "Вдоль контура",
		["Tangent to curve"] = "По касательной",
		["Inside shape"] = "Внутри фигуры",
		["Wrapping"] = "Обтекание",
		["Wrap others"] = "Обтекать другими",
		["Padding"] = "Отступ",
		["Columns"] = "Колонок",
		["Column gap"] = "Промежуток колонок",
		["Texture"] = "Текстура",
		["Tiling"] = "Замощение",
		["Smooth"] = "Сглаживание",
		["Softness"] = "Мягкость",
		["Steps"] = "Шагов",
		["Stops"] = "Точки",
		["Cells"] = "Ячейки",
		["Clipping"] = "Отсечение",
		["Scale"] = "Масштаб",
		["Horizontal"] = "По горизонтали",
		["Vertical"] = "По вертикали",
		["Front"] = "Спереди",
		["Back"] = "Сзади",
		["From"] = "От",
		["To"] = "До",
		["Edit"] = "Правка",
		["Text"] = "Текст",
		["Stroke"] = "Обводка",
		["Value"] = "Значение",
		["Factor"] = "Множитель",
		["Digits"] = "Знаков",

		["Radius"] = "Радиус",
		["Round X"] = "Скругление X",
		["Round Y"] = "Скругление Y",
		["Radius X"] = "Радиус X",
		["Radius Y"] = "Радиус Y",
		["Chamfer X"] = "Срез X",
		["Chamfer Y"] = "Срез Y",
		["Brightness"] = "Яркость",
		["Contrast"] = "Контраст",
		["Exposure"] = "Экспозиция",
		["Hue"] = "Тон",
		["Temperature"] = "Температура",
		["Tint"] = "Оттенок",
		["Vibrancy"] = "Насыщенность",
		["Sharpness"] = "Резкость",
		["Shadows"] = "Тени",
		["Noise reduction"] = "Подавление шума",
		["Invert"] = "Негатив",
		["B/W"] = "Ч/Б",

		#endregion

		#region Сообщения импорта

		["Import"] = "Импорт",
		["Import PDF"] = "Импорт PDF",
		["Import error"] = "Ошибка импорта",
		["Import notes"] = "Замечания импорта",
		["The document contains no shapes to import."] = "В документе нет фигур для импорта.",
		["The PDF contains no pages."] = "В PDF нет страниц.",
		["The PDF contains no importable content."] = "В PDF нет содержимого, пригодного для импорта.",

		#endregion

		#region Кастомные фигуры

		["Copy"] = "Копировать",
		["Paste"] = "Вставить",
		["Convert to curves"] = "Преобразовать в кривые",
		["Convert to bitmap"] = "Преобразовать в растр",
		["Group selected"] = "Сгруппировать выделенное",
		["Ungroup selected"] = "Разгруппировать выделенное",
		["Delete"] = "Удалить",

		["Settings"] = "Настройки",
		["Restore defaults"] = "Значения по умолчанию",
		["Restore all settings on this page to their defaults?"] =
			"Вернуть все настройки к значениям по умолчанию?",
		["Apply"] = "Применить",
		["View"] = "Вид",
		["Grid"] = "Сетка",
		["Rulers"] = "Линейки",
		["Snapping"] = "Привязки",
		["Shape defaults"] = "Фигуры по умолчанию",
		["Handles"] = "Маркеры",
		["Interface theme"] = "Тема оформления",
		["Show rulers"] = "Показывать линейки",
		["Show guidelines"] = "Показывать направляющие",
		["Show grid"] = "Показывать сетку",
		["Show print margins"] = "Показывать поля печати",
		["Canvas background"] = "Фон холста",
		["Page background"] = "Фон страницы",
		["Page border"] = "Рамка страницы",
		["Page shadow"] = "Тень страницы",
		["Minor lines"] = "Основные линии",
		["Every fifth line"] = "Каждая пятая линия",
		["Every tenth line"] = "Каждая десятая линия",
		["Origin axes"] = "Оси начала координат",
		["Printable area border"] = "Рамка области печати",
		["Ruler thickness"] = "Толщина линейки",
		["Origin mark length"] = "Длина метки нуля",
		["Major mark length"] = "Длина крупной метки",
		["Minor mark length"] = "Длина мелкой метки",
		["Snap to objects"] = "Привязка к объектам",
		["Snap to guidelines"] = "Привязка к направляющим",
		["Dynamic guides"] = "Динамические направляющие",
		["Snap distance (px)"] = "Расстояние привязки (px)",
		["Snap marker size"] = "Размер метки привязки",
		["Snap marker colour"] = "Цвет метки привязки",
		["Snap line colour"] = "Цвет линии привязки",
		["Default outline colour"] = "Цвет обводки по умолчанию",
		["Default outline width (mm)"] = "Толщина обводки по умолчанию (мм)",
		["Default dash cycle (mm)"] = "Длина цикла штриха по умолчанию (мм)",
		["Default fill colour"] = "Цвет заливки по умолчанию",
		["Handle size"] = "Размер маркера",
		["Handle outline width"] = "Толщина обводки маркера",
		["Handle fill"] = "Заливка маркера",
		["Handle outline"] = "Обводка маркера",
		["Grab radius (px)"] = "Радиус захвата (px)",
		["Curve node size"] = "Размер узла кривой",
		["Control point size"] = "Размер контрольной точки",

		["Custom shapes"] = "Кастомные фигуры",
		["Add selection to custom shape"] = "Добавить выделенное в кастомные фигуры",
		["Import custom shapes (*.vdrs)"] = "Импорт кастомных фигур (*.vdrs)",
		["Import Visio file (*.vsdx, *.vss, *.vssx)"] = "Импорт файла Visio (*.vsdx, *.vss, *.vssx)",
		["Import custom shapes"] = "Импорт кастомных фигур",
		["Import Visio file"] = "Импорт файла Visio",
		["This document"] = "Этот документ",
		["No custom shapes yet"] = "Кастомных фигур пока нет",
		["Unload this stencil"] = "Выгрузить этот клипарт",
		["Name of the custom shape:"] = "Имя кастомной фигуры:",
		["Select at least one shape first."] = "Сначала выделите хотя бы одну фигуру.",
		["Delete the custom shape \"{0}\"?"] = "Удалить кастомную фигуру «{0}»?",
		["Failed to insert the custom shape."] = "Не удалось вставить кастомную фигуру.",
		["Failed to save the custom shape to the library."] =
			"Не удалось сохранить кастомную фигуру в библиотеку.",
		["These files could not be read:"] = "Не удалось прочитать файлы:",
		["Stencil shapes are not saved with the document and are kept for this session only."] =
			"Фигуры клипарта не сохраняются с документом и живут только до конца сеанса.",
		["no shapes found"] = "фигур не найдено",

		#endregion
	};
}