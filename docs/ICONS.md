# VDraw — полный список иконок

Инвентаризация всех кнопок и пунктов меню приложения с указанием, откуда
берётся картинка сейчас, и предлагаемым именем SVG-файла.

Всего элементов интерфейса, которым нужна иконка: **138**
(115 в дизайнере формы + 7 в контекстном меню + 8 в выпадающих меню
split-кнопок + 7 кнопок рода заливки + 9 кнопок выравнивания текста
+ прочее).

---

## 1. Как иконки устроены сейчас

В приложении **три разных механизма** подачи картинки на кнопку.

| Механизм | Где задаётся | Как работает |
|---|---|---|
| **A. PNG-ресурс** | `MainForm.Designer.cs`: `X.Image = Properties.Resources.<имя>` | Файл лежит в `VDraw/Resources/<имя>.png`, подключён через `Properties/Resources.resx`. Для смены темы регистрируется в `MainForm.ThemeImages.cs` вызовом `ThemeImages.Add(control, "<имя>", ImageSlot.Image)` — тогда при тёмной теме подхватывается `<имя>_dark.png`, если он есть. |
| **B. Глиф иконочного шрифта** | `MainForm.cs`, метод `SetIcons()`: `X.Text = "\uXXXX"`, шрифт `MainForm.fSnipit` | Шрифт `Resources/FluentSystemIcons-Resizable.ttf`. Кнопка рисует символ, а не картинку — поэтому автоматически красится в цвет темы. Таких кнопок **45**. |
| **C. PNG по имени файла** | `SkPropertyPanelControl.cs`: `LoadUiBitmap("<имя>.png")` | Загрузка с диска в SkiaSharp. Используется только для 7 кнопок рода заливки. |

Текущее распределение по дизайнеру формы (115 элементов):

- **45** — глифы шрифта (механизм B);
- **41** — PNG-ресурсы (механизм A);
- **29** — **без иконки вообще** (пункты меню File/Help, цветовые
  предпросмотры, галочки, кнопка переключения языка).

### Что предлагаю для SVG

1. Папка `VDraw/Resources/icons/` с плоским набором `*.svg`, имена —
   `kebab-case`, как в колонке «SVG» ниже.
2. Размерная сетка **24×24** для меню и **32×32** (или тот же 24×24 с
   полем) для кнопок тулбаров — сейчас кнопки 48×48, картинка внутри
   масштабируется.
3. `stroke="currentColor"` / `fill="currentColor"` вместо жёстких цветов —
   тогда одна иконка будет работать и в светлой, и в тёмной теме, и не
   нужны будут парные файлы `*_dark`. Сейчас такие пары есть только для
   `lines` и `curve_edit`.
4. Отдельный файл `*-dark.svg` только там, где одного `currentColor`
   недостаточно (многоцветные иконки булевых операций, например).

---

## 2. Главное меню

### 2.1 Корневые пункты (`menuStrip1`)

| Элемент | Надпись | Сейчас | SVG |
|---|---|---|---|
| `menu_file` | File | — | `menu-file.svg` |
| `menu_objects` | Objects | — | `menu-objects.svg` |
| `menu_page` | Page | — | `menu-page.svg` |
| `menu_document` | Document | — | `menu-document.svg` |
| `helpToolStripMenuItem` | Help | — | `menu-help.svg` |

`menu_page` и `menu_document` — это не выпадающие меню, а кнопки-тумблеры:
они показывают/скрывают соответствующий тулбар.

### 2.2 File (`menu_file`)

| Элемент | Надпись | Сейчас | SVG |
|---|---|---|---|
| `menu_file_new` | New | — | `file-new.svg` |
| `menu_file_open` | Open | — | `file-open.svg` |
| `menu_file_import` | Import | — | `file-import.svg` |
| `menu_file_save` | Save | — | `file-save.svg` |
| `menu_file_save_as` | Save as | — | `file-save-as.svg` |
| `menu_file_export` | Export | — | `file-export.svg` |
| `menu_file_print` | Print | — | `file-print.svg` |
| `menu_file_close` | Close | — | `file-close.svg` |
| `menu_settings` | Settings | — | `settings.svg` |
| `menu_file_exit` | Exit | — | `exit.svg` |

Ни один пункт меню File иконки сейчас не имеет. Логично переиспользовать
те же SVG, что и на тулбаре документа (п. 3) — иконки совпадают по смыслу.

### 2.3 Objects (`menu_objects`)

| Элемент | Надпись | Сейчас (PNG-ресурс) | SVG |
|---|---|---|---|
| `menu_obj_lines` | Lines | `lines` (+ `lines_dark`) | `shape-lines.svg` |
| `menu_obj_curve` | Curve | `curve` | `shape-curve.svg` |
| `menu_obj_rect` | Rectangle | `rect` | `shape-rect.svg` |
| `menu_obj_rounded_rect` | Rounded Rectangle | `rounded_rect` | `shape-rect-rounded.svg` |
| `menu_obj_circle2p` | Circle 2-points | `circle2p` | `shape-circle-2p.svg` |
| `menu_obj_circle3p` | Circle 3-points | `circle3p` | `shape-circle-3p.svg` |
| `menu_obj_ellispe3p` | Ellipse 3-points | `ellipse3p` | `shape-ellipse-3p.svg` |
| `menu_obj_ellipse5p` | Ellipse 5-points | `ellipse5p` | `shape-ellipse-5p.svg` |
| `menu_obj_poly` | Polygon | `poly` | `shape-poly.svg` |
| `menu_obj_text` | Text | `text` (через `MainForm.resx`) | `shape-text.svg` |
| `menu_obj_image` | Image | `image` | `shape-image.svg` |
| `menu_obj_measurement_distance` | Measure distance | `measure_dist` | `measure-distance.svg` |
| `measureAngleToolStripMenuItem` | Measure angle | `measure_angle` | `measure-angle.svg` |
| `customShapesToolStripMenuItem` | Custom shapes | `custom_shapes` | `custom-shapes.svg` |
| `convertToCurveToolStripMenuItem` | Convert to curve | `convert_to_curve` | `convert-to-curve.svg` |
| `convertToBitmapToolStripMenuItem` | Convert to bitmap | **нет** | `convert-to-bitmap.svg` |

`menu_obj_text` — единственный элемент, чья картинка подключена не через
`Properties.Resources`, а напрямую через `MainForm.resx`. При переходе на
SVG это стоит унифицировать.

### 2.4 Custom shapes (подменю `customShapesToolStripMenuItem`)

| Элемент | Надпись | Сейчас | SVG |
|---|---|---|---|
| `menu_obj_add_to_custom_shapes` | Add selection to custom shape | — | `custom-shape-add.svg` |
| `menu_obj_import_vdrs` | Import custom shapes (*.vdrs) | — | `custom-shape-import.svg` |
| `menu_obj_import_visio` | Import Visio file (*.vsdx, *.vss, *.vssx) | — | `import-visio.svg` |

### 2.5 Help (`helpToolStripMenuItem`)

| Элемент | Надпись | Сейчас | SVG |
|---|---|---|---|
| `menu_manual` | User manual | — | `manual.svg` |
| `menu_about` | About ... | — | `about.svg` |

---

## 3. Контекстное меню фигуры

Строится в коде, файл **`MainForm.ShapeContextMenu.cs`**. Иконок нет ни у
одного пункта — везде передаётся `null`:
`new ToolStripMenuItem(L.T("..."), null, handler)`.

| Строка | Надпись | Сейчас | SVG |
|---|---|---|---|
| 60 | Copy | — | `edit-copy.svg` |
| 65 | Paste | — | `edit-paste.svg` |
| 70 | Convert to curves | — | `convert-to-curve.svg` |
| 78 | Convert to bitmap | — | `convert-to-bitmap.svg` |
| 86 | Group selected | — | `group.svg` |
| 95 | Ungroup selected | — | `ungroup.svg` |
| 103 | Delete | — | `delete.svg` |

Все семь иконок уже будут нарисованы для тулбаров — здесь нужно просто
подставить те же файлы.

---

## 4. Выпадающие меню split-кнопок

Строятся в коде в `MainForm.cs` (`InitCircleSplitButton`,
`InitEllipseSplitButton`, `InitRectangleSplitButton`). Иконка выбранного
пункта переносится на главную кнопку (`SelectedImage`), поэтому эти
картинки работают и как иконки тулбара.

| Меню | Пункт | Сейчас | SVG |
|---|---|---|---|
| `s_new_circle_but` | Circle 2P | `circle2p` | `shape-circle-2p.svg` |
| | Circle 3P | `circle3p` | `shape-circle-3p.svg` |
| `s_new_ellipse_but` | Ellipse 3P | `ellipse3p` | `shape-ellipse-3p.svg` |
| | Ellipse 5P | `ellipse5p` | `shape-ellipse-5p.svg` |
| `s_new_rc_but` | Rectangle | `rect` | `shape-rect.svg` |
| | Rounded Rectangle | `rounded_rect` | `shape-rect-rounded.svg` |
| | Chamfered Rectangle | `chamfered_rect` | `shape-rect-chamfered.svg` |

Меню рисуются с `ImageScalingSize = 48×48` — для SVG это уже не важно,
но при растеризации нужен размер не меньше 48.

---

## 5. Тулбар документа (`doc_toolbar_flow`)

Все семь кнопок — глифы шрифта.

| Элемент | Подсказка | Глиф | SVG |
|---|---|---|---|
| `d_new_but` | New document (Ctrl+N) | `U+E655` | `file-new.svg` |
| `d_open_but` | Open document (Ctrl+O) | `U+E859` | `file-open.svg` |
| `d_import_but` | Import (Ctrl+I) | `U+E0CF` | `file-import.svg` |
| `d_save_but` | Save (Ctrl+S) | `U+EE5F` | `file-save.svg` |
| `d_save_as_but` | Save as... (Ctrl+Shift+S) | `U+EE65` | `file-save-as.svg` |
| `d_export_but` | Export selection (Ctrl+E) | `U+E0D5` | `file-export.svg` |
| `d_print_but` | Print (Ctrl+P) | `U+E6EF` | `file-print.svg` |

---

## 6. Основной тулбар

### 6.1 Правка (`main_tools_flow`)

| Элемент | Подсказка | Глиф | SVG |
|---|---|---|---|
| `undo_but` | Undo (Ctrl+Z) | `U+E0F7` | `undo.svg` |
| `redo_but` | Redo (Ctrl+Y) | `U+E0F9` | `redo.svg` |
| `copy_but` | Copy (Ctrl+C) | `U+ED8E` | `edit-copy.svg` |
| `paste_but` | Paste (Ctrl+V) | `U+E490` | `edit-paste.svg` |
| `delete_but` | Delete selected (Del) | `U+E63E` | `delete.svg` |
| `format_but` | (копирование формата) | `U+EC02` | `format-painter.svg` |

У `format_but` подсказки нет — её стоит добавить («Format painter»).

### 6.2 Прочее (`main_toolbar_panel`)

| Элемент | Подсказка | Сейчас | SVG |
|---|---|---|---|
| `help_but` | Help (F1) | глиф `U+E22C` | `help.svg` |
| `language_switch_but` | — (надпись «EN»/«RU») | **текст, не иконка** | `language.svg` (опц.) |

`language_switch_but` показывает текстом текущий язык. Если хочется
иконку — нужен либо флаг, либо глобус плюс подпись.

---

## 7. Тулбар вида (`view_flow`)

| Элемент | Подсказка | Сейчас | SVG |
|---|---|---|---|
| `v_show_rulers_but` | Rulers (Ctrl+R) | PNG `rulers` | `view-rulers.svg` |
| `v_show_guidelines_but` | Guides (Ctrl+;) | PNG `guidelines` | `view-guides.svg` |
| `v_snap_but` | Snap to objects (Ctrl+Shift+N) | PNG `snapping` | `snap-objects.svg` |
| `v_dynamic_guidelines_but` | Dynamic guides | PNG `dynamic_guidelines` | `snap-dynamic-guides.svg` |
| `v_scale_page_but` | Fit page (Ctrl+1) | глиф `U+E701` | `zoom-fit-page.svg` |
| `v_scale_all_but` | Zoom to all shapes (Ctrl+0) | глиф `U+EE9D` | `zoom-fit-all.svg` |
| `v_scale_selected_but` | Zoom to selection (Ctrl+Shift+0) | глиф `U+EEA7` | `zoom-fit-selection.svg` |

Первые четыре — кнопки-тумблеры. Им нужен вариант «включено»: либо
отдельный SVG, либо достаточно фона нажатого состояния (сейчас так и
работает — через `WinFormsTheme.ButtonChecked`).

---

## 8. Тулбар страницы

| Группа | Элемент | Подсказка | Сейчас | SVG |
|---|---|---|---|---|
| `page_paper_flow` | `page_props_but` | Page properties | глиф `U+E703` | `page-properties.svg` |
| `page_layout_flow` | `page_vertical_but` | Portrait orientation | глиф `U+E655` | `page-portrait.svg` |
| `page_layout_flow` | `page_horizontal_but` | Landscape orientation | глиф `U+E6AF` | `page-landscape.svg` |
| `page_layout_flow` | `page_background_color_but` | Page background colour | — (предпросмотр цвета) | — |
| `page_lock_ratio_flow` | `page_lock_ratio_but` | Lock aspect ratio | глиф `U+EAB6` | `lock-ratio.svg` |
| `page_view_flow` | `page_grid_but` | Grid (Ctrl+#) | глиф `U+E244` | `page-grid.svg` |
| `page_view_flow` | `page_print_borders_but` | Printable area borders | глиф `U+E68F` | `page-print-borders.svg` |

`page_background_color_but` — это `ColorPreviewButton`: он сам рисует
залитый прямоугольник поверх шахматного фона `transparentbg_mini.png`.
Иконка не нужна, но **шахматный фон прозрачности стоит сделать SVG** —
`transparent-checker.svg`, он используется в 5 местах.

---

## 9. Тулбар инструментов рисования (`drawing_toolbar_panel`)

| Элемент | Подсказка | Сейчас | SVG |
|---|---|---|---|
| `s_cursor_but` | Select (Esc) | глиф `U+E597` | `tool-select.svg` |
| `s_curve_edit_but` | Edit nodes (N) | PNG `curve_edit` (+ `curve_edit_dark`) | `tool-edit-nodes.svg` |
| `s_new_line_but` | Straight lines (L) | PNG `lines` (+ `lines_dark`) | `shape-lines.svg` |
| `s_new_curve_but` | Curve (B) | PNG `curve` | `shape-curve.svg` |
| `s_new_rc_but` | Rectangle (split) | PNG `rect` / `rounded_rect` / `chamfered_rect` | см. п. 4 |
| `s_new_circle_but` | Circle (split) | PNG `circle2p` / `circle3p` | см. п. 4 |
| `s_new_ellipse_but` | Ellipse (split) | PNG `ellipse3p` / `ellipse5p` | см. п. 4 |
| `s_new_poly_but` | Regular polygon (P) | PNG `poly` | `shape-poly.svg` |
| `s_new_free_poly_but` | Free polygon (Shift+P) | PNG `free_poly` | `shape-poly-free.svg` |
| `s_new_text_but` | Text (T) | глиф `U+F190` | `shape-text.svg` |
| `s_new_image_but` | Image (Ctrl+Shift+I) | глиф `U+E97C` | `shape-image.svg` |
| `measure_distance_but` | Measure distance (M) | PNG `measure_dist` | `measure-distance.svg` |
| `measure_angle_but` | — | PNG `measure_angle` | `measure-angle.svg` |
| `image_mode_but` | Image editing mode (Ctrl+M) | глиф `U+E98E` | `mode-image.svg` |
| `s_morth_but` | — | глиф `U+F0E9` | `tool-morph.svg` |

У `measure_angle_but` и `s_morth_but` подсказок нет — стоит добавить.

Три split-кнопки (`s_new_rc_but`, `s_new_circle_but`, `s_new_ellipse_but`)
дополнительно рисуют треугольник раскрытия меню — он отрисовывается кодом,
отдельная иконка не нужна (но можно сделать `dropdown-arrow.svg`).

---

## 10. Группы тулбара фигур

### 10.1 Порядок и группировка (`s_layout_flow`)

| Элемент | Подсказка | Глиф | SVG |
|---|---|---|---|
| `s_bring_to_front_but` | Bring to front (Ctrl+Shift+Up) | `U+ED94` | `order-bring-front.svg` |
| `s_send_back_but` | Send to back (Ctrl+Shift+Down) | `U+ED92` | `order-send-back.svg` |
| `s_group_but` | Group (Ctrl+G) | `U+E8FB` | `group.svg` |
| `s_ungroup_but` | Ungroup (Ctrl+Shift+G) | `U+E8FD` | `ungroup.svg` |

### 10.2 Отражение и поворот (`flip_flow`)

| Элемент | Подсказка | Глиф | SVG |
|---|---|---|---|
| `flip_horizontal_but` | Flip horizontally (Shift+H) | `U+E833` | `flip-horizontal.svg` |
| `flip_vertical_but` | Flip vertically (Shift+V) | `U+E835` | `flip-vertical.svg` |
| `rotate_CW_90_but` | Rotate 90 clockwise (Ctrl+Right) | `U+E129` | `rotate-cw.svg` |
| `rotate_CCW_90_but` | Rotate 90 counter-clockwise (Ctrl+Left) | `U+E12B` | `rotate-ccw.svg` |

### 10.3 Булевы операции (`s_boolean_tools_flow`)

Все шесть — PNG-ресурсы.

| Элемент | Подсказка | PNG | SVG |
|---|---|---|---|
| `s_convert_to_curve_but` | Convert to curve (Ctrl+Q) | `convert_to_curve` | `convert-to-curve.svg` |
| `s_union_but` | Union (Ctrl+Alt+U) | `union_curve` | `bool-union.svg` |
| `s_substract_but` | Subtract (Ctrl+Alt+S) | `substract_curve` | `bool-subtract.svg` |
| `s_intersect_but` | Intersect (Ctrl+Alt+I) | `intersect_curve` | `bool-intersect.svg` |
| `s_xor_but` | Exclude (Ctrl+Alt+X) | `xor_curve` | `bool-exclude.svg` |
| `s_combine_but` | Combine (Ctrl+Alt+C) | `Combine_curve` | `bool-combine.svg` |

Обратите внимание: имя ресурса `Combine_curve` — с заглавной, в отличие от
остальных. При переходе на SVG стоит привести к единому стилю.

### 10.4 Разрезание (`s_scissors_flow`)

| Элемент | Подсказка | PNG | SVG |
|---|---|---|---|
| `s_scissors_line_but` | Cut with a straight line (K) | `scissors_line` | `cut-line.svg` |
| `s_scissors_curve_but` | Cut with a curve (Shift+K) | `scissors_curve1` | `cut-curve.svg` |
| `s_cap_scissors_but` | Close the cut edges | `scissors_cap` | `cut-cap.svg` |

**Мелочь на заметку:** в `Resources.resx` есть по две записи на один
файл — `scissors_curve` и `scissors_curve1` обе указывают на
`scissors_curve.png`, то же с `scissors_line` / `scissors_line1`. Код
использует вариант с единицей. При переходе на SVG дубли стоит убрать.

### 10.5 Цвета по умолчанию (`s_colors_flow`)

| Элемент | Подсказка | Сейчас | SVG |
|---|---|---|---|
| `s_line_color_but` | Default outline colour | `ColorPreviewButton` + `transparentbg_mini` | `transparent-checker.svg` |
| `s_fill_color_but` | Default fill colour | то же | то же |

---

## 11. Узлы кривой (`c_nodes_tools_flow`)

Все шесть — PNG-ресурсы.

| Элемент | Подсказка | PNG | SVG |
|---|---|---|---|
| `nodes_straight_but` | Straight node (1) | `Nodes_straight` | `node-straight.svg` |
| `node_tangent_but` | Smooth node (2) | `Node_tangent` | `node-tangent.svg` |
| `node_sharp_but` | Cusp node (3) | `Node_sharp` | `node-sharp.svg` |
| `nodes_merge_but` | Merge nodes (Ctrl+J) | `Nodes_merge` | `node-merge.svg` |
| `nodes_split_but` | Split node (Ctrl+Shift+J) | `Nodes_split` | `node-split.svg` |
| `node_delete_but` | Delete nodes (Del in node mode) | `X` | `node-delete.svg` |

В папке есть неиспользуемый `node_new.png` — видимо, задумывалась кнопка
вставки узла (сейчас узел вставляется двойным щелчком по сегменту).
Если кнопку добавите — `node-insert.svg`.

---

## 12. Тулбар растрового редактора

### 12.1 Инструменты (`image_tools_flow`)

Все девять — глифы.

| Элемент | Подсказка | Глиф | SVG |
|---|---|---|---|
| `i_brush_but` | Brush (B in image mode) | `U+E45E` | `img-brush.svg` |
| `i_erasor_but` | Eraser (E in image mode) | `U+E7CD` | `img-eraser.svg` |
| `i_fill_but` | Fill (G in image mode) | `U+E4FC` | `img-fill.svg` |
| `i_select_rect_but` | Rectangular selection (R in image mode) | `U+E252` | `img-select-rect.svg` |
| `i_select_lasso_but` | Lasso selection (L in image mode) | `U+E9F3` | `img-select-lasso.svg` |
| `i_light_but` | Lighten | `U+E2A8` | `img-lighten.svg` |
| `i_dark_but` | Darken | `U+E2A7` | `img-darken.svg` |
| `i_contrast_but` | Contrast | `U+E014` | `img-contrast.svg` |
| `i_blackwhite_but` | Black and white | `U+E43C` | `img-blackwhite.svg` |

### 12.2 Цвета и флажки

| Элемент | Подсказка | Сейчас | SVG |
|---|---|---|---|
| `i_fore_color_but` | Foreground colour | `ColorPreviewButton` + `transparentbg_mini` | `transparent-checker.svg` |
| `i_back_color_but` | Background colour | то же | то же |
| `i_size_lock_check` | Size lock | `CheckBox`, без иконки | `lock-ratio.svg` (опц.) |
| `i_actual_resolution_check` | Use page resolution | `CheckBox`, без иконки | — |

---

## 13. Панель свойств (Skia-интерфейс)

### 13.1 Род заливки — `SkPropertyPanelControl.cs`, строки 6128–6134

Единственное место, где PNG загружаются по имени файла через
`LoadUiBitmap()`. Кнопки `SkPictureButton`, 36×36.

| Файл сейчас | Подсказка | SVG |
|---|---|---|
| `fill_none.png` | No fill | `fill-none.svg` |
| `fill_uniform.png` | Solid fill | `fill-solid.svg` |
| `fountain_fill_linear.png` | Linear gradient | `fill-gradient-linear.svg` |
| `fountain_fill_radial.png` | Radial gradient | `fill-gradient-radial.svg` |
| `fountain_fill_square.png` | Rectangular gradient | `fill-gradient-rect.svg` |
| `fill_pattern.png` | Pattern fill | `fill-pattern.svg` |
| `fill_texture.png` | Texture fill | `fill-texture.svg` |

В папке есть неиспользуемый `fill_steps.png` — заливка ступенями. Если
она понадобится: `fill-steps.svg`.

### 13.2 Выравнивание текста в области — строки 4230–4234

Сетка 3×3, кнопки `SkButton` 30×28 с глифами шрифта. Индекс =
`(int)VAlign * 3 + (int)HAlign`.

| Позиция | Глиф | SVG |
|---|---|---|
| верх-лево | `U+F29B` | `align-top-left.svg` |
| верх-центр | `U+F299` | `align-top-center.svg` |
| верх-право | `U+F29D` | `align-top-right.svg` |
| середина-лево | `U+F291` | `align-middle-left.svg` |
| середина-центр | `U+F28D` | `align-middle-center.svg` |
| середина-право | `U+F293` | `align-middle-right.svg` |
| низ-лево | `U+F287` | `align-bottom-left.svg` |
| низ-центр | `U+F285` | `align-bottom-center.svg` |
| низ-право | `U+F289` | `align-bottom-right.svg` |

### 13.3 Начертание текста — строки 4186–4189

Сейчас буквы, не иконки. Можно оставить буквами — так принято — либо
нарисовать:

| Элемент | Надпись | SVG |
|---|---|---|
| `boldBut` | B | `text-bold.svg` |
| `italicBut` | I | `text-italic.svg` |
| `underlineBut` | U | `text-underline.svg` |
| `strikeBut` | S | `text-strikethrough.svg` |

### 13.4 Кнопки с текстовой надписью (иконки не обязательны)

`addPatternBut` «Add pattern», `deletePatternBut` «Delete pattern»,
`save` «Save», `clear` «Clear», `load` «Load texture», `applyBut`,
`resetBut`, `containerBut` «Select», `shapeAlongBut` «Select».

Если захочется компактнее — `pattern-add.svg`, `pattern-delete.svg`,
`texture-load.svg`, `pick-shape.svg`.

### 13.5 Сворачивание групп свойств

`SkCollapsePanel` рисует треугольник раскрытия кодом. Если нужен SVG —
`chevron-down.svg` / `chevron-right.svg`.

---

## 14. Панель анимации (`AnimationPanel.cs`)

Класс `AnimIconButton`, глифы заданы прямо в объявлениях полей
(строки 370–428). Перечисление `AnimIcon` задаёт запасную отрисовку
кодом, если глиф не задан.

| Элемент | Назначение | Глиф | SVG |
|---|---|---|---|
| `a_to_start_but` | В начало | `U+EDB0` | `anim-to-start.svg` |
| `a_step_left_but` | Кадр назад | `U+EDB2` | `anim-step-back.svg` |
| `a_play_but` | Воспроизведение | `U+F2E8` | `anim-play.svg` |
| (то же, состояние «стоп») | Остановить | `U+EFC4` | `anim-stop.svg` |
| `a_step_right_but` | Кадр вперёд | `U+EBA1` | `anim-step-forward.svg` |
| `a_to_end_but` | В конец | `U+EB9F` | `anim-to-end.svg` |
| `a_reverse_but` | Обратное воспроизведение | `U+EE2B` | `anim-reverse.svg` |
| `a_set_key` | Set key (надпись + значок) | — (`AnimIcon.Key`, рисуется кодом) | `anim-key.svg` |
| `a_zoom_but` | Масштаб шкалы | — (`AnimIcon.Zoom`) | `anim-zoom.svg` |
| `a_collapse_but` | Свернуть панель | — (`AnimIcon.ChevronUp`) | `chevron-down.svg` |

---

## 15. Прочее

| Где | Элемент | Сейчас | SVG |
|---|---|---|---|
| `SColorDialog.cs:127` | `sample_but` — пипетка | глиф `U+E7EA` | `eyedropper.svg` |
| `PageTabsControl.cs:287` | кнопка «+» — добавить страницу | текст `"+"` | `page-add.svg` |
| везде | шахматный фон прозрачности | `transparentbg_mini.png` | `transparent-checker.svg` |
| `SkControls.cs:1555` | `transparentbg_mini-2.jpg` (с диска) | JPG | то же |
| окно / ярлык | иконка приложения | `vdraw.ico`, `vdraw_256.png` | `app-icon.svg` |
| `SColorDialog` | круговой градиент цветового круга | `CircularHueGradient.png` | оставить растром |

---

## 16. Сводка по файлам в `VDraw/Resources`

### Используются (на кнопках и в меню)

`chamfered_rect`, `circle2p`, `circle3p`, `Combine_curve`,
`convert_to_curve`, `curve`, `curve_edit`, `curve_edit_dark`,
`custom_shapes`, `dynamic_guidelines`, `ellipse3p`, `ellipse5p`,
`fill_none`, `fill_pattern`, `fill_texture`, `fill_uniform`,
`fountain_fill_linear`, `fountain_fill_radial`, `fountain_fill_square`,
`free_poly`, `guidelines`, `image`, `intersect_curve`, `lines`,
`lines_dark`, `measure_angle`, `measure_dist`, `Node_sharp`,
`Node_tangent`, `Nodes_merge`, `Nodes_split`, `Nodes_straight`, `poly`,
`rect`, `rounded_rect`, `rulers`, `scissors_cap`, `scissors_curve`,
`scissors_line`, `snapping`, `substract_curve`, `text`,
`transparentbg_mini`, `union_curve`, `X`, `xor_curve`.

### Не используются на кнопках

| Файл | Примечание |
|---|---|
| `arrow_up`, `arrow_down`, `arrow_left`, `arrow_right` | нет ни в коде, ни в `Resources.resx` — остатки |
| `fill_steps` | заливка ступенями не подключена; в `.resx` не зарегистрирован |
| `link` | нет ни в коде, ни в `.resx` |
| `node_new` | кнопка вставки узла не сделана; в `.resx` не зарегистрирован |
| `CircularHueGradient` | цветовой круг — растр по природе |
| `texture.jpg` | образец текстуры, не иконка |
| `vdraw.ico`, `vdraw_256.png` | иконка приложения |
| `FluentSystemIcons-*.ttf` | иконочный шрифт |
| `Brushes/` | кисти растрового редактора |
| `VDraw_User_Manual_v1.pdf` | документация |

---

## 17. Что нужно нарисовать — минимальный набор

Если собрать по уникальным именам, без повторов, то SVG-файлов нужно
**103**. Из них:

- **46** заменяют существующие PNG;
- **45** заменяют глифы шрифта (после чего `FluentSystemIcons-*.ttf`
  можно убрать из ресурсов — минус ~700 КБ);
- **12** — новые, которых сейчас нет вообще (пункты меню File и Help,
  контекстное меню, `convert-to-bitmap`, `page-add`, `language`).

Приоритет, если делать по частям:

1. **Контекстное меню** (7 иконок) — там нет ничего, и это самое заметное.
2. **Меню File и Help** (12 иконок) — тоже пусто.
3. **Замена глифов шрифта** (45) — снимает зависимость от стороннего
   шрифта и даёт полный контроль над видом.
4. **Замена PNG** (46) — чисто качество: векторные иконки не мылятся на
   мониторах с масштабом 125–200 %.
