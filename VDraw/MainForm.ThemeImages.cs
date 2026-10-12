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
	}
}
