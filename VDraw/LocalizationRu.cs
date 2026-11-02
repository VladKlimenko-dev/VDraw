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

		["Import  (Ctrl+I)\r\nInserts shapes from a .vdr or pages from a .pdf into the current document,\r\nand adds .vdrs sets and Visio stencils to custom shapes."] =
			"Импорт  (Ctrl+I)\r\nВставляет фигуры из .vdr или страницы из .pdf в текущий документ,\r\n" +
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

		["Lasso selection  (L in image mode)\r\nCtrl adds to the selection."] =
			"Выделение лассо  (L в режиме изображения)\r\nCtrl добавляет к выделению.",

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
			"Измерить угол  (Shift+M)\r\nЗадаёт два вектора, затем положение подписи.\r\n"
			+ "Щелчок по прямому сегменту берёт его целиком, либо задайте две точки.",
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
		["Front"] = "Основной",
		["Back"] = "Фоновый",
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

		#region Вкладка сочетаний клавиш

		["Shortcuts"] = "Сочетания клавиш",
		["Reset keys to default"] = "Сбросить сочетания",
		["Reset mouse to default"] = "Сбросить мышь",
		["Click a mouse button..."] = "Щёлкните кнопкой мыши...",
		["Hold the keys and click..."] = "Держите клавиши и щёлкните...",

		["left button"] = "левая кнопка",
		["middle button"] = "средняя кнопка",
		["right button"] = "правая кнопка",
		["wheel"] = "колесо",

		["Pan the view"] = "Панорамирование вида",
		["Cancel the tool, finish the polygon"] = "Отменить инструмент, завершить многоугольник",
		["Add to the selection"] = "Добавить к выделению",
		["Lock the movement to one axis"] = "Держать перемещение по одной оси",
		["Turn snapping off"] = "Отключить привязки",
		["Keep the proportions"] = "Сохранять пропорции",
		["Resize around the centre"] = "Менять размер от центра",
		["Mirror the opposite control point"] = "Зеркалить противоположный рычаг",
		["Do not attach a measurement end"] = "Не привязывать конец измерения",
		["Lock the cutting line to an axis or diagonal"] =
			"Держать линию разреза по оси или диагонали",
		["Add to the pixel selection"] = "Добавить к выделению пикселей",
		["Press keys..."] = "Нажмите клавиши...",
		["none"] = "нет",
		["Measurement default digits"] = "Знаков после точки в измерениях",
		["Selection filter"] = "Фильтр выделения",
		["Light / dark theme"] = "Светлая / тёмная тема",

		["Click a shortcut and press the new key combination.\r\n"
			+ "Click a mouse assignment, then click the mouse button you want,\r\n"
			+ "holding the modifiers. Esc cancels, Delete clears.\r\n"
			+ "Duplicates are shown in red. Muted lines are a reference and cannot be changed."] =
			"Щёлкните сочетание и нажмите новые клавиши.\r\n"
			+ "Щёлкните назначение мыши, затем щёлкните нужной кнопкой,\r\n"
			+ "удерживая модификаторы. Esc — отмена, Delete — очистить.\r\n"
			+ "Совпадения показаны красным. Приглушённые строки — справка, они не настраиваются.",

		// Группы действий
		["Tools"] = "Инструменты",
		["Arrange"] = "Расположение",
		["Boolean operations"] = "Логические операции",
		// «Сдвиг», а не «Сдвиг стрелками»: этой же строкой подписан шаг
		// отмены, а он получается и от перетаскивания мышью.
		["Nudge"] = "Сдвиг",
		["Curve nodes"] = "Узлы кривой",

		// Действия: файл и правка
		["New document"] = "Новый документ",
		["Open document"] = "Открыть документ",
		["Export selection"] = "Экспорт выделенного",
		["Undo"] = "Отменить",
		["Redo"] = "Повторить",
		["Copy formatting"] = "Копировать формат",
		["Paste formatting"] = "Применить формат",
		["Select all"] = "Выделить всё",
		["Delete selected"] = "Удалить выделенное",

		// Действия: инструменты
		["Select"] = "Выбор",
		["Straight lines"] = "Прямые линии",
		["Edit nodes"] = "Правка узлов",
		["Regular polygon"] = "Правильный многоугольник",
		["Free polygon"] = "Произвольный многоугольник",
		["Cut with a straight line"] = "Разрез прямой",
		["Cut with a curve"] = "Разрез кривой",
		["Circle"] = "Окружность",
		["Ellipse"] = "Эллипс",
		["Image editing mode"] = "Режим правки изображения",

		// Действия: расположение
		["Bring to front"] = "На передний план",
		["Send to back"] = "На задний план",
		["Flip horizontally"] = "Отразить по горизонтали",
		["Flip vertically"] = "Отразить по вертикали",
		["Rotate 90 clockwise"] = "Повернуть на 90 по часовой",
		["Rotate 90 counter-clockwise"] = "Повернуть на 90 против часовой",

		// Действия: логические операции
		["Union"] = "Объединение",
		["Subtract"] = "Вычитание",
		["Intersect"] = "Пересечение",
		["Exclude"] = "Исключение",
		["Combine"] = "Соединение",

		// Действия: сдвиг
		["Nudge left"] = "Сдвиг влево",
		["Nudge right"] = "Сдвиг вправо",
		["Nudge up"] = "Сдвиг вверх",
		["Nudge down"] = "Сдвиг вниз",
		["Nudge left, large step"] = "Сдвиг влево, крупный шаг",
		["Nudge right, large step"] = "Сдвиг вправо, крупный шаг",
		["Nudge up, large step"] = "Сдвиг вверх, крупный шаг",
		["Nudge down, large step"] = "Сдвиг вниз, крупный шаг",

		// Действия: узлы
		["Straight node"] = "Прямой узел",
		["Smooth node"] = "Гладкий узел",
		["Cusp node"] = "Угловой узел",
		["Merge nodes"] = "Объединить узлы",
		["Split node"] = "Разделить узел",

		// Действия: вид
		["Zoom to all shapes"] = "Показать все фигуры",
		["Fit page"] = "Вписать страницу",
		["Zoom to selection"] = "Показать выделенное",
		["Guides"] = "Направляющие",

		#endregion

		#region Справка по кнопкам мыши

		["Mouse: view"] = "Мышь: вид",
		["Mouse: selection"] = "Мышь: выделение",
		["Mouse: moving and resizing"] = "Мышь: перемещение и размер",
		["Mouse: shape tools"] = "Мышь: инструменты фигур",
		["Mouse: curve nodes"] = "Мышь: узлы кривой",
		["Mouse: image editing"] = "Мышь: правка изображения",

		// Жесты
		["Wheel"] = "Колесо",
		["Middle button"] = "Средняя кнопка",
		["Drag from a ruler"] = "Протяжка с линейки",
		["Left button"] = "Левая кнопка",
		["Right button"] = "Правая кнопка",
		["Ctrl + left"] = "Ctrl + левая",
		["Frame, left to right"] = "Рамка слева направо",
		["Frame, right to left"] = "Рамка справа налево",
		["Ctrl + frame"] = "Ctrl + рамка",
		["Centre handle"] = "Центральный маркер",
		["Left drag"] = "Протяжка левой",
		["Ctrl + drag"] = "Ctrl + протяжка",
		["Alt + drag"] = "Alt + протяжка",
		["Handle drag"] = "Протяжка маркером",
		["Ctrl + handle"] = "Ctrl + маркер",
		["Shift + handle"] = "Shift + маркер",
		["Rotation handle"] = "Маркер поворота",
		["Right button, polygon"] = "Правая, многоугольник",
		["Right button, other tools"] = "Правая, другие инструменты",
		["Ctrl, end of a measurement"] = "Ctrl, конец измерения",
		["Shift, cutting line"] = "Shift, линия разреза",
		["Double click"] = "Двойной щелчок",
		["Double click on a segment"] = "Двойной щелчок по сегменту",
		["Left button on a node"] = "Левая по узлу",
		["Ctrl + node"] = "Ctrl + узел",
		["Drag over the curve"] = "Протяжка по кривой",
		["Control point"] = "Рычаг узла",
		["Shift + control point"] = "Shift + рычаг узла",
		["Left with a brush"] = "Левая кистью",
		["Right with a brush"] = "Правая кистью",
		["Left with the fill"] = "Левая заливкой",
		["Right with the fill"] = "Правая заливкой",
		["Left with the eraser"] = "Левая ластиком",
		["Left, selection"] = "Левая, выделение",
		["Ctrl + selection"] = "Ctrl + выделение",

		// Что делают
		["Zoom in and out around the pointer"] = "Масштаб вокруг курсора",
		["Pans the view"] = "Панорамирование вида",
		["Pulls out a guide"] = "Вытягивает направляющую",
		["Selects the shape under the pointer"] = "Выделяет фигуру под курсором",
		["Adds the shape to the selection"] = "Добавляет фигуру к выделенному",
		["Takes only what fits inside entirely"] = "Берёт только вошедшее целиком",
		["Takes everything the frame touches"] = "Берёт всё, чего коснулась рамка",
		["Adds the framed shapes to the selection"] = "Добавляет обведённое к выделенному",
		["Switches resizing and rotating"] = "Переключает размер и поворот",
		["Shape context menu"] = "Контекстное меню фигуры",
		["Moves the selection"] = "Перемещает выделенное",
		["Locks the movement to one axis"] = "Держит перемещение по одной оси",
		["Turns snapping off"] = "Отключает привязки",
		["Resizes"] = "Меняет размер",
		["Keeps the proportions"] = "Сохраняет пропорции",
		["Resizes around the centre"] = "Меняет размер от центра",
		["Rotates around the pivot"] = "Поворачивает вокруг центра вращения",
		["Places the next point"] = "Ставит очередную точку",
		["Finishes the contour"] = "Завершает контур",
		["Cancels what has been started"] = "Отменяет начатое",
		["Does not attach the end to a shape"] = "Не привязывает конец к фигуре",
		["Locks the line to an axis"] = "Держит линию по оси",
		["Enters node editing"] = "Входит в режим узлов",
		["Inserts a node, keeping the curve"] = "Вставляет узел, не меняя кривую",
		["Leaves that node alone selected"] = "Оставляет выбранным один узел",
		["Adds the node to the selected ones, or drops it"] =
			"Добавляет узел к выбранным или снимает",
		["Frames the nodes"] = "Выделяет узлы рамкой",
		["Bends the curve at the node"] = "Изгибает кривую у узла",
		["Mirrors the opposite handle"] = "Зеркалит противоположный рычаг",
		["Opens image editing"] = "Открывает правку изображения",
		["Paints with the foreground colour"] = "Рисует основным цветом",
		["Paints with the background colour"] = "Рисует фоновым цветом",
		["Fills with the foreground colour"] = "Заливает основным цветом",
		["Fills with the background colour"] = "Заливает фоновым цветом",
		["Erases. The right button does not"] = "Стирает. Правая кнопка — нет",
		["Selects pixels, dropping the previous ones"] = "Выделяет пиксели, сбрасывая прежнее",
		["Adds the pixels to the selection"] = "Добавляет пиксели к выделенному",

		#endregion

		#region Остатки дизайнера

		["Ok"] = "ОК",
		["Help"] = "Справка",
		["Group"] = "Группировать",
		["Ungroup"] = "Разгруппировать",
		["Title"] = "Заголовок",
		["Units"] = "Единицы",
		["Format"] = "Формат",
		["About ..."] = "О программе ...",
		["User manual"] = "Руководство пользователя",
		["Export parameters"] = "Параметры экспорта",
		["Orientation"] = "Ориентация",
		["Zero X"] = "Нуль X",
		["Zero Y"] = "Нуль Y",
		["Units:"] = "Единицы:",
		["Shape:"] = "Фигура:",
		["Size:"] = "Размер:",
		["Tolerance:"] = "Допуск:",
		["Strength:"] = "Сила:",
		["Smooth:"] = "Сглаживание:",
		["Cursor"] = "Курсор",
		["Use page resolution"] = "Разрешение страницы",
		["Size lock"] = "Фиксация размера",
		["Status"] = "Состояние",

		#endregion

		#region Диалоги и сообщения

		["Open VDraw document"] = "Открыть документ VDraw",
		["Save VDraw document"] = "Сохранить документ VDraw",
		["Select image"] = "Выбрать изображение",
		["Copy to clipboard"] = "Копирование в буфер",
		["Image editing"] = "Правка изображения",
		["Select Image first."] = "Сначала выберите изображение.",
		["Could not load image."] = "Не удалось загрузить изображение.",

		["Save document"] = "Сохранение документа",
		["The document has unsaved changes.\r\nDo you want to save it before closing?"] =
			"В документе есть несохранённые изменения.\r\nСохранить его перед закрытием?",

		["Save error"] = "Ошибка сохранения",
		["Open error"] = "Ошибка открытия",
		["Export error"] = "Ошибка экспорта",

		["Failed to save document.\r\n\r\n{0}"] = "Не удалось сохранить документ.\r\n\r\n{0}",
		["Failed to open document.\r\n\r\n{0}"] = "Не удалось открыть документ.\r\n\r\n{0}",
		["Failed to import document.\r\n\r\n{0}\r\n{1}"] =
			"Не удалось импортировать документ.\r\n\r\n{0}\r\n{1}",
		["Failed to import PDF.\r\n\r\n{0}"] = "Не удалось импортировать PDF.\r\n\r\n{0}",
		["Failed to export selection.\r\n\r\n{0}"] = "Не удалось экспортировать выделенное.\r\n\r\n{0}",
		["Select at least one shape to export."] = "Выделите хотя бы одну фигуру для экспорта.",

		["User manual not found.\r\n\r\nExpected file:\r\n{0}"] =
			"Руководство пользователя не найдено.\r\n\r\nОжидался файл:\r\n{0}",
		["Could not open the user manual.\r\n\r\n{0}\r\n\r\n{1}"] =
			"Не удалось открыть руководство пользователя.\r\n\r\n{0}\r\n\r\n{1}",

		["About application"] = "О программе",
		["Version {0}"] = "Версия {0}",
		["Build date: {0}"] = "Дата сборки: {0}",
		["Author: {0}"] = "Автор: {0}",
		["Description:"] = "Описание:",
		["This computer program was created for 2D vector graphics, "
			+ "image editing and animation."] =
			"Программа создана для работы с двумерной векторной графикой, "
			+ "правки изображений и анимации.",
		["Any questions or suggestion for improvement can be sent "
			+ "to email address: {0}"] =
			"Вопросы и предложения по улучшению можно присылать "
			+ "на адрес: {0}",

		["Color picker"] = "Выбор цвета",
		["Add"] = "Добавить",
		["Create new document"] = "Создать документ",

		["VDraw could not start."] = "VDraw не смог запуститься.",
		["Details have been written to the file:"] = "Подробности записаны в файл:",

		#endregion

		#region Печать

		["Printer"] = "Принтер",
		["Paper:"] = "Бумага:",
		["Properties..."] = "Свойства...",
		["Page range"] = "Диапазон страниц",
		["All pages"] = "Все страницы",
		["Current page"] = "Текущая страница",
		["Pages:"] = "Страницы:",
		["Copies"] = "Копии",
		["Number of copies:"] = "Число копий:",
		["Collate"] = "Разбирать по копиям",
		["Layout"] = "Разметка",
		["Portrait"] = "Книжная",
		["Landscape"] = "Альбомная",
		["Scale:"] = "Масштаб:",
		["Fit to page"] = "Вписать в лист",
		["Actual size"] = "Реальный размер",
		["Centre on page"] = "По центру листа",
		["Print page background"] = "Печатать фон страницы",
		["Show printer margins"] = "Показывать поля принтера",

		// «Увеличение», а не «Масштаб»: масштабом здесь уже назван
		// пересчёт чертежа под лист, а это — увеличение предпросмотра.
		["Zoom:"] = "Увеличение:",
		["Fit"] = "Вписать",

		["Page {0} of {1}"] = "Страница {0} из {1}",
		["Print error"] = "Ошибка печати",
		["Failed to print.\r\n\r\n{0}"] = "Не удалось напечатать.\r\n\r\n{0}",
		["Nothing to print: the page range is empty."] =
			"Печатать нечего: диапазон страниц пуст.",

		["mm"] = "мм",

		#endregion

		#region Импорт PDF: выбор страниц

		["Import PDF — select pages"] = "Импорт PDF — выбор страниц",
		["Each page on its own sheet"] = "Каждую страницу на отдельный лист",
		["All"] = "Все",
		["Select at least one page."] = "Выберите хотя бы одну страницу.",
		["The page will be inserted into the current sheet."] =
			"Страница будет вставлена в текущий лист.",
		["Sheets to be created: {0}."] = "Будет создано листов: {0}.",
		["All {0} pages will be placed on the current sheet, one on top of another."] =
			"Все {0} страницы лягут в текущий лист друг поверх друга.",

		#endregion

		#region Фильтры файловых диалогов

		// Ключ — подпись целиком, вместе со списком расширений в скобках:
		// он виден пользователю, см. Localization.Filter.
		["All files (*.*)"] = "Все файлы (*.*)",
		["All Files"] = "Все файлы",
		["VDraw document (*.vdr)"] = "Документ VDraw (*.vdr)",
		["JSON files (*.json)"] = "Файлы JSON (*.json)",
		["PDF document (*.pdf)"] = "Документ PDF (*.pdf)",
		["VDraw shapes (*.vdrs)"] = "Фигуры VDraw (*.vdrs)",
		["Images"] = "Изображения",
		["Image Files"] = "Файлы изображений",
		["Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)"] =
			"Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)",
		["PNG image (*.png)"] = "Изображение PNG (*.png)",
		["JPEG image (*.jpg)"] = "Изображение JPEG (*.jpg)",
		["Bitmap (*.bmp)"] = "Растр BMP (*.bmp)",
		["SVG vector (*.svg)"] = "Вектор SVG (*.svg)",
		["Animated GIF (*.gif)"] = "Анимированный GIF (*.gif)",
		["Enhanced metafile (*.emf)"] = "Метафайл EMF (*.emf)",
		["Windows metafile (*.wmf)"] = "Метафайл WMF (*.wmf)",
		["Visio drawing or stencil (*.vsdx;*.vsdm;*.vss;*.vssx;*.vssm;*.vsx;*.vdx)"] =
			"Чертёж или клипарт Visio (*.vsdx;*.vsdm;*.vss;*.vssx;*.vssm;*.vsx;*.vdx)",
		["All supported (*.vdr;*.json;*.pdf;*.vdrs;*.vss;*.vssx;*.png;*.jpg;*.bmp)"] =
			"Все поддерживаемые (*.vdr;*.json;*.pdf;*.vdrs;*.vss;*.vssx;*.png;*.jpg;*.bmp)",

		#endregion

		#region Шаги отмены

		// Эти строки показываются в подсказках кнопок «отменить» и
		// «повторить»: «Отменить: Создать фигуру».
		["Undo: {0}"] = "Отменить: {0}",
		["Redo: {0}"] = "Повторить: {0}",

		["Create shape"] = "Создать фигуру",
		["Delete shape"] = "Удалить фигуру",
		["Delete shapes"] = "Удалить фигуры",
		["Select and move"] = "Выделить и переместить",
		["Change selection"] = "Изменить выделение",
		["Clear selection"] = "Снять выделение",
		["Add transform"] = "Добавить преобразование",
		["Boolean operation"] = "Логическая операция",
		["Line color"] = "Цвет линии",
		["Fill color"] = "Цвет заливки",
		["Text binding"] = "Привязка текста",
		["Transform binding"] = "Привязка преобразования",
		["Link anchor"] = "Привязать к фигуре",

		["Create guide"] = "Создать направляющую",
		["Delete guide"] = "Удалить направляющую",

		["Add page"] = "Добавить лист",
		["Delete page"] = "Удалить лист",
		["Rename page"] = "Переименовать лист",
		["Reorder pages"] = "Переставить листы",
		["Paper format"] = "Формат листа",
		["Page width"] = "Ширина листа",
		["Page height"] = "Высота листа",
		["Page orientation"] = "Ориентация листа",
		["Page units"] = "Единицы листа",
		["Page origin"] = "Начало координат листа",
		["Page grid"] = "Сетка листа",
		["Print borders"] = "Поля печати",

		["Change image selection"] = "Изменить выделение на изображении",
		["Delete selection"] = "Удалить выделенное",
		["Lift selection"] = "Поднять выделенное",
		["Image adjustments"] = "Правка изображения",

		["Set key"] = "Поставить ключ",
		["Set keys"] = "Поставить ключи",
		["Delete key"] = "Удалить ключ",
		["Delete keys"] = "Удалить ключи",
		["Move key"] = "Переместить ключ",
		["Move keys"] = "Переместить ключи",

		#endregion

		#region Каталоги: штрихи, глифы, форматы

		["Solid"] = "Сплошная",
		["Dash"] = "Штрих",
		["Dot"] = "Точка",
		["Dash Dot"] = "Штрих-точка",

		["None"] = "Нет",
		["Arrow"] = "Стрелка",
		["Triangle arrow"] = "Треугольная стрелка",
		["Engineer arrow"] = "Чертёжная стрелка",
		["Empty arrow"] = "Пустая стрелка",
		["Square"] = "Квадрат",
		["Square filled"] = "Квадрат с заливкой",
		["Diamond"] = "Ромб",
		["Diamond filled"] = "Ромб с заливкой",

		["Custom"] = "Свой",

		#endregion

		#region Палитра цветов

		// Подсказки на плитках палитры. Имена остаются английскими ключами
		// в самой таблице цветов, перевод берётся при показе подсказки.
		["Transparent"] = "Прозрачный",
		["Black"] = "Чёрный",
		["White"] = "Белый",
		["Red"] = "Красный",
		["Green"] = "Зелёный",
		["Blue"] = "Синий",

		["Gray 10%"] = "Серый 10%",
		["Gray 20%"] = "Серый 20%",
		["Gray 25%"] = "Серый 25%",
		["Gray 30%"] = "Серый 30%",
		["Gray 40%"] = "Серый 40%",
		["Gray 50%"] = "Серый 50%",
		["Gray 60%"] = "Серый 60%",
		["Gray 70%"] = "Серый 70%",
		["Gray 75%"] = "Серый 75%",
		["Gray 80%"] = "Серый 80%",
		["Gray 90%"] = "Серый 90%",

		["Pink"] = "Розовый",
		["Light coral"] = "Светло-коралловый",
		["Salmon"] = "Лососевый",
		["Coral"] = "Коралловый",
		["Crimson"] = "Малиновый",
		["Firebrick"] = "Кирпичный",
		["Dark red"] = "Тёмно-красный",

		["Peach"] = "Персиковый",
		["Sand"] = "Песочный",
		["Light orange"] = "Светло-оранжевый",
		["Orange"] = "Оранжевый",
		["Dark orange"] = "Тёмно-оранжевый",
		["Chocolate"] = "Шоколадный",
		["Brown"] = "Коричневый",

		["Cream"] = "Кремовый",
		["Light yellow"] = "Светло-жёлтый",
		["Yellow"] = "Жёлтый",
		["Gold"] = "Золотой",
		["Amber"] = "Янтарный",
		["Dark gold"] = "Тёмно-золотой",
		["Olive"] = "Оливковый",

		["Mint"] = "Мятный",
		["Light green"] = "Светло-зелёный",
		["Lime"] = "Лаймовый",
		["Grass"] = "Травяной",
		["Sea green"] = "Морской зелёный",
		["Forest green"] = "Лесной зелёный",
		["Dark green"] = "Тёмно-зелёный",

		["Ice"] = "Ледяной",
		["Aquamarine"] = "Аквамариновый",
		["Turquoise"] = "Бирюзовый",
		["Cyan"] = "Циан",
		["Light sea green"] = "Светлый морской зелёный",
		["Teal"] = "Бирюзово-зелёный",
		["Dark teal"] = "Тёмный бирюзово-зелёный",

		["Light blue"] = "Светло-синий",
		["Sky blue"] = "Небесно-голубой",
		["Cornflower"] = "Васильковый",
		["Steel blue"] = "Стальной синий",
		["Azure"] = "Лазурный",
		["Royal blue"] = "Королевский синий",
		["Navy"] = "Тёмно-синий",

		["Lavender"] = "Лавандовый",
		["Thistle"] = "Чертополоховый",
		["Orchid"] = "Орхидея",
		["Violet"] = "Фиолетовый",
		["Magenta"] = "Маджента",
		["Purple"] = "Пурпурный",
		["Indigo"] = "Индиго",

		#endregion

		#region Панель анимации

		["Frame"] = "Кадр",
		["Frames"] = "Кадров",
		["FPS"] = "Кадр/с",
		["Parameter"] = "Параметр",

		// Значение логического параметра в колонке дерева. С уточнением
		// контекста: «on» и «off» сами по себе слишком общие ключи.
		["anim|on"] = "есть",
		["anim|off"] = "нет",

		// Названия анимируемых параметров. Шаблоны с {0} — для подписей
		// с номером: узлов у кривой бывают сотни, и ключа на каждый
		// в словаре не держат. {0} в переводе обязателен, иначе шаблон
		// не применяется и остаётся английское название.
		["Scale X"] = "Масштаб X",
		["Scale Y"] = "Масштаб Y",
		["Skew X"] = "Наклон X",
		["Skew Y"] = "Наклон Y",
		["Visible"] = "Видимость",
		["Nodes"] = "Узлы",
		["Center"] = "Центр",
		["Point {0}"] = "Точка {0}",
		["Rotation, rad"] = "Поворот, рад",
		["Extension"] = "Вынос",
		["Text offset"] = "Смещение текста",
		["Node {0}"] = "Узел {0}",
		["Node {0} ctrl in"] = "Узел {0}, рычаг входа",
		["Node {0} ctrl out"] = "Узел {0}, рычаг выхода",

		["Pattern length"] = "Длина штриха",
		["Start glyph size"] = "Размер начального глифа",
		["End glyph size"] = "Размер конечного глифа",

		["Gradient color {0}"] = "Цвет градиента {0}",
		["Angle"] = "Угол",
		["Start point"] = "Начальная точка",
		["End point"] = "Конечная точка",
		["Pattern front"] = "Основной цвет узора",
		["Pattern back"] = "Фоновый цвет узора",
		["Gradient stops"] = "Точки градиента",
		["Stop {0} offset"] = "Точка {0}: положение",
		["Stop {0} color"] = "Точка {0}: цвет",

		["Shadow"] = "Тень",
		["Center offset"] = "Смещение центра",
		["Size vector"] = "Вектор размера",

		["Start opacity"] = "Начальная плотность",
		["End opacity"] = "Конечная плотность",
		["Start offset"] = "Начальное смещение",
		["End offset"] = "Конечное смещение",

		["Morthing"] = "Морфинг",
		["Bias"] = "Смещение",

		#endregion

		#region Панель свойств

		["No object selected"] = "Объект не выбран",
		["Fill intersection gaps"] = "Заполнять просветы на пересечениях",
		["Stop color"] = "Цвет точки",
		["Clear"] = "Очистить",
		["Load texture"] = "Загрузить текстуру",
		["Text inside shape"] = "Текст внутри фигуры",
		["Text along path"] = "Текст по контуру",
		["Tangent"] = "По касательной",
		["Reset"] = "Сбросить",

		#endregion
	};
}