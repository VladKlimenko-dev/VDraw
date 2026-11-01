namespace VDraw;

public partial class MainForm
{
	/// <summary>
	/// Таблица значков, повторяющая присваивания дизайнера.
	///
	/// Живёт в MainForm, потому что перечисленные поля закрыты внутри неё:
	/// внешний класс до них не дотянется, а делать их открытыми ради
	/// одной таблицы значило бы открыть наружу всю панель инструментов.
	///
	/// Вызовы записаны с именем класса: Add — статический метод
	/// ThemeImages, а не член формы, и без квалификации компилятор
	/// ищет его среди членов MainForm и не находит.
	/// </summary>
	internal void RegisterThemeImages()
	{
		ThemeImages.Add(menu_obj_lines, "lines", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_curve, "curve", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_rect, "rect", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_rounded_rect, "rounded_rect", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_circle2p, "circle2p", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_circle3p, "circle3p", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_ellispe3p, "ellipse3p", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_ellipse5p, "ellipse5p", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_poly, "poly", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_measurement_distance, "measure_dist", ImageSlot.Foreground);
		ThemeImages.Add(measureAngleToolStripMenuItem, "measure_angle", ImageSlot.Foreground);
		ThemeImages.Add(convertToCurveToolStripMenuItem, "convert_to_curve", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_text, "text", ImageSlot.Foreground);
		ThemeImages.Add(menu_obj_image, "image", ImageSlot.Foreground);
		ThemeImages.Add(customShapesToolStripMenuItem, "custom_shapes", ImageSlot.Foreground);
		ThemeImages.Add(page_background_color_but, "transparentbg_mini", ImageSlot.Background);
		ThemeImages.Add(s_scissors_line_but, "scissors_line1", ImageSlot.Foreground);
		ThemeImages.Add(s_scissors_curve_but, "scissors_curve1", ImageSlot.Foreground);
		ThemeImages.Add(s_cap_scissors_but, "scissors_cap", ImageSlot.Foreground);
		ThemeImages.Add(s_line_color_but, "transparentbg_mini", ImageSlot.Background);
		ThemeImages.Add(s_fill_color_but, "transparentbg_mini", ImageSlot.Background);
		ThemeImages.Add(s_convert_to_curve_but, "convert_to_curve", ImageSlot.Foreground);
		ThemeImages.Add(s_combine_but, "Combine_curve", ImageSlot.Foreground);
		ThemeImages.Add(s_union_but, "union_curve", ImageSlot.Foreground);
		ThemeImages.Add(s_substract_but, "substract_curve", ImageSlot.Foreground);
		ThemeImages.Add(s_intersect_but, "intersect_curve", ImageSlot.Foreground);
		ThemeImages.Add(s_xor_but, "xor_curve", ImageSlot.Foreground);
		ThemeImages.Add(i_fore_color_but, "transparentbg_mini", ImageSlot.Background);
		ThemeImages.Add(i_back_color_but, "transparentbg_mini", ImageSlot.Background);
		ThemeImages.Add(nodes_merge_but, "Nodes_merge", ImageSlot.Foreground);
		ThemeImages.Add(nodes_split_but, "Nodes_split", ImageSlot.Foreground);
		ThemeImages.Add(nodes_straight_but, "Nodes_straight", ImageSlot.Foreground);
		ThemeImages.Add(node_tangent_but, "Node_tangent", ImageSlot.Foreground);
		ThemeImages.Add(node_sharp_but, "Node_sharp", ImageSlot.Foreground);
		ThemeImages.Add(node_delete_but, "X", ImageSlot.Foreground);
		ThemeImages.Add(v_show_rulers_but, "rulers", ImageSlot.Foreground);
		ThemeImages.Add(v_show_guidelines_but, "guidelines", ImageSlot.Foreground);
		ThemeImages.Add(v_snap_but, "snapping", ImageSlot.Foreground);
		ThemeImages.Add(v_dynamic_guidelines_but, "dynamic_guidelines", ImageSlot.Foreground);
		ThemeImages.Add(measure_distance_but, "measure_dist", ImageSlot.Foreground);
		ThemeImages.Add(s_curve_edit_but, "curve_edit", ImageSlot.Foreground);
		ThemeImages.Add(s_new_line_but, "lines", ImageSlot.Foreground);
		ThemeImages.Add(s_new_curve_but, "curve", ImageSlot.Foreground);
		ThemeImages.Add(s_new_poly_but, "poly", ImageSlot.Foreground);
		ThemeImages.Add(s_new_free_poly_but, "free_poly", ImageSlot.Foreground);
		ThemeImages.Add(measure_angle_but, "measure_angle", ImageSlot.Foreground);

		RegisterGlyphButtonIcons();
		RegisterMenuIcons();
	}

	/// <summary>
	/// Кнопки, которые раньше показывали символ иконочного шрифта.
	///
	/// Им передаётся clearText: подпись с глифом снимается в тот момент,
	/// когда для кнопки нашёлся файл значка. Пока файла нет, остаётся
	/// глиф — набор можно переводить на SVG по частям, и приложение
    /// в промежуточном состоянии выглядит разнородно, но работает.
	///
	/// Имена здесь — имена файлов в Resources\Icons, без таблицы
	/// соответствий: таблица нужна была только старым именам PNG.
	/// </summary>
	private void RegisterGlyphButtonIcons()
	{
		void Icon(Control control, string name)
			=> ThemeImages.Add(control, name, ImageSlot.Foreground, 0, clearText: true);

		// Тулбар документа
		Icon(d_new_but, "file-new");
		Icon(d_open_but, "file-open");
		Icon(d_import_but, "file-import");
		Icon(d_save_but, "file-save");
		Icon(d_save_as_but, "file-save-as");
		Icon(d_export_but, "file-export");
		Icon(d_print_but, "file-print");

		// Правка
		Icon(undo_but, "undo");
		Icon(redo_but, "redo");
		Icon(copy_but, "edit-copy");
		Icon(paste_but, "edit-paste");
		Icon(delete_but, "delete");
		Icon(format_but, "format-painter");
		Icon(help_but, "help");

		// Вид
		Icon(v_scale_page_but, "zoom-fit-page");
		Icon(v_scale_all_but, "zoom-fit-all");
		Icon(v_scale_selected_but, "zoom-fit-selection");

		// Страница
		Icon(page_props_but, "page-properties");
		Icon(page_vertical_but, "page-portrait");
		Icon(page_horizontal_but, "page-landscape");
		Icon(page_lock_ratio_but, "lock-ratio");
		Icon(page_grid_but, "page-grid");
		Icon(page_print_borders_but, "page-print-borders");

		// Инструменты рисования
		Icon(s_cursor_but, "tool-select");
		Icon(s_new_text_but, "shape-text");
		Icon(s_new_image_but, "shape-image");
		Icon(image_mode_but, "mode-image");
		Icon(s_morth_but, "tool-morph");

		// Порядок и группировка
		Icon(s_bring_to_front_but, "order-bring-front");
		Icon(s_send_back_but, "order-send-back");
		Icon(s_group_but, "group");
		Icon(s_ungroup_but, "ungroup");

		// Отражение и поворот
		Icon(flip_horizontal_but, "flip-horizontal");
		Icon(flip_vertical_but, "flip-vertical");
		Icon(rotate_CW_90_but, "rotate-cw");
		Icon(rotate_CCW_90_but, "rotate-ccw");

		// Растровый редактор
		Icon(i_brush_but, "img-brush");
		Icon(i_erasor_but, "img-eraser");
		Icon(i_fill_but, "img-fill");
		Icon(i_select_rect_but, "img-select-rect");
		Icon(i_select_lasso_but, "img-select-lasso");
		Icon(i_light_but, "img-lighten");
		Icon(i_dark_but, "img-darken");
		Icon(i_contrast_but, "img-contrast");
		Icon(i_blackwhite_but, "img-blackwhite");
	}

	/// <summary>
	/// Пункты меню, у которых значков не было вовсе: всё меню File,
	/// Help и подменю Custom shapes.
	/// </summary>
	private void RegisterMenuIcons()
	{
		ThemeImages.Add(menu_file_new, "menu_file_new");
		ThemeImages.Add(menu_file_open, "menu_file_open");
		ThemeImages.Add(menu_file_import, "menu_file_import");
		ThemeImages.Add(menu_file_save, "menu_file_save");
		ThemeImages.Add(menu_file_save_as, "menu_file_save_as");
		ThemeImages.Add(menu_file_export, "menu_file_export");
		ThemeImages.Add(menu_file_print, "menu_file_print");
		ThemeImages.Add(menu_file_close, "menu_file_close");
		ThemeImages.Add(menu_settings, "menu_settings");

		ThemeImages.Add(menu_manual, "menu_manual");
		ThemeImages.Add(menu_about, "menu_about");

		ThemeImages.Add(menu_obj_add_to_custom_shapes, "menu_obj_add_to_custom_shapes");
		ThemeImages.Add(menu_obj_import_vdrs, "menu_obj_import_vdrs");
		ThemeImages.Add(menu_obj_import_visio, "menu_obj_import_visio");

		ThemeImages.Add(convertToBitmapToolStripMenuItem, "convertToBitmapToolStripMenuItem");
	}
}
